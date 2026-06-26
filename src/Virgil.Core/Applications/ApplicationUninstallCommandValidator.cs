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

        if (lower.Contains("program files") &&
            !LooksLikeOfficialUninstaller(lower) &&
            !lower.Contains("msiexec"))
        {
            return Blocked("Chemin Program Files sans desinstalleur officiel identifiable.");
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
                ? AllowedOrCaution(risk, "Commande rundll32 reconnue comme desinstalleur Windows.")
                : Blocked("Commande rundll32 non reconnue.");
        }

        var tokens = Tokenize(normalized);
        if (tokens.Count == 0)
        {
            return Blocked("Commande illisible.");
        }

        var executable = tokens[0].Trim('"');
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
            Arguments = tokens.Skip(1).ToList()
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

    private static ApplicationCommandValidationResult AllowedOrCaution(ApplicationRiskLevel risk, string reason)
    {
        return new ApplicationCommandValidationResult
        {
            Status = risk == ApplicationRiskLevel.Caution
                ? ApplicationCommandValidationStatus.NeedsCaution
                : ApplicationCommandValidationStatus.Allowed,
            Reason = reason
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

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex ProductCodeRegex();

    [GeneratedRegex("\"[^\"]+\"|\\S+")]
    private static partial Regex CommandTokenRegex();
}

