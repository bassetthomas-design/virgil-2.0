using Virgil.Contracts;

namespace Virgil.Core.Updates;

public sealed record StorePackage(string ProductId, string FamilyName, string Name, string Version);
public sealed record StoreSearch(bool Supported, string Reason, IReadOnlyList<StorePackage> Packages);
public sealed record StoreStart(bool Accepted, string Reason)
{
    public bool WasUnsupported { get; init; }
}
public interface IStoreUpdateClient
{
    Task<StoreSearch> SearchAsync(CancellationToken token);
    Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token);
    Task<StoreStart> StartAsync(StorePackage package, CancellationToken token);
}
public interface IStoreUpdateBackend
{
    StoreUpdateService StoreUpdates { get; }
}

public sealed class StoreUpdateService(IStoreUpdateClient client)
{
    private readonly Dictionary<string, StorePackage> _packages = new(StringComparer.OrdinalIgnoreCase);
    public const string PartialStepId = "store-partial";
    public const string StatusStepId = "store-current";
    public const string PartialStatus = "vérification partielle";
    public UpdateSequenceStepResult? Partial { get; private set; }
    public UpdateSequenceStepResult? ScreenCard { get; private set; }
    private string? _unsupportedReason;
    private IReadOnlyList<UpdateItem> _managed = [];

