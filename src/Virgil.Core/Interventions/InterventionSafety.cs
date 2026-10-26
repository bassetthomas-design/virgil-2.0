using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public static class InterventionSafety
{
    public static bool RequiresRestorePoint(InterventionId id) => id is
        InterventionId.FlushDns or InterventionId.RenewIp or InterventionId.ResetWinsock or
        InterventionId.ResetTcpIp or InterventionId.SfcScan or InterventionId.DismRestoreHealth or
        InterventionId.RestartWindowsUpdate or InterventionId.RestartAudio or InterventionId.ResetPrintQueue;

    // Only machine-readable findings or explicit integrity-tool findings propose a repair.
    public static IReadOnlySet<InterventionId> RepairsFor(string output)
    {
        var actions = new HashSet<InterventionId>();
        if (output.Contains("found integrity violations", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("a trouvé des violations d’intégrité", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("a trouvé des violations d'intégrité", StringComparison.OrdinalIgnoreCase)) actions.Add(InterventionId.SfcScan);
        if (output.Contains("component store is repairable", StringComparison.OrdinalIgnoreCase)) actions.Add(InterventionId.DismRestoreHealth);
        foreach (var line in output.Split('\n'))
        {
            if (!line.StartsWith("VIRGIL|", StringComparison.Ordinal) || !line.Contains("|FINDING|", StringComparison.Ordinal)) continue;
            var key = line.Split('|')[1];
            if (key == "SFC") actions.Add(InterventionId.SfcScan);
            if (key == "DISM") actions.Add(InterventionId.DismRestoreHealth);
            if (key is "Audiosrv" or "AudioEndpointBuilder") actions.Add(InterventionId.RestartAudio);
            if (key == "PrintQueue") actions.Add(InterventionId.ResetPrintQueue);
            if (key == "Spooler") actions.Add(InterventionId.ResetPrintQueue);
            if (key == "DNS") actions.Add(InterventionId.FlushDns);
        }
        return actions;
    }
}
