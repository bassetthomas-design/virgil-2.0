using System.Text.Json;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Updates;

public enum UpdateComponent { Windows, Defender, Driver, Winget, Launcher, Restore, MicrosoftUpdate, Store, WindowsFeatureUpgrade, WingetFinal, PendingReboot, OutsideWinget }
public sealed record UpdateSequenceStep(string Id, string Title, UpdateComponent Component, UpdateItem? Application = null)
{
    public bool IsSingleItem { get; init; }
    public SteamSession? SteamSession { get; init; }
    public bool InformationOnly { get; init; }
    public bool IsInformational => InformationOnly || Component is UpdateComponent.Restore or UpdateComponent.PendingReboot or UpdateComponent.OutsideWinget;
}
public sealed record UpdateSequenceProgress(int Current, int Total, string Title, string Status)
{
    public string? StepId { get; init; }
    public UpdateComponent? Component { get; init; }
}
public sealed record UpdateSourceVerification(UpdateComponent Component, string Status, string Detail)
{
    public bool? HasRemaining { get; init; }
    public IReadOnlyList<DriverUpdateOutcome> DriverItems { get; init; } = [];
    public bool IsCurrent => Status is "à jour" or "mis à jour";
}
public sealed record UpdateSequenceStepResult(UpdateSequenceStep Step, bool Succeeded, bool RebootRequired, string Message)
{
    public string? VerifiedStatus { get; init; }
    public bool WasSkipped { get; init; }
    public bool WasUnavailable { get; init; }
    public bool? RestorePointVerified { get; init; }
}
public sealed record UpdateSequenceResult(bool WasRefused, bool WasCancelled, bool RebootRequired, IReadOnlyList<UpdateSequenceStepResult> Steps, string Message)
{
    public IReadOnlyList<UpdateSourceVerification> Verifications { get; init; } = [];
    public IReadOnlyList<UpdateItem> UnsupportedApplications { get; init; } = [];
    public int SuccessfulCount => Steps.Count(step => !step.Step.IsInformational && !step.WasSkipped && !step.WasUnavailable && step.Succeeded);
    public bool HasFailures => Steps.Any(step => !step.Step.IsInformational && !step.WasSkipped && !step.WasUnavailable && !step.Succeeded);
}
public interface IUpdateSequenceBackend
{
    Task<UpdateSourceVerification> VerifyAsync(UpdateComponent component, CancellationToken token) =>
        Task.FromResult(new UpdateSourceVerification(component, "indisponible", "Contrôle final non disponible sur ce PC."));
    Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    void BeginExecution(string restoreSessionId) { }
    async Task<IReadOnlyList<UpdateSequenceStepResult>> ExecuteGroupAsync(IReadOnlyList<UpdateSequenceStep> steps, CancellationToken token)
    {
        var results = new List<UpdateSequenceStepResult>();
        foreach (var step in steps)
        {
            if (token.IsCancellationRequested) break;
            results.Add(await ExecuteAsync(step, token).ConfigureAwait(false));
        }
        return results;
    }
    Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken cancellationToken);
}
public interface IUpdateSequenceService
{
    IReadOnlyList<UpdateSequenceStepResult> SingleItems => [];
    Task<IReadOnlyList<UpdateSequenceStepResult>> ReadSingleItemsAsync(CancellationToken token) => Task.FromResult(SingleItems);
    Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> apps, CancellationToken cancellationToken);
    ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> selected);
    Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> selected, ConfirmedPlan? plan, IProgress<UpdateSequenceProgress>? progress, CancellationToken cancellationToken);
}
public sealed class UpdateSequenceService(IUpdateSequenceBackend backend, IConfirmedPlanGate gate, IPendingRebootProbe? rebootProbe = null, LauncherShutdownPolicy? launcherShutdown = null) : IUpdateSequenceService
{
    public IReadOnlyList<UpdateSequenceStepResult> SingleItems { get; private set; } = [];
    private IReadOnlyList<UpdateItem> _unsupported = [];
    private IReadOnlyList<UpdateSequenceStepResult> _driverInformation = [];
    private IReadOnlyList<DriverPackageOffer> _driverOffers = [];
    public async Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> apps, CancellationToken cancellationToken)
    {
        var steps = new List<UpdateSequenceStep>
        {
            new("restore", "Point de restauration", UpdateComponent.Restore),
            new("defender", "Signatures Microsoft Defender", UpdateComponent.Defender),
            new("microsoft-update", "Enregistrer Microsoft Update (Office et produits Microsoft)", UpdateComponent.MicrosoftUpdate)
        };
        var launchers = LauncherCatalog.Detect(await backend.DetectLaunchersAsync(cancellationToken).ConfigureAwait(false));
        foreach (var name in launchers)
        {
            SteamSession? session = null;
            if (name == "Steam")
            {
                try { session = backend is ISteamUpdateBackend steam ? await steam.SteamClient.ReadSessionAsync(cancellationToken).ConfigureAwait(false) : new(false, null); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { session = new(false, null); }
            }
            steps.Add(new(name, name, UpdateComponent.Launcher) { SteamSession = session });
        }
        _driverInformation = []; _driverOffers = [];
        steps.AddRange(apps.Where(a => a.Status == UpdateItemStatus.Available && !DriverOfficialPackageCatalog.IsFirmware(a)
            && !(backend is IDriverSequenceBackend && DriverOfficialPackageCatalog.IsDriverPackage(a)))
            .Select(a => new UpdateSequenceStep(a.Id, a.Name, UpdateComponent.Winget, a)));
        steps.AddRange([
            new("software", "Windows Update : logiciels (jusqu'à 3 passes)", UpdateComponent.Windows),
            new("drivers", "Windows Update : pilotes (jusqu'à 3 passes)", UpdateComponent.Driver),
            new("store", "Rechercher les mises à jour du Microsoft Store", UpdateComponent.Store),
            new("windows-feature-upgrade", "Nouvelle version de Windows (si disponible)", UpdateComponent.WindowsFeatureUpgrade),
            new("winget-final", "Passe finale : applications disponibles", UpdateComponent.WingetFinal),
            new("pending-reboot", "Vérifier le redémarrage en attente", UpdateComponent.PendingReboot)
        ]);
        if (backend is IDriverSequenceBackend driverBackend)
        {
            driverBackend.DriverPackages.SetInventory(apps);
            var inventory = await driverBackend.DriverInventory.ReadAsync(cancellationToken).ConfigureAwait(false);
            var bios = await driverBackend.Bios.ReadAsync(cancellationToken).ConfigureAwait(false);
            _driverOffers = await driverBackend.DriverPackages.ScanAsync(cancellationToken).ConfigureAwait(false);
            var information = new List<UpdateSequenceStepResult>();
            void Info(string id, string title, string reason, UpdateItem? item = null) => information.Add(new(
                new(id, title, UpdateComponent.Driver, item) { InformationOnly = true }, false, false, reason)
                { VerifiedStatus = DriverCardSummary.Unsupported, WasSkipped = true });
            Info("BIOS", "BIOS : information seulement, jamais installé par Virgil" + (bios.Version.Length > 0 ? " — " + bios.Version : " — version non disponible"),
                "Virgil n'installe jamais le BIOS ; passez par l'outil officiel du fabricant de l'ordinateur si une mise à jour est proposée.");
            foreach (var provider in inventory.Drivers.Select(driver => driver.Provider).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
                Info("Provider:" + provider, provider,
                    "Ce pilote n'est proposé ni par Windows Update ni par une source officielle connue ; passez par l'outil du fabricant si besoin.");
            if (inventory.Errors.Count > 0) Info("inventory", "Matériel : lecture incomplète", "Inventaire du matériel incomplet ; les fournisseurs ne sont pas tous connus.");
            var packages = new List<UpdateSequenceStep>();
            foreach (var offer in _driverOffers)
            {
                if (DriverOfficialPackageCatalog.IsFirmware(offer.Item)) continue;
                if (DriverOfficialPackageCatalog.CanInstall(offer)) packages.Add(new("WinGet:" + offer.Item.Id, offer.Item.Name, UpdateComponent.Driver, offer.Item));
                else Info("WinGet:" + offer.Item.Id, offer.Item.Name, string.IsNullOrWhiteSpace(offer.Reason)
                    ? "Ce paquet n'est pas dans la liste officielle des pilotes installables sans intervention." : offer.Reason, offer.Item);
            }
            _driverInformation = information;
            steps.InsertRange(steps.FindIndex(s => s.Id == "drivers") + 1, packages);
            steps.AddRange(information.Select(r => r.Step));
        }
        if (backend is IStoreUpdateBackend storeBackend)
        {
            steps.RemoveAll(step => step.Component == UpdateComponent.Store);
            var storeSteps = await storeBackend.StoreUpdates.ScanAsync(apps, cancellationToken).ConfigureAwait(false);
            steps.InsertRange(steps.FindIndex(step => step.Component == UpdateComponent.WindowsFeatureUpgrade), storeSteps);
        }
        _unsupported = backend is IOutsideWingetBackend outside
            ? await outside.OutsideWinget.ScanAsync(cancellationToken).ConfigureAwait(false) : [];
        steps.AddRange(_unsupported.Select(app => new UpdateSequenceStep(app.Id, app.Name, UpdateComponent.OutsideWinget, app)));
        var cards = steps.Where(IsIndividualCandidate).Select(step =>
        {
            if (step.Component == UpdateComponent.Launcher)
                step = step with { Application = apps.FirstOrDefault(app => app.Status == UpdateItemStatus.Available
                    && LauncherCatalog.Detect([app.Name]).Contains(step.Id, StringComparer.OrdinalIgnoreCase)) };
            var unsupported = step.IsInformational || (step.Component == UpdateComponent.Launcher
                && (step.Id == "Steam" ? step.SteamSession?.IsOpen != true : step.Application is null));
            var reason = step.Component == UpdateComponent.Launcher && unsupported
                ? step.Id == "Steam" ? LauncherCardSummary.NoSession : LauncherCardSummary.OtherGames
                : step.Application?.Message ?? "Cette mise à jour reste à installer.";
            return new UpdateSequenceStepResult(step, false, false, reason)
                { VerifiedStatus = unsupported ? DriverCardSummary.Unsupported : "reste à installer" };
        }).ToList();
        foreach (var information in _driverInformation)
        {
            cards.RemoveAll(c => c.Step.Component == information.Step.Component && c.Step.Id == information.Step.Id);
            cards.Add(information);
        }
        SingleItems = cards;
        return steps;
    }
    public async Task<IReadOnlyList<UpdateSequenceStepResult>> ReadSingleItemsAsync(CancellationToken cancellationToken)
    {
        var cards = SingleItems.ToList();
        if (backend is IDriverSequenceBackend)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                var check = await backend.VerifyAsync(UpdateComponent.Driver, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                cards.AddRange(check.DriverItems.Select(item => new UpdateSequenceStepResult(
                    new("WU:" + item.Identity, item.Title, UpdateComponent.Driver) { InformationOnly = DriverOfficialPackageCatalog.IsFirmware(new() { Name = item.Title }) },
                    false, false, DriverOfficialPackageCatalog.IsFirmware(new() { Name = item.Title })
                        ? "Virgil n'installe jamais le BIOS ni le firmware ; passez par l'outil officiel du fabricant de l'ordinateur." : item.Message)
                    { VerifiedStatus = DriverOfficialPackageCatalog.IsFirmware(new() { Name = item.Title }) ? DriverCardSummary.Unsupported : "reste à installer" }));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { /* Une lecture indisponible ne crée aucun élément installable. */ }
        }
        SingleItems = cards;
        return cards;
    }
    private static bool IsIndividualCandidate(UpdateSequenceStep step) => step.Component is UpdateComponent.Winget or UpdateComponent.OutsideWinget
        || step.Component == UpdateComponent.Store && step.Application is not null
        || step.Component == UpdateComponent.Launcher && step.Id != "launchers"
        || step.Component == UpdateComponent.Driver && step.Id != "drivers";
    public ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> selected)
    {
        selected = selected.Where(step => !step.IsInformational).ToArray();
        if (selected.Count == 1 && selected[0].IsSingleItem)
            return UpdateActionPreviewFactory.CreateSingle(selected[0]);
        return new(
        "updates.sequence", "Tout mettre à jour", RiskLevel.Sensitive,
        selected.Select(s => s.Component == UpdateComponent.Launcher && s.Id != "launchers" ? LauncherCardSummary.Confirmation(s) : s.Title)
            .Concat(_driverInformation.Select(r => r.Step.Title + (r.Step.Id == "BIOS" ? "" : " — " + r.VerifiedStatus))).ToArray(),
        new Dictionary<string, string> { ["Steps"] = JsonSerializer.Serialize(selected) },
        new[] { "Installer les composants cochés dans l’ordre présenté.", "Un redémarrage peut être nécessaire ; il restera manuel.", "Les pilotes et Windows exigent un point de restauration vérifié." }
            .Concat(selected.Where(s => s.Component == UpdateComponent.Driver).Select(s => s.Title))
            .Concat(_driverInformation.Select(r => r.Step.Title + (r.Step.Id == "BIOS" ? "" : " — " + r.VerifiedStatus))).ToArray(),
        selected.Any(s => IsElevated(s.Component)), true, Reversibility.Partial, ["Point de restauration"]);
    }
    public static bool IsElevated(UpdateComponent component) => component is UpdateComponent.Windows or UpdateComponent.Driver or UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate or UpdateComponent.Store or UpdateComponent.WindowsFeatureUpgrade;
    public static bool IsSafetyBlocked(UpdateSequenceStepResult result) => !result.Succeeded && !result.WasUnavailable
        && (result.RestorePointVerified == false || result.Message.Contains("bloquée", StringComparison.OrdinalIgnoreCase)
            || result.Message.Contains("garde de sécurité", StringComparison.OrdinalIgnoreCase)
            || (result.Message.Contains("refus", StringComparison.OrdinalIgnoreCase) && result.Message.Contains("hors catalogue", StringComparison.OrdinalIgnoreCase)));

    public async Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> selected, ConfirmedPlan? plan, IProgress<UpdateSequenceProgress>? progress, CancellationToken cancellationToken)
    {
        var snapshot = selected.Where(s => !s.IsInformational).Select(s => s with { Application = s.Application is null ? null : s.Application with { } }).ToArray();
        var validation = gate.TryConsume(plan, CreatePreview(snapshot));
        if (!validation.IsAccepted) return new(true, false, false, [], validation.Message);
        var single = snapshot.Any(s => s.IsSingleItem);
        if (single && (snapshot.Length != 1 || !IsSingleTarget(snapshot[0])))
            return new(true, false, false, [], "Élément non pris en charge automatiquement.");
        backend.BeginExecution(Guid.NewGuid().ToString("D"));
        var results = new List<UpdateSequenceStepResult>();
        var handled = new HashSet<(UpdateComponent Component, string Id)>();
        var microsoftBlocked = false;
        var steamStep = snapshot.FirstOrDefault(s => s.Component == UpdateComponent.Launcher && s.Id == "Steam" && s.SteamSession?.IsOpen == true);
        IReadOnlyList<SteamGameState> steamGames = [];
        string? steamReadError = null;
        string? steamGuardError = null;
        var steamMayRestart = false;
        if (steamStep is not null && backend is ISteamUpdateBackend steamBackend && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var check = await steamBackend.SteamClient.CheckExecutableAsync(steamStep.SteamSession?.ExecutablePath, cancellationToken).ConfigureAwait(false);
                if (!check.IsValid) steamGuardError = check.Reason;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch { steamGuardError = "Steam : relance bloquée, vérification du fichier impossible."; }
            if (steamGuardError is null && !cancellationToken.IsCancellationRequested)
            {
                try { steamGames = await steamBackend.SteamClient.ReadGamesAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch { steamReadError = "Steam : lecture des jeux impossible. Aucune relance automatique."; }
            }
        }
        if (snapshot.Any(s => IsElevated(s.Component)))
        {
            var preparation = snapshot.Where(s => s.Component is UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate).ToArray();
            if (!cancellationToken.IsCancellationRequested)
            {
                foreach (var member in preparation) handled.Add((member.Component, member.Id));
                progress?.Report(new(1, snapshot.Length, "Point de restauration et préparation Microsoft Update", "En cours"));
                IReadOnlyList<UpdateSequenceStepResult> protectedResults;
                try { protectedResults = await backend.ExecuteGroupAsync(preparation, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { protectedResults = []; }
                catch
                {
                    protectedResults = preparation.Length > 0
                        ? preparation.Select(s => new UpdateSequenceStepResult(s, false, false, "Installation bloquée : préparation non vérifiée.")).ToArray()
                        : [new(new("restore", "Point de restauration", UpdateComponent.Restore), false, false, "Installation bloquée : préparation non vérifiée.")];
                }
                results.AddRange(protectedResults);
                microsoftBlocked = protectedResults.Any(r => r.RestorePointVerified == false) ||
                    (protectedResults.All(r => r.RestorePointVerified is null) && protectedResults.Any(r => !r.Succeeded));
            }
        }
        foreach (var step in snapshot)
        {
            if (handled.Contains((step.Component, step.Id))) continue;
            if (cancellationToken.IsCancellationRequested) break;
            if (microsoftBlocked && IsElevated(step.Component))
            {
                results.Add(new(step, false, false, "Installation Microsoft bloquée : point de restauration non vérifié."));
                handled.Add((step.Component, step.Id));
                continue;
            }
            progress?.Report(new(results.Count + 1, snapshot.Length, step.Title, "En cours") { StepId = step.Id, Component = step.Component });
            try
            {
                var applicationLauncher = single && step.Component == UpdateComponent.Winget && step.Application is not null
                    ? LauncherCatalog.Detect([step.Application.Name]).FirstOrDefault() : null;
                if (applicationLauncher is not null)
                {
                    var shutdown = launcherShutdown is null ? null : await launcherShutdown.ExecuteAsync([applicationLauncher], cancellationToken).ConfigureAwait(false);
                    if (shutdown?.ClosedOrAlreadyClosed != true)
                    {
                        results.Add(new(step, false, false, "Non lancée : un jeu tourne ou la fermeture du lanceur n'est pas vérifiée.") { WasSkipped = true, VerifiedStatus = "non lancée" });
                        handled.Add((step.Component, step.Id)); continue;
                    }
                }
                if (step.Component == UpdateComponent.WingetFinal && backend is IDriverFinalPassBackend finalBackend)
                {
                    var inventory = await finalBackend.ReadFinalApplicationsAsync(cancellationToken).ConfigureAwait(false);
                    if (inventory.IsComplete && !inventory.Items.Any(app => DriverOfficialPackageCatalog.IsFirmware(app) || DriverOfficialPackageCatalog.IsDriverPackage(app)))
                        results.Add(await backend.ExecuteAsync(step, cancellationToken).ConfigureAwait(false));
                    else
                    {
                        var children = new List<UpdateSequenceStepResult>();
                        foreach (var app in inventory.Items)
                        {
                            if (DriverOfficialPackageCatalog.IsFirmware(app) || DriverOfficialPackageCatalog.IsDriverPackage(app))
                                children.Add(new(new("WinGet:" + app.Id, app.Name, UpdateComponent.Driver, app) { InformationOnly = true }, false, false,
                                    DriverOfficialPackageCatalog.IsFirmware(app)
                                        ? "Virgil n'installe jamais le BIOS ni le firmware ; passez par l'outil officiel du fabricant de l'ordinateur si une mise à jour est proposée."
                                        : "Ce pilote exige une source officielle silencieuse et un point de restauration vérifié ; il n'est pas lancé dans la passe des logiciels.")
                                    { VerifiedStatus = DriverCardSummary.Unsupported, WasSkipped = true });
                            else
                            {
                                var child = new UpdateSequenceStep(app.Id, app.Name, UpdateComponent.Winget, app);
                                children.Add(cancellationToken.IsCancellationRequested
                                    ? new(child, false, false, "Non lancée : annulation demandée.") { WasSkipped = true }
                                    : await backend.ExecuteAsync(child, cancellationToken).ConfigureAwait(false));
                            }
                        }
                        results.AddRange(children);
                        results.Add(new(step, children.Any(r => r.Succeeded) && children.All(r => r.Step.IsInformational || r.Succeeded),
                            children.Any(r => r.RebootRequired), inventory.Detail)
                            { VerifiedStatus = "à vérifier", WasUnavailable = children.Count == 0 && !inventory.IsComplete });
                    }
                    handled.Add((step.Component, step.Id));
                }
                else if (step.Component == UpdateComponent.Driver && step.Id.StartsWith("WinGet:", StringComparison.Ordinal))
                {
                    var offer = _driverOffers.FirstOrDefault(o => o.Item == step.Application && step.Id == "WinGet:" + o.Item.Id);
                    results.Add(backend is IDriverSequenceBackend driverBackend && offer is not null && DriverOfficialPackageCatalog.CanInstall(offer)
                        ? await driverBackend.DriverPackages.InstallAsync(step, cancellationToken).ConfigureAwait(false)
                        : new(step, false, false, "Installation bloquée : paquet officiel non vérifié.") { VerifiedStatus = "bloquée" });
                    handled.Add((step.Component, step.Id));
                }
                else if (step.Component == UpdateComponent.Launcher)
                {
                    if (single && step.Id == "Steam" && (steamGuardError is not null || steamReadError is not null))
                    { handled.Add((step.Component, step.Id)); continue; }
                    if (single && step.Id == "Steam" && step.SteamSession?.IsOpen != true)
                    {
                        results.Add(new(step, false, false, LauncherCardSummary.NoSession) { VerifiedStatus = LauncherCardSummary.Unsupported, WasSkipped = true });
                        handled.Add((step.Component, step.Id)); continue;
                    }
                    var names = step.Id == "launchers" ? step.Title.Replace("Virgil fermera : ", "", StringComparison.Ordinal).Split(", ") : new[] { step.Id };
                    var shutdown = launcherShutdown is null ? new LauncherShutdownResult(false, "mis à jour au prochain lancement") : await launcherShutdown.ExecuteAsync(names, cancellationToken).ConfigureAwait(false);
                    if (step.Id == "Steam") steamMayRestart = shutdown.ClosedOrAlreadyClosed;
                    if (single && step.Application is not null && shutdown.ClosedOrAlreadyClosed)
                    {
                        var package = await backend.ExecuteAsync(new(step.Application.Id, step.Application.Name, UpdateComponent.Winget, step.Application), cancellationToken).ConfigureAwait(false);
                        if (!package.Succeeded) { results.Add(package with { Step = step }); steamMayRestart = false; steamReadError = "Steam : la mise à jour a échoué. Aucune relance automatique."; handled.Add((step.Component, step.Id)); continue; }
                        if (step.Id != "Steam")
                        {
                            results.Add(package with { Step = step, VerifiedStatus = "à vérifier" });
                            handled.Add((step.Component, step.Id)); continue;
                        }
                    }
                    if (single && step.Application is not null && !shutdown.ClosedOrAlreadyClosed)
                    {
                        results.Add(new(step, false, false, "Non lancée : un jeu tourne ou la fermeture du lanceur n'est pas vérifiée.") { WasSkipped = true, VerifiedStatus = "non lancée" });
                        handled.Add((step.Component, step.Id)); continue;
                    }
                    if (step.Id == "launchers")
                        results.Add(new(step, shutdown.AnyClosed, false, shutdown.Message) { WasSkipped = !shutdown.AnyClosed });
                    else
                    {
                        var supported = step.Id == "Steam" && step.SteamSession?.IsOpen == true && backend is ISteamUpdateBackend;
                        var detail = shutdown.Message + "\n" + (supported ? "Jeux : contrôle Steam après les applications."
                            : step.Id == "Steam" ? "Jeux : non pris en charge automatiquement — " + LauncherCardSummary.NoSession : LauncherCardSummary.OtherGames);
                        results.Add(new(step, supported && shutdown.AnyClosed, false, detail)
                        { WasSkipped = !supported || !shutdown.AnyClosed, VerifiedStatus = supported ? "à vérifier" : LauncherCardSummary.Unsupported });
                    }
                    handled.Add((step.Component, step.Id));
                }
                else if (step.Component == UpdateComponent.Store && backend is IStoreUpdateBackend storeBackend)
                {
                    // Même plan et même préparation protégée ; arrêt entre deux applications.
                    results.Add(await storeBackend.StoreUpdates.ExecuteAsync(step, cancellationToken).ConfigureAwait(false));
                    handled.Add((step.Component, step.Id));
                }
                else if (IsElevated(step.Component))
                {
                    var prepare = step.Component is UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate;
                    var group = snapshot.Where(s => IsElevated(s.Component) && !(s.Component == UpdateComponent.Driver && s.Id.StartsWith("WinGet:", StringComparison.Ordinal))
                        && !(s.Component == UpdateComponent.Store && backend is IStoreUpdateBackend) && (s.Component is UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate) == prepare).ToArray();
                    foreach (var member in group) handled.Add((member.Component, member.Id));
                    results.AddRange(await backend.ExecuteGroupAsync(group, cancellationToken).ConfigureAwait(false));
                }
                else
                {
                    results.Add(await backend.ExecuteAsync(step, cancellationToken).ConfigureAwait(false));
                    handled.Add((step.Component, step.Id));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { results.Add(new(step, false, false, "Étape impossible. Aucune autre action pour ce composant.")); }
            if (cancellationToken.IsCancellationRequested)
                for (var index = 0; index < results.Count; index++)
                    if ((results[index].Step.Component != UpdateComponent.Driver || results[index].VerifiedStatus is null)
                        && !results[index].Message.Contains("Étape terminée après annulation.", StringComparison.Ordinal))
                        results[index] = results[index] with { Message = results[index].Message + " Étape terminée après annulation." };
            progress?.Report(new(results.Count, snapshot.Length, step.Title, cancellationToken.IsCancellationRequested ? "Étape terminée après annulation." : results.LastOrDefault()?.Message ?? "Étape terminée.") { StepId = step.Id, Component = step.Component });
        }
        if (steamStep is not null && backend is ISteamUpdateBackend steamSource)
        {
            var closure = results.FirstOrDefault(r => r.Step.Id == "Steam")?.Message ?? "Steam : déjà fermé ou non lancé.";
            results.RemoveAll(r => r.Step.Component == UpdateComponent.Launcher && r.Step.Id == "Steam");
            try
            {
                if (steamGuardError is not null)
                    results.Add(new(steamStep, false, false, steamGuardError) { VerifiedStatus = "bloquée" });
                else if (steamReadError is not null)
                    results.Add(new(steamStep, false, false, steamReadError) { VerifiedStatus = "en erreur" });
                else
                    results.AddRange(await new SteamUpdateService(steamSource).ExecuteAsync(steamStep, steamGames,
                        () => steamMayRestart && launcherShutdown is not null ? launcherShutdown.CanRestartSteamAsync(cancellationToken) : Task.FromResult(false),
                        results.Any(r => r.Step.Component == UpdateComponent.WingetFinal && r.Succeeded && !r.WasSkipped), closure,
                        () => progress?.Report(new(results.Count, snapshot.Length, steamStep.Title, LauncherCardSummary.Waiting)
                            { StepId = steamStep.Id, Component = steamStep.Component }), cancellationToken, single && steamMayRestart).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                results.Add(new(steamStep, false, false, "Non lancée : annulation avant la relance de Steam.") { VerifiedStatus = "non lancée", WasSkipped = true });
                results.AddRange(steamGames.Select(game => new UpdateSequenceStepResult(new("SteamGame:" + game.Id, game.Name, UpdateComponent.Launcher), false, false,
                    "Non lancée : annulation avant la relance de Steam.") { VerifiedStatus = "non lancée", WasSkipped = true }));
            }
            catch { results.Add(new(steamStep, false, false, "Steam : relance bloquée, vérification impossible.") { VerifiedStatus = "bloquée" }); }
            handled.Add((steamStep.Component, steamStep.Id));
        }
        results.AddRange(snapshot.Where(s => !handled.Contains((s.Component, s.Id)) && !results.Any(r => r.Step.Component == s.Component && r.Step.Id == s.Id)).Select(s => new UpdateSequenceStepResult(s, false, false, "Non lancée : annulation demandée.")));
        if (snapshot.Any(s => s.Component == UpdateComponent.Driver))
            results.AddRange(_driverInformation.Where(info => !info.Step.Id.StartsWith("Provider:", StringComparison.Ordinal)
                || !results.Any(r => r.Step.Component == UpdateComponent.Driver && !r.Step.IsInformational && r.Step.Id != "drivers"
                    && r.VerifiedStatus != DriverCardSummary.Unsupported && DriverOfficialPackageCatalog.Provider(r.Step.Title + " " + r.Step.Application?.Publisher)
                        .Contains(DriverOfficialPackageCatalog.Provider(info.Step.Id[9..]), StringComparison.OrdinalIgnoreCase))));
        // Lectures seulement : même après annulation, aucune installation supplémentaire.
        var checks = new List<UpdateSourceVerification>();
        foreach (var component in snapshot.Select(s => s.Component).Distinct().Where(c => IsElevated(c)))
        {
            UpdateSourceVerification check;
            using var verificationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { check = await (component == UpdateComponent.Store && backend is IStoreUpdateBackend storeBackend
                ? storeBackend.StoreUpdates.VerifyAsync(verificationTimeout.Token)
                : backend.VerifyAsync(component, verificationTimeout.Token)).WaitAsync(verificationTimeout.Token).ConfigureAwait(false); }
            catch { check = new(component, "indisponible", "Contrôle final indisponible ou délai dépassé."); }
            var outcomes = results.Where(r => r.Step.Component == component).ToArray();
            if (single && component == UpdateComponent.Driver && check.Status == "indisponible")
                for (var i = 0; i < results.Count; i++)
                    if (results[i].Step.Component == component && !results[i].Step.IsInformational && results[i].Succeeded)
                        results[i] = results[i] with { VerifiedStatus = "indisponible", Message = "Installation exécutée ; contrôle final de ce pilote indisponible." };
            if (component == UpdateComponent.Driver && outcomes.Any(r => r.VerifiedStatus is not null))
            {
                for (var i = 0; i < results.Count; i++)
                    if (results[i].Step.Id.StartsWith("WU:", StringComparison.Ordinal) && DriverCardSummary.Status(results[i]) is "mis à jour" or "à jour"
                        && check.DriverItems.Any(item => "WU:" + item.Identity == results[i].Step.Id))
                        results[i] = results[i] with { VerifiedStatus = "reste à installer", Message = "Ce pilote reste proposé après les installations." };
                results.AddRange(check.DriverItems.Where(item => !results.Any(r => r.Step.Id == "WU:" + item.Identity))
                    .Select(item => new UpdateSequenceStepResult(new("WU:" + item.Identity, item.Title, UpdateComponent.Driver), false, false,
                        "Ce pilote reste proposé après les installations.") { VerifiedStatus = "reste à installer" }));
                check = DriverCardSummary.Summarize(results.Where(r => r.Step.Component == component).ToArray());
            }
            else if (component == UpdateComponent.Store && backend is IStoreUpdateBackend)
                check = SummarizeOutcomes(check, outcomes);
            else if (outcomes.Length == 0 || outcomes.Any(r => r.Message.StartsWith("Non lancée", StringComparison.Ordinal)))
                check = new(component, "non lancée", check.Detail);
            else if (outcomes.Any(r => r.WasUnavailable)) check = new(component, "indisponible", check.Detail);
            else if (outcomes.Any(r => !r.Succeeded && !r.WasSkipped))
                check = new(component, outcomes.Any(IsSafetyBlocked) ? "bloquée" : "en erreur", check.Detail);
            else if (component == UpdateComponent.Store)
                check = new(component, "à vérifier", "à terminer dans le Microsoft Store");
            checks.Add(check with { Component = component });
        }
        if (!checks.Any(check => check.Component == UpdateComponent.Driver) && results.Any(r => r.Step.Component == UpdateComponent.Driver))
            checks.Add(DriverCardSummary.Summarize(results.Where(r => r.Step.Component == UpdateComponent.Driver).ToArray()));
        var launcherOutcomes = results.Where(r => r.Step.Component == UpdateComponent.Launcher && r.Step.Id != "launchers").ToArray();
        if (launcherOutcomes.Length > 0) checks.Add(LauncherCardSummary.Summarize(launcherOutcomes));
        var sources = new List<string>();
        if (results.Any(r => r.RebootRequired && r.Step.Component is UpdateComponent.Windows or UpdateComponent.Driver or UpdateComponent.WindowsFeatureUpgrade)) sources.Add("après Windows Update");
        if (results.Any(r => r.RebootRequired && r.Step.Component is not UpdateComponent.Windows and not UpdateComponent.Driver and not UpdateComponent.WindowsFeatureUpgrade)) sources.Add("après une installation");
        try
        {
            if (rebootProbe is not null) sources.AddRange(rebootProbe.Read().Sources.Select(source => "signalé par " + source));
            else checks.Add(new(UpdateComponent.PendingReboot, "indisponible", "Sonde de redémarrage absente."));
        }
        catch { sources.Add("Lecture du redémarrage indisponible"); }
        var reboot = sources.Count > 0;
        var verdict = "Redémarrage nécessaire : " + (reboot ? "oui, " + string.Join(" ; ", sources.Distinct()) : "non") + ".";
        results.Add(new(new("pending-reboot", "Vérifier le redémarrage en attente", UpdateComponent.PendingReboot), true, reboot, verdict));
        var message = cancellationToken.IsCancellationRequested ? "Séquence annulée entre deux étapes." : results.Any(r => !r.Succeeded && !r.Step.IsInformational && !r.WasSkipped && !r.WasUnavailable) ? "Séquence terminée avec erreurs ; consultez les étapes." : "Séquence terminée.";
        return new(false, cancellationToken.IsCancellationRequested, reboot, results, message) { Verifications = checks, UnsupportedApplications = _unsupported.ToArray() };
    }
    public static bool IsSingleTarget(UpdateSequenceStep step)
    {
        if (step.IsInformational || !IsIndividualCandidate(step)) return false;
        if (step.Component == UpdateComponent.Driver && step.Id.StartsWith("WU:", StringComparison.Ordinal))
        {
            var parts = step.Id[3..].Split(':');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "D", out _) && parts[1].Length > 0
                && parts[1].All(char.IsAsciiDigit) && int.TryParse(parts[1], out var revision) && revision >= 0;
        }
        if (step.Component == UpdateComponent.Launcher) return LauncherCatalog.Executables.ContainsKey(step.Id)
            && (step.Id == "Steam" || step.Application is not null);
        var item = step.Application;
        return item is not null && item.Status == UpdateItemStatus.Available && !item.IsIdTruncated && !item.Id.Contains('…')
            && item.Id.Length > 0 && !DriverOfficialPackageCatalog.IsFirmware(item)
            && (step.Component != UpdateComponent.Winget || !DriverOfficialPackageCatalog.IsDriverPackage(item))
            && item.RiskLevel != UpdateRiskLevel.CriticalInformationOnly
            && (step.Component == UpdateComponent.Store || (!string.IsNullOrWhiteSpace(item.AvailableVersion)
                && item.AvailableVersion.Any(char.IsAsciiDigit) && char.IsAsciiLetterOrDigit(item.AvailableVersion[0])
                && item.AvailableVersion.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+' or '~')));
    }
    public static UpdateSourceVerification SummarizeOutcomes(UpdateSourceVerification check, IReadOnlyList<UpdateSequenceStepResult> outcomes)
    {
        var blocked = outcomes.Any(IsSafetyBlocked);
        var failures = outcomes.Count(r => !r.Succeeded && !r.WasSkipped && !r.WasUnavailable && r.VerifiedStatus != "reste à installer"
            && !IsSafetyBlocked(r) && !r.Message.StartsWith("Non lancée", StringComparison.Ordinal));
        var unsupported = outcomes.Where(r => r.VerifiedStatus == "non pris en charge automatiquement").ToArray();
        var suffix = blocked ? "bloquée" : failures > 0 ? $"{failures} échec" + (failures > 1 ? "s" : "") : null;
        if (check.HasRemaining == true)
            return check with { Status = "reste à installer" + (suffix is null ? "" : " ; " + suffix)
                + (unsupported.Length > 0 ? " ; non pris en charge automatiquement" : ""),
                Detail = unsupported.Length > 0 ? check.Detail + " " + unsupported[0].Message : check.Detail };
        if (blocked) return check with { Status = "bloquée", Detail = "Installation bloquée par une garde de sécurité." };
        if (failures > 0) return check with { Status = "en erreur", Detail = "Échec pendant l'exécution de cette source ; consultez les cartes." };
        if (outcomes.Any(r => r.VerifiedStatus == "non pris en charge automatiquement"))
            return check with { Status = "non pris en charge automatiquement", Detail = outcomes.First(r => r.VerifiedStatus == "non pris en charge automatiquement").Message };
        return check;
    }

}