    public async Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> applications, CancellationToken token)
    {
        _packages.Clear();
        Partial = null;
        ScreenCard = null;
        _managed = applications.Where(a => a.Source == UpdateSource.MicrosoftStore).ToArray();
        var search = await SearchAsync(token).ConfigureAwait(false);
        _unsupportedReason = search.Supported ? null : search.Reason;
        var unmatched = search.Packages.Count(p => string.IsNullOrWhiteSpace(p.Version) && !IsManaged(p));
        var detected = search.Packages.Where(p => !string.IsNullOrWhiteSpace(p.Version)).ToArray();
        if (!search.Supported) Partial = PartialCard(search.Reason);
        else if (unmatched > 0)
            Partial = PartialCard(unmatched == 1
                ? "Une mise à jour proposée par le Microsoft Store ne correspond à aucune application installée sur ce compte ; Virgil ne l'installe pas."
                : $"{unmatched} mises à jour proposées par le Microsoft Store ne correspondent à aucune application installée sur ce compte ; Virgil ne les installe pas.");
        if (!search.Supported || detected.Length == 0)
        {
            if (search.Supported && unmatched == 0)
                ScreenCard = StatusCard("Aucune mise à jour détectée dans le Microsoft Store.");
            return [new("store", "Rechercher les mises à jour du Microsoft Store", UpdateComponent.Store)];
        }
        foreach (var package in detected.Where(p => !IsManaged(p)))
            _packages.TryAdd(package.ProductId, package);
        if (_packages.Count == 0 && Partial is null)
            ScreenCard = StatusCard("Les mises à jour du Microsoft Store détectées sont déjà suivies par WinGet.");
        return _packages.Values.Select(p => new UpdateSequenceStep(p.ProductId, p.Name, UpdateComponent.Store,
            new UpdateItem { Id = p.ProductId, Name = p.Name, InstalledVersion = p.Version, Scope = p.FamilyName, Source = UpdateSource.MicrosoftStore })).ToArray();
    }

    public async Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token)
    {
        if (_unsupportedReason is not null) return Unsupported(step, _unsupportedReason);
        if (step.Application is null)
        {
            var check = await VerifyAsync(token).ConfigureAwait(false);
            return new(step, check.IsCurrent, false, check.Detail) { VerifiedStatus = check.Status, WasUnavailable = check.Status == "non pris en charge automatiquement" };
        }
        // Le paquet doit appartenir à l'aperçu figé : aucun paquet découvert après confirmation n'est installé.
        if (!_packages.TryGetValue(step.Id, out var package) || package.FamilyName != step.Application.Scope)
            return new(step, false, false, "Installation bloquée : application absente de l'aperçu confirmé.") { VerifiedStatus = "bloquée" };
        token.ThrowIfCancellationRequested();
        IReadOnlyDictionary<string, string> before;
        try { before = await client.ReadVersionsAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return Unsupported(step, "La version installée ne peut pas être lue avec ce compte."); }
        if (!before.ContainsKey(package.FamilyName))
            return Unsupported(step, "La version de cette application ne peut pas être vérifiée avant l'installation.");
        StoreStart started;
        try { started = await client.StartAsync(package, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return Unsupported(step, "Le système refuse la mise à jour avec les droits ou le contexte de ce compte."); }
        if (!started.Accepted)
        {
            if (started.WasUnsupported) return Unsupported(step, started.Reason);
            var failure = new UpdateSequenceStepResult(step, false, false,
                string.IsNullOrWhiteSpace(started.Reason) ? "Échec de l'installation." : started.Reason);
            return failure with { VerifiedStatus = UpdateSequenceService.IsSafetyBlocked(failure) ? "bloquée" : "en erreur" };
        }
        // Une installation lancée se termine ; l'annulation bloque l'application suivante.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            var after = await client.ReadVersionsAsync(timeout.Token).ConfigureAwait(false);
            var rescan = await SearchAsync(timeout.Token).ConfigureAwait(false);
            before.TryGetValue(package.FamilyName, out var oldVersion);
            after.TryGetValue(package.FamilyName, out var newVersion);
            var increased = Version.TryParse(oldVersion, out var oldValue) && Version.TryParse(newVersion, out var newValue) && newValue > oldValue;
            var gone = after.ContainsKey(package.FamilyName) && rescan.Supported && !rescan.Packages.Any(p => p.ProductId == package.ProductId || p.FamilyName == package.FamilyName);
            var status = increased || gone ? "mis à jour" : rescan.Supported ? "reste à installer" : "en erreur";
            var detail = $"Version avant : {oldVersion ?? "non disponible"} ; après : {newVersion ?? "non disponible"}. "
                + (increased ? "La version installée a augmenté." : gone ? "Cette application n'est plus proposée au contrôle final."
                    : rescan.Supported ? "Cette application reste proposée au contrôle final." : "Le contrôle final n'a pas pu confirmer l'installation.");
            return new(step, increased || gone, false, detail) { VerifiedStatus = status };
        }
        catch { return new(step, false, false, "Installation lancée ; preuve finale indisponible.") { VerifiedStatus = "en erreur" }; }
    }

    public async Task<UpdateSourceVerification> VerifyAsync(CancellationToken token)
    {
        var search = await SearchAsync(token).ConfigureAwait(false);
        if (!search.Supported) return new(UpdateComponent.Store, "non pris en charge automatiquement", search.Reason);
        var remaining = search.Packages.Any(p => !string.IsNullOrWhiteSpace(p.Version) && !IsManaged(p));
        return new(UpdateComponent.Store, remaining ? "reste à installer" : _packages.Count == 0 ? "à jour" : "mis à jour",
            remaining ? "Des applications restent proposées au contrôle final." : "Contrôle final : aucune mise à jour sélectionnée ne reste proposée.") { HasRemaining = remaining };
    }
    private async Task<StoreSearch> SearchAsync(CancellationToken token)
    {
        try { return await client.SearchAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return new(false, "Le système refuse la recherche avec les droits ou le contexte de ce compte.", []); }
    }
    private bool IsManaged(StorePackage package) => _managed.Any(a =>
        string.Equals(a.Id, package.ProductId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(a.Id, package.FamilyName, StringComparison.OrdinalIgnoreCase));

    public static UpdateSequenceStepResult PartialCard(string reason) =>
        new(new(PartialStepId, "Microsoft Store", UpdateComponent.Store) { InformationOnly = true }, false, false,
            string.IsNullOrWhiteSpace(reason) ? "Lecture du Microsoft Store indisponible." : reason)
        { WasUnavailable = true, VerifiedStatus = PartialStatus };

    private static UpdateSequenceStepResult StatusCard(string detail) =>
        new(new(StatusStepId, "Microsoft Store", UpdateComponent.Store) { InformationOnly = true }, false, false, detail)
        { WasSkipped = true, VerifiedStatus = "à jour" };

    private static UpdateSequenceStepResult Unsupported(UpdateSequenceStep step, string reason) =>
        new(step, false, false, string.IsNullOrWhiteSpace(reason) ? "Les droits ou le contexte de ce compte ne permettent pas cette mise à jour." : reason)
        { WasUnavailable = true, VerifiedStatus = "non pris en charge automatiquement" };
}
