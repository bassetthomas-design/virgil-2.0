using System.Text.RegularExpressions;
using Virgil.Domain;

namespace Virgil.Core.Reports;

public sealed partial class ReportSanitizer : IReportSanitizer
{
    private const int MaximumStoredTextLength = 8_000;
    private readonly string _userProfile;

    public ReportSanitizer()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public ReportSanitizer(string userProfile)
    {
        _userProfile = NormalizePath(userProfile);
    }

    public ReportEntry Sanitize(ReportEntry report)
    {
        return report with
        {
            Title = SanitizeText(report.Title),
            Summary = SanitizeText(report.Summary),
            Status = SanitizeText(report.Status),
            Module = SanitizeText(report.Module),
            ProposedActions = report.ProposedActions.Select(SanitizeAction).ToList(),
            ExecutedActions = report.ExecutedActions.Select(SanitizeAction).ToList(),
            SkippedActions = report.SkippedActions.Select(SanitizeAction).ToList(),
            Errors = report.Errors.Select(SanitizeText).Where(value => value.Length > 0).ToList(),
            SimpleView = SanitizeText(report.SimpleView),
            TechnicalDetails = SanitizeText(report.TechnicalDetails),
            VirgilVersion = SanitizeText(report.VirgilVersion),
            Source = SanitizeText(report.Source)
        };
    }

    public string SanitizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var clean = value.Replace("\0", string.Empty, StringComparison.Ordinal);
        clean = BearerSecretRegex().Replace(clean, "$1[MASQUE]");
        clean = NamedSecretRegex().Replace(clean, "$1=[MASQUE]");
        clean = ArgumentSecretRegex().Replace(clean, "$1$2=[MASQUE]");
        clean = SanitizeProfilePaths(clean);
        return clean.Length <= MaximumStoredTextLength
            ? clean
            : clean[..MaximumStoredTextLength] + "\n[DETAILS TRONQUES]";
    }

    private ReportAction SanitizeAction(ReportAction action)
    {
        return action with
        {
            Name = SanitizeText(action.Name),
            Risk = SanitizeText(action.Risk),
            Result = SanitizeText(action.Result),
            ReadableError = string.IsNullOrWhiteSpace(action.ReadableError)
                ? null
                : SanitizeText(action.ReadableError),
            TechnicalDetails = SanitizeText(action.TechnicalDetails)
        };
    }

    private string SanitizeProfilePaths(string value)
    {
        if (string.IsNullOrWhiteSpace(_userProfile))
        {
            return value;
        }

        var profilePattern = Regex.Escape(_userProfile)
            .Replace("\\\\", "[\\\\/]", StringComparison.Ordinal);
        var clean = Regex.Replace(
            value,
            profilePattern + "[\\\\/]Downloads(?=[\\\\/]|$)",
            "Telechargements",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        clean = Regex.Replace(
            clean,
            profilePattern + "[\\\\/]Documents(?=[\\\\/]|$)",
            "Documents",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        clean = Regex.Replace(
            clean,
            profilePattern + "[\\\\/]Desktop(?=[\\\\/]|$)",
            "Bureau",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(
            clean,
            profilePattern,
            "[PROFIL]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }

    [GeneratedRegex("(?i)(bearer\\s+)[A-Za-z0-9._~+\\-/=]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex BearerSecretRegex();

    [GeneratedRegex("(?i)\\b(password|passwd|pwd|token|secret|api[-_]?key|license[-_]?key|key)\\b\\s*[:=]\\s*(?:\\\"[^\\\"]*\\\"|'[^']*'|[^\\s;,]+)", RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecretRegex();

    [GeneratedRegex("(?i)(--|/)(password|passwd|pwd|token|secret|api[-_]?key|license[-_]?key|key)(?:\\s+|[:=])(?:\\\"[^\\\"]*\\\"|'[^']*'|[^\\s;,]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentSecretRegex();
}
