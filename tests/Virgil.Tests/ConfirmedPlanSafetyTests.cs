using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Xunit;

namespace Virgil.Tests;

public sealed class ConfirmedPlanSafetyTests
{
    private static ActionPreview Preview() => new("test.action", "Action de test", RiskLevel.Low,
        ["cible A", "cible B"], new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" },
        ["effet A", "effet B"], false, false, Reversibility.Full, ["sauvegarde A", "sauvegarde B"]);

    private static ConfirmationConsent Consent(ActionPreview preview) => new(
        RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel),
        preview.RiskLevel == RiskLevel.Critical ? ConfirmationService.ExpectedCriticalText(preview) : null);

    [Theory]
    [InlineData(RiskLevel.Info, ConfirmationLevel.Simple)]
    [InlineData(RiskLevel.Low, ConfirmationLevel.Simple)]
    [InlineData(RiskLevel.Sensitive, ConfirmationLevel.Reinforced)]
    [InlineData(RiskLevel.Critical, ConfirmationLevel.Critical)]
    public void EachRiskIssuesWithRequiredConsentRegardlessOfAdmin(RiskLevel risk, ConfirmationLevel expected)
    {
        var service = new ConfirmationService(new FakeTimeProvider());
        foreach (var admin in new[] { false, true })
        {
            var preview = Preview() with { RiskLevel = risk, RequiresAdmin = admin };
            var plan = service.Issue(preview, Consent(preview));
            Assert.Equal(expected, plan.ConfirmationLevel);
            Assert.Equal(preview.ActionId, plan.ActionId);
            Assert.Equal(ActionPreviewHasher.Hash(preview), plan.PreviewHash);
            Assert.NotEqual(Guid.Empty, plan.PlanId);
            Assert.Equal(TimeSpan.FromMinutes(10), plan.ExpiresAt - plan.IssuedAt);
            Assert.True(service.Validate(plan, preview).IsAccepted);
        }
    }

    [Theory]
    [InlineData(RiskLevel.Sensitive, ConfirmationLevel.Simple)]
    [InlineData(RiskLevel.Critical, ConfirmationLevel.Simple)]
    [InlineData(RiskLevel.Critical, ConfirmationLevel.Reinforced)]
    [InlineData(RiskLevel.Low, (ConfirmationLevel)(-1))]
    [InlineData(RiskLevel.Low, (ConfirmationLevel)99)]
    public void InsufficientOrUndefinedConsentIsRefused(RiskLevel risk, ConfirmationLevel level)
    {
        var service = new ConfirmationService(new FakeTimeProvider());
        var preview = Preview() with { RiskLevel = risk };
        var refused = Assert.Throws<ConfirmationRefusedException>(() => service.Issue(preview, new(level, null)));
        Rejected(refused.Result, PlanRejectionReason.InsufficientConsent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("JE COMPRENDS ET JE CONFIRME")]
    [InlineData("je comprends et je confirme : test.action : cible A")]
    [InlineData("JE COMPRENDS ET JE CONFIRME : autre.action : cible A")]
    [InlineData("JE COMPRENDS ET JE CONFIRME : test.action : cible B")]
    [InlineData("JE COMPRENDS ET JE CONFIRME : test.action : cible A ")]
    public void CriticalTextMustMatchExactly(string? text)
    {
        var service = new ConfirmationService(new FakeTimeProvider());
        var preview = Preview() with { RiskLevel = RiskLevel.Critical };
        var refused = Assert.Throws<ConfirmationRefusedException>(() => service.Issue(preview, new(ConfirmationLevel.Critical, text)));
        Rejected(refused.Result, PlanRejectionReason.InsufficientConsent);
    }

    [Fact]
    public void CriticalTextBindsActionAndFirstTarget()
    {
        var preview = Preview();
        var text = ConfirmationService.ExpectedCriticalText(preview);
        Assert.Contains(preview.ActionId, text);
        Assert.Contains(preview.Targets[0], text);
        Assert.NotEqual(text, ConfirmationService.ExpectedCriticalText(preview with { ActionId = "autre" }));
        Assert.NotEqual(text, ConfirmationService.ExpectedCriticalText(preview with { Targets = ["autre"] }));
        var empty = preview with { RiskLevel = RiskLevel.Critical, Targets = [] };
        var service = new ConfirmationService(new FakeTimeProvider());
        Assert.True(service.Validate(service.Issue(empty, Consent(empty)), empty).IsAccepted);
    }

    [Fact]
    public void HigherConsentCanCoverLowerRisk()
    {
        var preview = Preview();
        var service = new ConfirmationService(new FakeTimeProvider());
        Assert.True(service.Validate(service.Issue(preview, new(ConfirmationLevel.Reinforced, null)), preview).IsAccepted);
    }

    [Theory]
    [InlineData(PlanRejectionReason.Missing)]
    [InlineData(PlanRejectionReason.UnknownProvenance)]
    [InlineData(PlanRejectionReason.ActionMismatch)]
    [InlineData(PlanRejectionReason.PreviewHashMismatch)]
    [InlineData(PlanRejectionReason.Expired)]
    [InlineData(PlanRejectionReason.AlreadyUsed)]
    [InlineData(PlanRejectionReason.InsufficientConsent)]
    public void EveryRefusalPreventsEffects(PlanRejectionReason reason)
    {
        var clock = new FakeTimeProvider();
        var service = new ConfirmationService(clock);
        var preview = Preview();
        ConfirmedPlan? plan = service.Issue(preview, Consent(preview));
        var action = new FakeAction(service);
        switch (reason)
        {
            case PlanRejectionReason.Missing: plan = null; break;
            case PlanRejectionReason.UnknownProvenance:
                plan = new ConfirmationService(clock).Issue(preview, Consent(preview)); break;
            case PlanRejectionReason.ActionMismatch: preview = preview with { ActionId = "autre" }; break;
            case PlanRejectionReason.PreviewHashMismatch: preview = preview with { Targets = ["autre"] }; break;
            case PlanRejectionReason.Expired: clock.Advance(TimeSpan.FromMinutes(10)); break;
            case PlanRejectionReason.AlreadyUsed: Assert.True(service.TryConsume(plan, preview).IsAccepted); break;
            case PlanRejectionReason.InsufficientConsent:
                preview = preview with { RiskLevel = RiskLevel.Critical };
                var refusal = Assert.Throws<ConfirmationRefusedException>(() =>
                    action.ConfirmAndExecute(service, preview, new(ConfirmationLevel.Simple, null)));
                Rejected(refusal.Result, reason);
                Assert.Equal(0, action.Effects);
                return;
        }
        Rejected(service.Validate(plan, preview), reason);
        Rejected(action.Execute(plan, preview), reason);
        Assert.Equal(0, action.Effects);
    }

    [Fact]
    public void ReflectionCloneHasUnknownProvenanceEvenWithAllIssuedValues()
    {
        var service = new ConfirmationService(new FakeTimeProvider());
        var preview = Preview();
        var original = service.Issue(preview, Consent(preview));
        var clone = Clone(original);
        Assert.Equal(original.PlanId, clone.PlanId);
        var action = new FakeAction(service);
        Rejected(action.Execute(clone, preview), PlanRejectionReason.UnknownProvenance);
        Assert.Equal(0, action.Effects);
        Assert.True(service.Validate(original, preview).IsAccepted);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("parameter")]
    [InlineData("effect")]
    public void ChangedScopeRefusesPlan(string field)
    {
        var preview = Preview();
        var service = new ConfirmationService(new FakeTimeProvider());
        var plan = service.Issue(preview, Consent(preview));
        var changed = field switch
        {
            "target" => preview with { Targets = ["cible C"] },
            "parameter" => preview with { Parameters = new Dictionary<string, string> { ["a"] = "3" } },
            _ => preview with { Effects = ["effet C"] }
        };
        var action = new FakeAction(service);
        Rejected(action.Execute(plan, changed), PlanRejectionReason.PreviewHashMismatch);
        Assert.Equal(0, action.Effects);
    }

    [Fact]
    public void ExpirationIncludesExactBoundary()
    {
        var clock = new FakeTimeProvider();
        var service = new ConfirmationService(clock);
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        Assert.Equal(clock.GetUtcNow(), plan.IssuedAt);
        clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1));
        Assert.True(service.Validate(plan, preview).IsAccepted);
        clock.Advance(TimeSpan.FromTicks(1));
        Rejected(service.Validate(plan, preview), PlanRejectionReason.Expired);
        clock.Advance(TimeSpan.FromTicks(1));
        Rejected(service.TryConsume(plan, preview), PlanRejectionReason.Expired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(901)]
    public void LifetimeOutsideBoundsIsRejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ConfirmationService(new FakeTimeProvider(), TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void MaximumLifetimeIsFifteenMinutes()
    {
        var clock = new FakeTimeProvider();
        var service = new ConfirmationService(clock, TimeSpan.FromMinutes(15));
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        Assert.Equal(TimeSpan.FromMinutes(15), plan.ExpiresAt - plan.IssuedAt);
        clock.Advance(TimeSpan.FromMinutes(15));
        Rejected(service.Validate(plan, preview), PlanRejectionReason.Expired);
    }

    [Fact]
    public void ValidationDoesNotConsumeAndSecondConsumptionIsRefused()
    {
        var service = new ConfirmationService(new FakeTimeProvider());
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        Assert.True(service.Validate(plan, preview).IsAccepted);
        Assert.True(service.Validate(plan, preview).IsAccepted);
        var action = new FakeAction(service);
        Assert.True(action.Execute(plan, preview).IsAccepted);
        Rejected(action.Execute(plan, preview), PlanRejectionReason.AlreadyUsed);
        Assert.Equal(1, action.Effects);
    }

    [Fact]
    public void ConcurrentConsumptionAllowsExactlyOneEffect()
    {
        var service = new ConfirmationService(new FakeTimeProvider());
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        var action = new FakeAction(service);
        Parallel.For(0, 32, _ => action.Execute(plan, preview));
        Assert.Equal(1, action.Effects);
    }

    [Fact]
    public void RejectionsFollowSpecifiedPriority()
    {
        var clock = new FakeTimeProvider();
        var service = new ConfirmationService(clock);
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        Assert.True(service.TryConsume(plan, preview).IsAccepted);
        clock.Advance(TimeSpan.FromMinutes(10));
        Rejected(service.Validate(null, preview), PlanRejectionReason.Missing);
        Rejected(service.Validate(Clone(plan), preview with { ActionId = "autre" }), PlanRejectionReason.UnknownProvenance);
        Rejected(service.Validate(plan, preview with { ActionId = "autre", Targets = [] }), PlanRejectionReason.ActionMismatch);
        Rejected(service.Validate(plan, preview with { Targets = [] }), PlanRejectionReason.PreviewHashMismatch);
        Rejected(service.Validate(plan, preview), PlanRejectionReason.Expired);
    }

    [Fact]
    public void HashIsStableAcrossDictionaryInsertionOrderAndCulture()
    {
        var preview = Preview();
        var hash = ActionPreviewHasher.Hash(preview);
        Assert.Matches("^[0-9A-F]{64}$", hash);
        Assert.Equal(hash, ActionPreviewHasher.Hash(Preview()));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal(hash, ActionPreviewHasher.Hash(preview with
            {
                Parameters = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }
            }));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    public static IEnumerable<object[]> ChangedFields()
    {
        for (var i = 0; i < 18; i++) yield return [i];
    }

    [Theory]
    [MemberData(nameof(ChangedFields))]
    public void HashCoversEveryFieldAndCollectionOrder(int field)
    {
        var p = Preview();
        var changed = field switch
        {
            0 => p with { ActionId = "autre" }, 1 => p with { Title = "Autre titre" },
            2 => p with { RiskLevel = RiskLevel.Critical }, 3 => p with { Targets = ["autre", "cible B"] },
            4 => p with { Targets = ["cible B", "cible A"] },
            5 => p with { Parameters = new Dictionary<string, string> { ["a"] = "nouveau", ["b"] = "2" } },
            6 => p with { Parameters = new Dictionary<string, string> { ["A"] = "1", ["b"] = "2" } },
            7 => p with { Effects = ["nouveau", "effet B"] }, 8 => p with { Effects = ["effet B", "effet A"] },
            9 => p with { RequiresAdmin = true }, 10 => p with { RebootRequired = true },
            11 => p with { Reversibility = Reversibility.None }, 12 => p with { RequiredBackups = ["nouvelle"] },
            13 => p with { RequiredBackups = ["sauvegarde B", "sauvegarde A"] },
            14 => p with { Targets = [] }, 15 => p with { Parameters = new Dictionary<string, string>() },
            16 => p with { Effects = [] }, _ => p with { RequiredBackups = [] }
        };
        Assert.NotEqual(ActionPreviewHasher.Hash(p), ActionPreviewHasher.Hash(changed));
        var service = new ConfirmationService(new FakeTimeProvider());
        var plan = service.Issue(p, Consent(p));
        var action = new FakeAction(service);
        Rejected(action.Execute(plan, changed), field == 0 ? PlanRejectionReason.ActionMismatch : PlanRejectionReason.PreviewHashMismatch);
        Assert.Equal(0, action.Effects);
    }

    [Fact]
    public void HashFramingDistinguishesAmbiguousTextAndUnicode()
    {
        var p = Preview();
        Assert.NotEqual(ActionPreviewHasher.Hash(p with { Targets = ["a:b", "c"] }),
            ActionPreviewHasher.Hash(p with { Targets = ["a", "b:c"] }));
        Assert.NotEqual(ActionPreviewHasher.Hash(p with { Title = "\ud800" }),
            ActionPreviewHasher.Hash(p with { Title = "\ud801" }));
    }

    [Fact]
    public void MutableBackingCollectionsCannotChangeAlreadyIssuedConsent()
    {
        var targets = new List<string> { "cible A" };
        var preview = Preview() with { Targets = targets };
        var service = new ConfirmationService(new FakeTimeProvider());
        var plan = service.Issue(preview, Consent(preview));
        targets[0] = "cible B";
        Assert.Equal("cible A", preview.Targets[0]);
        Assert.Equal(plan.PreviewHash, ActionPreviewHasher.Hash(preview));
        Assert.True(service.TryConsume(plan, preview).IsAccepted);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void SourceMutationNeverChangesSnapshotBeforeOrAfterConsumption(bool useWith, int stage)
    {
        var targets = new List<string> { "cible A" };
        var parameters = new Dictionary<string, string> { ["option"] = "valeur confirmée" };
        var effects = new List<string> { "effet confirmé" };
        var backups = new List<string> { "sauvegarde confirmée" };
        var preview = useWith
            ? Preview() with { Targets = targets, Parameters = parameters, Effects = effects, RequiredBackups = backups }
            : new ActionPreview("test.action", "Action de test", RiskLevel.Low, targets, parameters, effects,
                false, false, Reversibility.Full, backups);
        var hash = ActionPreviewHasher.Hash(preview);
        var service = new ConfirmationService(new FakeTimeProvider());
        ConfirmedPlan? plan = stage >= 1 ? service.Issue(preview, Consent(preview)) : null;
        if (stage == 2) Assert.True(service.TryConsume(plan, preview).IsAccepted);

        targets[0] = "cible non confirmée";
        targets.Add("cible ajoutée");
        parameters["option"] = "valeur non confirmée";
        parameters.Add("nouvelle option", "ajout");
        effects.Clear();
        backups[0] = "autre sauvegarde";

        Assert.Equal("cible A", Assert.Single(preview.Targets));
        Assert.Equal("valeur confirmée", Assert.Single(preview.Parameters).Value);
        Assert.Equal("effet confirmé", Assert.Single(preview.Effects));
        Assert.Equal("sauvegarde confirmée", Assert.Single(preview.RequiredBackups));
        Assert.Equal(hash, ActionPreviewHasher.Hash(preview));
        if (plan is not null) Assert.Equal(plan.PreviewHash, hash);
        if (stage < 2)
        {
            plan ??= service.Issue(preview, Consent(preview));
            Assert.True(service.TryConsume(plan, preview).IsAccepted);
        }
        // The future effect reads the same frozen scope even after consumption.
        Assert.Equal("cible A", preview.Targets[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SnapshotCollectionsRejectWritesThroughMutableInterfaces(int field)
    {
        var preview = Preview();
        var hash = ActionPreviewHasher.Hash(preview);
        if (field == 1)
        {
            var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(preview.Parameters);
            Assert.True(dictionary.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => dictionary["a"] = "autre");
            Assert.Throws<NotSupportedException>(() => dictionary.Add("nouveau", "autre"));
            var untyped = Assert.IsAssignableFrom<System.Collections.IDictionary>(preview.Parameters);
            Assert.Throws<NotSupportedException>(() => untyped["a"] = "autre");
        }
        else
        {
            var values = field switch { 0 => preview.Targets, 2 => preview.Effects, _ => preview.RequiredBackups };
            var list = Assert.IsAssignableFrom<IList<string>>(values);
            Assert.True(list.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => list[0] = "autre");
            Assert.Throws<NotSupportedException>(() => list.Add("autre"));
            var untyped = Assert.IsAssignableFrom<System.Collections.IList>(values);
            Assert.Throws<NotSupportedException>(() => untyped[0] = "autre");
        }
        Assert.Equal(hash, ActionPreviewHasher.Hash(preview));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void NullCollectionsAreRejectedOnConstructionAndWith(bool useWith, int field)
    {
        var preview = Preview();
        Assert.Throws<ArgumentNullException>(() => useWith
            ? field switch
            {
                0 => preview with { Targets = null! }, 1 => preview with { Parameters = null! },
                2 => preview with { Effects = null! }, _ => preview with { RequiredBackups = null! }
            }
            : new ActionPreview(preview.ActionId, preview.Title, preview.RiskLevel,
                field == 0 ? null! : preview.Targets, field == 1 ? null! : preview.Parameters,
                field == 2 ? null! : preview.Effects, false, false, Reversibility.Full,
                field == 3 ? null! : preview.RequiredBackups));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullCurrentPreviewIsRejectedBeforeValidation(bool consume)
    {
        var service = new ConfirmationService(new FakeTimeProvider());
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        foreach (var candidate in new ConfirmedPlan?[] { null, plan })
        {
            var exception = Assert.Throws<ArgumentNullException>(() => consume
                ? service.TryConsume(candidate, null!) : service.Validate(candidate, null!));
            Assert.Equal("currentPreview", exception.ParamName);
        }
        Assert.True(service.Validate(plan, preview).IsAccepted);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    public void MonotonicDeadlineExpiresDespiteWallClockRollback(int minutes)
    {
        var clock = new FakeTimeProvider();
        var service = new ConfirmationService(clock, TimeSpan.FromMinutes(minutes));
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        clock.AdvanceUtc(TimeSpan.FromHours(-1));
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(minutes) - TimeSpan.FromTicks(1));
        Assert.True(service.Validate(plan, preview).IsAccepted);
        clock.AdvanceMonotonic(TimeSpan.FromTicks(1));
        Rejected(service.Validate(plan, preview), PlanRejectionReason.Expired);
        var action = new FakeAction(service);
        Rejected(action.Execute(plan, preview), PlanRejectionReason.Expired);
        Assert.Equal(0, action.Effects);
        Assert.Equal(TimeSpan.FromMinutes(minutes), plan.ExpiresAt - plan.IssuedAt);
    }

    [Fact]
    public void WallClockDeadlineExpiresWithoutMonotonicAdvance()
    {
        var clock = new FakeTimeProvider();
        var service = new ConfirmationService(clock);
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        clock.AdvanceUtc(TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1));
        Assert.True(service.Validate(plan, preview).IsAccepted);
        clock.AdvanceUtc(TimeSpan.FromTicks(1));
        Rejected(service.Validate(plan, preview), PlanRejectionReason.Expired);
        var action = new FakeAction(service);
        Rejected(action.Execute(plan, preview), PlanRejectionReason.Expired);
        Assert.Equal(0, action.Effects);
    }

    [Fact]
    public void MonotonicExpirationPreservesRejectionPriority()
    {
        var clock = new FakeTimeProvider();
        var service = new ConfirmationService(clock);
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        Assert.True(service.TryConsume(plan, preview).IsAccepted);
        clock.AdvanceUtc(TimeSpan.FromHours(-1));
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(10));
        Rejected(service.Validate(null, preview), PlanRejectionReason.Missing);
        Rejected(service.Validate(Clone(plan), preview with { ActionId = "autre" }), PlanRejectionReason.UnknownProvenance);
        Rejected(service.Validate(plan, preview with { ActionId = "autre", Targets = [] }), PlanRejectionReason.ActionMismatch);
        Rejected(service.Validate(plan, preview with { Targets = [] }), PlanRejectionReason.PreviewHashMismatch);
        Rejected(service.TryConsume(plan, preview), PlanRejectionReason.Expired);
    }

    [Fact]
    public void PlanIsSealedReadOnlyAndAbsentFromContracts()
    {
        var type = typeof(ConfirmedPlan);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        Assert.All(type.GetProperties(), property => Assert.Null(property.GetSetMethod()));
        Assert.DoesNotContain(typeof(ActionPreview).Assembly.GetTypes(), t => t.Name == nameof(ConfirmedPlan));
        Assert.All(type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic), constructor =>
            Assert.DoesNotContain(constructor.GetCustomAttributesData(), a => a.AttributeType.Name == "JsonConstructorAttribute"));
    }

    [Fact]
    public void JsonCannotProduceAnAcceptedPlan()
    {
        var preview = Preview();
        var service = new ConfirmationService(new FakeTimeProvider());
        var plan = service.Issue(preview, Consent(preview));
        var json = JsonSerializer.Serialize(plan);
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<ConfirmedPlan>(json));
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ForgedPlanConverter(plan));
        var forged = JsonSerializer.Deserialize<ConfirmedPlan>(json, options);
        var action = new FakeAction(service);
        Rejected(action.Execute(forged, preview), PlanRejectionReason.UnknownProvenance);
        Assert.Equal(0, action.Effects);
    }

    [Fact]
    public void CoreActionsHaveNoFileOrProcessDependencies()
    {
        var types = typeof(ConfirmedPlan).Assembly.GetTypes()
            .Where(t => t.Namespace == "Virgil.Core.Actions");
        foreach (var type in types)
        {
            var referenced = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(f => f.FieldType)
                .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)));
            Assert.DoesNotContain(referenced, t => t.Namespace?.StartsWith("System.IO", StringComparison.Ordinal) == true ||
                t.Namespace?.StartsWith("System.Diagnostics", StringComparison.Ordinal) == true);
        }
        var root = RepositoryRoot();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src/Virgil.Core/Actions"), "*.cs"))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("System.IO", source);
            Assert.DoesNotContain("System.Diagnostics", source);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Virgil.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Racine de test introuvable.");
    }

    private static void Rejected(PlanValidationResult result, PlanRejectionReason reason)
    {
        Assert.False(result.IsAccepted);
        Assert.Equal(reason, result.Reason);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    private static ConfirmedPlan Clone(ConfirmedPlan plan) => (ConfirmedPlan)Activator.CreateInstance(
        typeof(ConfirmedPlan), BindingFlags.NonPublic | BindingFlags.Instance, null,
        [plan.PlanId, plan.ActionId, plan.PreviewHash, plan.ConfirmationLevel, plan.IssuedAt, plan.ExpiresAt],
        CultureInfo.InvariantCulture)!;

    private sealed class ForgedPlanConverter(ConfirmedPlan template) : System.Text.Json.Serialization.JsonConverter<ConfirmedPlan>
    {
        public override ConfirmedPlan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return Clone(template);
        }
        public override void Write(Utf8JsonWriter writer, ConfirmedPlan value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PurgePreservesExactRetentionBoundary(bool consume)
    {
        var clock = new FakeTimeProvider();
        var service = new ConfirmationService(clock);
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        Assert.True(service.TryConsume(plan, preview).IsAccepted);
        Rejected(service.Validate(plan, preview), PlanRejectionReason.AlreadyUsed);
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(25));
        Rejected(consume ? service.TryConsume(plan, preview) : service.Validate(plan, preview), PlanRejectionReason.Expired);
        clock.AdvanceMonotonic(TimeSpan.FromTicks(1));
        Rejected(consume ? service.TryConsume(plan, preview) : service.Validate(plan, preview), PlanRejectionReason.UnknownProvenance);
        var action = new FakeAction(service);
        Rejected(action.Execute(plan, preview), PlanRejectionReason.UnknownProvenance);
        Assert.Equal(0, action.Effects);
        // A purged identity can never regain consent, including after wall-clock rollback.
        clock.AdvanceUtc(TimeSpan.FromDays(-1));
        Rejected(service.TryConsume(plan, preview), PlanRejectionReason.UnknownProvenance);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    public void IssuePurgesUsingConfiguredLifetimeAndMonotonicTime(int minutes)
    {
        var clock = new FakeTimeProvider();
        var service = new ConfirmationService(clock, TimeSpan.FromMinutes(minutes));
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        clock.AdvanceUtc(TimeSpan.FromDays(-1));
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(minutes) + ConfirmationService.MaximumLifetime + TimeSpan.FromTicks(1));
        service.Issue(preview, Consent(preview));
        var registry = (System.Collections.IDictionary)typeof(ConfirmationService)
            .GetField("_issued", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        Assert.Equal(1, registry.Count);
        var action = new FakeAction(service);
        Rejected(action.Execute(plan, preview), PlanRejectionReason.UnknownProvenance);
        Assert.Equal(0, action.Effects);
    }

    [Fact]
    public void CapacityEvictsOldestIssuanceEvenWhenTimestampsAreEqual()
    {
        var service = new ConfirmationService(new FakeTimeProvider());
        var preview = Preview();
        var plans = Enumerable.Range(0, 1024).Select(_ => service.Issue(preview, Consent(preview))).ToArray();
        Assert.True(service.TryConsume(plans[0], preview).IsAccepted);
        service.Issue(preview, Consent(preview));
        Rejected(service.TryConsume(plans[0], preview), PlanRejectionReason.UnknownProvenance);
        Assert.All(plans.Skip(1), plan => Assert.True(service.Validate(plan, preview).IsAccepted));
        service.Issue(preview, Consent(preview));
        Rejected(service.Validate(plans[1], preview), PlanRejectionReason.UnknownProvenance);
        Assert.True(service.TryConsume(plans[2], preview).IsAccepted);
    }

    [Fact]
    public void PurgedProvenanceStillPrecedesActionAndHashButFollowsMissing()
    {
        var clock = new FakeTimeProvider();
        var service = new ConfirmationService(clock);
        var preview = Preview();
        var plan = service.Issue(preview, Consent(preview));
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(25) + TimeSpan.FromTicks(1));
        Rejected(service.Validate(null, preview), PlanRejectionReason.Missing);
        Rejected(service.TryConsume(plan, preview with { ActionId = "autre", Targets = [] }), PlanRejectionReason.UnknownProvenance);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp = TimeSpan.TicksPerHour;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void AdvanceUtc(TimeSpan duration) => _now += duration;
        public void AdvanceMonotonic(TimeSpan duration) => _timestamp += duration.Ticks;
        public void Advance(TimeSpan duration)
        {
            AdvanceUtc(duration);
            AdvanceMonotonic(duration);
        }
    }

    private sealed class FakeAction(IConfirmedPlanGate gate)
    {
        private int _effects;
        public int Effects => _effects;
        public PlanValidationResult Execute(ConfirmedPlan? plan, ActionPreview preview)
        {
            var result = gate.TryConsume(plan, preview);
            if (result.IsAccepted) Interlocked.Increment(ref _effects);
            return result;
        }
        public PlanValidationResult ConfirmAndExecute(IConfirmationService service, ActionPreview preview, ConfirmationConsent consent) =>
            Execute(service.Issue(preview, consent), preview);
    }
}
