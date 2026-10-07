using System.Text.RegularExpressions;
using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public sealed partial class ApplicationUninstallCommandValidator
{
    public ApplicationCommandValidationResult Validate(InstalledApplication application)
    {
        if (application.RiskLevel == ApplicationRiskLevel.Protected)
        {
            return Blocked("Application protegee : aucune desinstallation depuis Virgil V1.");
        }

        if (application.UninstallKind == ApplicationUninstallKind.Winget)
        {
            return ValidateWinget(application.WingetId, exactMatch: true);
        }

        if (application.UninstallKind == ApplicationUninstallKind.StoreSettings)
        {
            return Blocked("Application Store : ouverture des parametres Windows uniquement en V1.");
        }

        var command = application.UninstallCommand;
        if (string.IsNullOrWhiteSpace(command))
        {
            return Blocked("Commande de desinstallation absente.");
        }

        return ValidateCommand(command, application.UninstallKind, application.RiskLevel);
    }

    public ApplicationCommandValidationResult ValidateCommand(
        string command,
        ApplicationUninstallKind kind,
        ApplicationRiskLevel risk)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Blocked("Commande vide.");
        }

        var normalized = command.Trim();
        var lower = normalized.ToLowerInvariant();

        if (ContainsDangerousShell(lower))
        {
            return Blocked("Commande dangereuse ou chainee bloquee.");
        }

        if (TouchesProtectedPersonalArea(lower))
        {
            return Blocked("Commande touchant un profil utilisateur bloquee.");
        }

        if (LooksLikeMsiexec(normalized))
        {
            var args = Tokenize(normalized).Skip(1).ToList();
            return new ApplicationCommandValidationResult
            {
                Status = risk == ApplicationRiskLevel.Caution ? ApplicationCommandValidationStatus.NeedsCaution : ApplicationCommandValidationStatus.Allowed,
                Reason = "Commande MSI officielle validee.",
                Executable = "msiexec.exe",
                Arguments = args
            };
        }

        if (lower.StartsWith("rundll32", StringComparison.OrdinalIgnoreCase))
        {
            return lower.Contains("setupapi.dll", StringComparison.OrdinalIgnoreCase)
                ? WithExecutable(
                    risk,
                    "Commande rundll32 reconnue comme desinstalleur Windows.",
                    "rundll32.exe",
                    Tokenize(normalized).Skip(1).ToList())
                : Blocked("Commande rundll32 non reconnue.");
        }

        if (!TrySplitExecutableAndArguments(normalized, out var executable, out var arguments))
        {
            return Blocked("Commande illisible.");
        }

        if (!Path.IsPathRooted(executable) && !File.Exists(executable))
        {
            return Blocked("Executable relatif ou introuvable.");
        }

        var fileName = Path.GetFileName(executable);
        if (!LooksLikeOfficialUninstaller(fileName))
        {
            return Blocked("Executable non reconnu comme desinstalleur officiel.");
        }

        return new ApplicationCommandValidationResult
        {
            Status = risk == ApplicationRiskLevel.Caution ? ApplicationCommandValidationStatus.NeedsCaution : ApplicationCommandValidationStatus.Allowed,
            Reason = "Desinstalleur officiel local valide.",
            Executable = executable,
            Arguments = arguments
        };
    }

    public ApplicationCommandValidationResult ValidateWinget(string? packageId, bool exactMatch)
    {
        if (string.IsNullOrWhiteSpace(packageId) || !exactMatch || !LooksLikeWingetId(packageId))
        {
            return Blocked("WinGet bloque : identifiant exact requis.");
        }

        return new ApplicationCommandValidationResult
        {
            Status = ApplicationCommandValidationStatus.Allowed,
            Reason = "WinGet autorise avec ID exact.",
            Executable = "winget",
            Arguments = ["uninstall", "--id", packageId, "--exact"]
        };
    }

    private static ApplicationCommandValidationResult WithExecutable(
        ApplicationRiskLevel risk,
        string reason,
        string executable,
        IReadOnlyList<string> arguments)
    {
        return new ApplicationCommandValidationResult
        {
            Status = risk == ApplicationRiskLevel.Caution
                ? ApplicationCommandValidationStatus.NeedsCaution
                : ApplicationCommandValidationStatus.Allowed,
            Reason = reason,
            Executable = executable,
            Arguments = arguments
        };
    }

    private static ApplicationCommandValidationResult Blocked(string reason)
    {
        return new ApplicationCommandValidationResult
        {
            Status = ApplicationCommandValidationStatus.Blocked,
            Reason = reason
        };
    }

    private static bool ContainsDangerousShell(string lower)
    {
        string[] dangerous =
        [
            "cmd /c del", "cmd.exe /c del", "cmd /c rmdir", "cmd.exe /c rmdir",
            "remove-item", "takeown", "icacls", " format ", " erase ", " del ",
            " rmdir ", " rd /s", "&&", "||", " | ", ";"
        ];
        return dangerous.Any(lower.Contains);
    }

    private static bool TouchesProtectedPersonalArea(string lower)
    {
        return lower.Contains(@"c:\users\", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("%userprofile%", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("%appdata%", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeOfficialUninstaller(string value)
    {
        var file = Path.GetFileName(value).ToLowerInvariant();
        return file.Contains("uninstall") ||
            file.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
            file.Contains("setup") ||
            file.Contains("installer") ||
            file.Contains("modify");
    }

    private static bool LooksLikeMsiexec(string command)
    {
        var lower = command.ToLowerInvariant();
        return lower.Contains("msiexec") &&
            (lower.Contains(" /x") || lower.Contains(" -x") || lower.Contains("/uninstall")) &&
            ProductCodeRegex().IsMatch(command);
    }

    private static bool LooksLikeWingetId(string value)
    {
        return value.Contains('.') &&
            value.Any(char.IsLetter) &&
            value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or '+');
    }

    private static List<string> Tokenize(string command)
    {
        var matches = CommandTokenRegex().Matches(command);
        return matches
            .Select(match => match.Value.Trim().Trim('"'))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static bool TrySplitExecutableAndArguments(
        string command,
        out string executable,
        out IReadOnlyList<string> arguments)
    {
        var tokens = Tokenize(command);
        if (tokens.Count == 0)
        {
            executable = string.Empty;
            arguments = Array.Empty<string>();
            return false;
        }

        if (command.StartsWith('"'))
        {
            executable = tokens[0].Trim('"');
            arguments = tokens.Skip(1).ToList();
            return true;
        }

        var executableMatch = UnquotedExecutableRegex().Match(command);
        if (executableMatch.Success)
        {
            executable = executableMatch.Groups["executable"].Value.Trim();
            var rawArguments = executableMatch.Groups["arguments"].Value.Trim();
            arguments = string.IsNullOrWhiteSpace(rawArguments)
                ? Array.Empty<string>()
                : Tokenize(rawArguments);
            return true;
        }

        executable = tokens[0].Trim('"');
        arguments = tokens.Skip(1).ToList();
        return true;
    }

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex ProductCodeRegex();

    [GeneratedRegex(@"^(?<executable>[A-Za-z]:\\.+?\.exe)(?<arguments>\s+.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex UnquotedExecutableRegex();

    [GeneratedRegex("\"[^\"]+\"|\\S+")]
    private static partial Regex CommandTokenRegex();
}
