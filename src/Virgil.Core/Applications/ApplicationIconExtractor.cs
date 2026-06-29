namespace Virgil.Core.Applications;

public sealed class ApplicationIconExtractor
{
    public static string? CleanIconPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith('"'))
        {
            var closing = candidate.IndexOf('"', 1);
            if (closing > 1)
            {
                candidate = candidate[1..closing];
            }
        }
        else
        {
            var comma = candidate.IndexOf(',');
            if (comma > 0)
            {
                candidate = candidate[..comma];
            }
        }

        candidate = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
        return File.Exists(candidate) ? candidate : null;
    }

    public string? ResolveIconPath(string? displayIcon, string? installLocation)
    {
        var direct = CleanIconPath(displayIcon);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(installLocation, "*.ico", SearchOption.TopDirectoryOnly).FirstOrDefault() ??
                Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
