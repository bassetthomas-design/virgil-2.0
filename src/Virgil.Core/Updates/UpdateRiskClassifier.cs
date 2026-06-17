using Virgil.Domain;

namespace Virgil.Core.Updates;

public sealed class UpdateRiskClassifier
{
    private static readonly string[] SafeTerms =
    {
        "7zip",
        "7-zip",
        "videolan",
        "vlc",
        "notepad++",
        "notepadplusplus",
        "sumatrapdf"
    };

    private static readonly string[] SensitiveTerms =
    {
        "antivirus",
        "defender",
        "security",
        "vpn",
        "firewall",
        "network",
        "ethernet",
        "wireless",
        "driver",
        "nvidia",
        "amd.",
        "intel.",
        "realtek",
        "synaptics",
        "storage",
        "chipset",
        "vmware",
        "virtualbox",
        "docker"
    };

    private static readonly string[] FirmwareTerms =
    {
        "bios",
        "firmware",
        "uefi"
    };

    public UpdateRiskAssessment Classify(UpdateItem item)
    {
        var fingerprint = string.Join(
            " ",
            item.Id,
            item.Name,
            item.Publisher,
            item.Source.ToString()).ToLowerInvariant();

        if (item.Source == UpdateSource.FirmwareInformation || ContainsAny(fingerprint, FirmwareTerms))
        {
            return new UpdateRiskAssessment(
                UpdateRiskLevel.CriticalInformationOnly,
                "Firmware ou BIOS : information uniquement dans cette preview.");
        }

        if (item.Source == UpdateSource.Driver || ContainsAny(fingerprint, SensitiveTerms))
        {
            return new UpdateRiskAssessment(
                UpdateRiskLevel.Sensitive,
                "Composant sensible : validation explicite requise.");
        }

        if (ContainsAny(fingerprint, SafeTerms))
        {
            return new UpdateRiskAssessment(
                UpdateRiskLevel.Safe,
                "Application utilisateur courante a faible risque.");
        }

        return new UpdateRiskAssessment(
            UpdateRiskLevel.ValidationRequired,
            "Application a verifier avant installation.");
    }

    private static bool ContainsAny(string value, IEnumerable<string> terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record UpdateRiskAssessment(
    UpdateRiskLevel Level,
    string Reason);
