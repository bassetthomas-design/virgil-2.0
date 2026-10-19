using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Contracts.Applications;
using Virgil.Core.Actions;
using Xunit;

namespace Virgil.Tests;

public sealed class RiskLevelMappingTests
{
    public static IEnumerable<object[]> Applications() => Enum.GetValues<ApplicationRiskLevel>().Select(value => new object[] { value, value switch
    {
        ApplicationRiskLevel.SafeToUninstall => RiskLevel.Low,
        ApplicationRiskLevel.Caution => RiskLevel.Sensitive,
        ApplicationRiskLevel.Protected or ApplicationRiskLevel.Unknown => RiskLevel.Critical,
        _ => throw new InvalidOperationException("Nouvelle valeur à examiner.")
    } });
    public static IEnumerable<object[]> Updates() => Enum.GetValues<UpdateRiskLevel>().Select(value => new object[] { value, value switch
    {
        UpdateRiskLevel.Safe or UpdateRiskLevel.ValidationRequired => RiskLevel.Low,
        UpdateRiskLevel.Sensitive => RiskLevel.Sensitive,
        UpdateRiskLevel.CriticalInformationOnly => RiskLevel.Critical,
        _ => throw new InvalidOperationException("Nouvelle valeur à examiner.")
    } });
    public static IEnumerable<object[]> Interventions() => Enum.GetValues<InterventionRiskLevel>().Select(value => new object[] { value, value switch
    {
        InterventionRiskLevel.Low or InterventionRiskLevel.Moderate => RiskLevel.Low,
        InterventionRiskLevel.Sensitive => RiskLevel.Sensitive,
        _ => throw new InvalidOperationException("Nouvelle valeur à examiner.")
    } });
    public static IEnumerable<object[]> Cleanup() => Enum.GetValues<CleanupRiskLevel>().Select(value => new object[] { value, value switch
    {
        CleanupRiskLevel.Low or CleanupRiskLevel.Medium => RiskLevel.Low,
        CleanupRiskLevel.High => RiskLevel.Sensitive,
        _ => throw new InvalidOperationException("Nouvelle valeur à examiner.")
    } });

    [Theory, MemberData(nameof(Applications))]
    public void ApplicationMapping(ApplicationRiskLevel value, RiskLevel expected) => Assert.Equal(expected, value.ToRiskLevel());
    [Theory, MemberData(nameof(Updates))]
    public void UpdateMapping(UpdateRiskLevel value, RiskLevel expected) => Assert.Equal(expected, value.ToRiskLevel());
    [Theory, MemberData(nameof(Interventions))]
    public void InterventionMapping(InterventionRiskLevel value, RiskLevel expected) => Assert.Equal(expected, value.ToRiskLevel());
    [Theory, MemberData(nameof(Cleanup))]
    public void CleanupMapping(CleanupRiskLevel value, RiskLevel expected) => Assert.Equal(expected, value.ToRiskLevel());

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void UndefinedValuesAreRejected(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((ApplicationRiskLevel)value).ToRiskLevel());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((UpdateRiskLevel)value).ToRiskLevel());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((InterventionRiskLevel)value).ToRiskLevel());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((CleanupRiskLevel)value).ToRiskLevel());
    }
}
