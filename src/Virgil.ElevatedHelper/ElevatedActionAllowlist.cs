using Virgil.Contracts;

namespace Virgil.ElevatedHelper;

public sealed class ElevatedActionAllowlist
{
    public bool IsMaintenanceAllowed(InterventionId id, string identity) =>
        UpdateMaintenanceRunner.Handles(id) && UpdateMaintenanceRunner.ValidIdentity(id, identity);

    public bool TryGet(InterventionId actionId, string systemDrive, out ElevatedActionSpec spec)
    {
        spec = actionId switch
        {
            InterventionId.VerifyHealth when IsValidSystemDrive(systemDrive) => Spec(actionId, false,
                Command("sfc.exe", "/verifyonly"),
                Command("dism.exe", "/Online", "/Cleanup-Image", "/CheckHealth", "/English"),
                Command("chkdsk.exe", systemDrive, "/scan"), Command("virgil-services")),
            InterventionId.VerifyHealthDeep when IsValidSystemDrive(systemDrive) => Spec(actionId, false,
                Command("sfc.exe", "/verifyonly"),
                Command("dism.exe", "/Online", "/Cleanup-Image", "/CheckHealth", "/English"),
                Command("dism.exe", "/Online", "/Cleanup-Image", "/ScanHealth", "/English"),
                Command("chkdsk.exe", systemDrive, "/scan"), Command("virgil-services")),
            InterventionId.SfcVerifyOnly => Spec(actionId, false, Command("sfc.exe", "/verifyonly")),
            InterventionId.DismCheckHealth => Spec(actionId, false, Command("dism.exe", "/Online", "/Cleanup-Image", "/CheckHealth", "/English")),
            InterventionId.CheckServices => Spec(actionId, false, Command("virgil-services")),
            InterventionId.CreateRestorePoint => Spec(actionId, false, Command("virgil-restore-point")),
            InterventionId.RestartWindowsUpdate => Spec(actionId, false, Command("virgil-update")),
            InterventionId.RestartAudio => Spec(actionId, false, Command("virgil-audio")),
            InterventionId.ResetPrintQueue => Spec(actionId, false, Command("virgil-print")),
            InterventionId.FlushDns => Spec(actionId, reboot: false, Command("ipconfig.exe", "/flushdns")),
            InterventionId.RenewIp => Spec(actionId, reboot: false,
                Command("ipconfig.exe", "/release"),
                Command("ipconfig.exe", "/renew")),
            InterventionId.ResetWinsock => Spec(actionId, reboot: true, Command("netsh.exe", "winsock", "reset")),
            InterventionId.ResetTcpIp => Spec(actionId, reboot: true, Command("netsh.exe", "int", "ip", "reset")),
            InterventionId.SfcScan => Spec(actionId, reboot: true, Command("sfc.exe", "/scannow")),
            InterventionId.DismScanHealth => Spec(actionId, reboot: false,
                Command("dism.exe", "/Online", "/Cleanup-Image", "/ScanHealth")),
            InterventionId.DismRestoreHealth => Spec(actionId, reboot: true,
                Command("dism.exe", "/Online", "/Cleanup-Image", "/RestoreHealth")),
            InterventionId.ChkdskOnlineScan when IsValidSystemDrive(systemDrive) => Spec(actionId, reboot: false,
                Command("chkdsk.exe", systemDrive, "/scan")),
            _ => new ElevatedActionSpec(actionId, false, Array.Empty<ElevatedCommandSpec>(), false)
        };

        return spec.Commands.Count > 0 && IsSafe(spec);
    }

    public static bool IsValidSystemDrive(string value)
    {
        return value.Length == 2 &&
            value[1] == ':' &&
            value[0] is >= 'A' and <= 'Z';
    }

    private static ElevatedActionSpec Spec(
        InterventionId actionId,
        bool reboot,
        params ElevatedCommandSpec[] commands)
    {
        return new ElevatedActionSpec(actionId, true, commands, reboot);
    }

    private static ElevatedCommandSpec Command(string fileName, params string[] arguments)
    {
        return new ElevatedCommandSpec(fileName, arguments);
    }

    private static bool IsSafe(ElevatedActionSpec spec)
    {
        foreach (var command in spec.Commands)
        {
            if (command.FileName.Contains('\\') ||
                command.FileName.Contains('/') ||
                command.Arguments.Any(IsForbiddenArgument))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsForbiddenArgument(string argument)
    {
        return argument.Contains('&') ||
            argument.Contains('|') ||
            argument.Contains(';') ||
            argument.Equals("/f", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("/r", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("/x", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("/ResetBase", StringComparison.OrdinalIgnoreCase) ||
            argument.Contains("takeown", StringComparison.OrdinalIgnoreCase) ||
            argument.Contains("icacls", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ElevatedActionSpec(
    InterventionId ActionId,
    bool RequiresAdministrator,
    IReadOnlyList<ElevatedCommandSpec> Commands,
    bool RebootRequiredOnSuccess);

public sealed record ElevatedCommandSpec(
    string FileName,
    IReadOnlyList<string> Arguments);
