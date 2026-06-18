using Virgil.Domain;

namespace Virgil.Core.Resources;

public sealed class ResourceRecommendationService
{
    public ResourceHealthLevel ClassifyMemory(double usedPercent)
    {
        return usedPercent switch
        {
            < 0 => ResourceHealthLevel.Unknown,
            < 70 => ResourceHealthLevel.Stable,
            < 85 => ResourceHealthLevel.Watch,
            < 95 => ResourceHealthLevel.InterventionRecommended,
            _ => ResourceHealthLevel.Critical
        };
    }

    public ResourceHealthLevel ClassifyCpu(IReadOnlyList<double> samples)
    {
        if (samples.Count == 0)
        {
            return ResourceHealthLevel.Unknown;
        }

        var average = samples.Average();
        var sustainedCriticalSamples = samples.Count(value => value >= 95);
        if (average >= 95 && sustainedCriticalSamples >= 2)
        {
            return ResourceHealthLevel.Critical;
        }

        if (average >= 85)
        {
            return ResourceHealthLevel.InterventionRecommended;
        }

        if (average >= 70 || samples.Max() >= 95)
        {
            return ResourceHealthLevel.Watch;
        }

        return ResourceHealthLevel.Stable;
    }

    public ResourceHealthLevel ClassifyUptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero)
        {
            return ResourceHealthLevel.Unknown;
        }

        if (uptime >= TimeSpan.FromDays(7))
        {
            return ResourceHealthLevel.InterventionRecommended;
        }

        return uptime >= TimeSpan.FromDays(3)
            ? ResourceHealthLevel.Watch
            : ResourceHealthLevel.Stable;
    }

    public ResourceHealthLevel Overall(params ResourceHealthLevel[] levels)
    {
        var known = levels.Where(level => level != ResourceHealthLevel.Unknown).ToList();
        return known.Count == 0 ? ResourceHealthLevel.Unknown : known.Max();
    }

    public IReadOnlyList<string> BuildRecommendations(
        ResourceHealthLevel cpu,
        ResourceHealthLevel memory,
        TimeSpan uptime,
        IReadOnlyList<ProcessResourceInfo> processes)
    {
        var recommendations = new List<string>();

        if (cpu >= ResourceHealthLevel.InterventionRecommended)
        {
            recommendations.Add("CPU eleve sur la duree : examiner les processus CPU principaux.");
        }
        else if (cpu == ResourceHealthLevel.Watch)
        {
            recommendations.Add("CPU a surveiller : aucun pic isole ne justifie une action automatique.");
        }

        if (memory >= ResourceHealthLevel.InterventionRecommended)
        {
            recommendations.Add("RAM elevee : examiner les applications lourdes avant toute fermeture.");
        }
        else if (memory == ResourceHealthLevel.Watch)
        {
            recommendations.Add("RAM a surveiller : comparer avec les applications actuellement utilisees.");
        }

        if (uptime >= TimeSpan.FromDays(7))
        {
            recommendations.Add("Session Windows longue : envisager un redemarrage manuel si des ralentissements persistent.");
        }
        else if (uptime >= TimeSpan.FromDays(3))
        {
            recommendations.Add("Session Windows active depuis plusieurs jours : surveiller la stabilite.");
        }

        if (processes.Any(process => process.Status == ProcessResourceStatus.Heavy))
        {
            recommendations.Add("Des processus lourds sont detectes. Aucune fermeture n'est automatique.");
        }

        return recommendations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
