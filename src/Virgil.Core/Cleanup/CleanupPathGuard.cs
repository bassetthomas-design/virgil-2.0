using System;
using System.IO;

namespace Virgil.Core.Cleanup;

public static class CleanupPathGuard
{
    private static readonly StringComparison WindowsPathComparison = StringComparison.OrdinalIgnoreCase;

    public static bool IsStrictlyUnderRoot(string candidatePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        try
        {
            var candidate = Path.GetFullPath(candidatePath);
            var root = Path.GetFullPath(rootPath);
            var trimmedCandidate = TrimDirectorySeparator(candidate);
            var trimmedRoot = TrimDirectorySeparator(root);

            if (string.Equals(trimmedCandidate, trimmedRoot, WindowsPathComparison))
            {
                return false;
            }

            return candidate.StartsWith(EnsureTrailingSeparator(root), WindowsPathComparison);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryValidateContainedFile(
        string candidatePath,
        string rootPath,
        out string fullPath,
        out string reason)
    {
        fullPath = string.Empty;
        reason = string.Empty;

        if (!TryNormalizePath(candidatePath, rootPath, out fullPath, out var fullRoot, out reason))
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            reason = "Fichier absent.";
            return false;
        }

        if (ContainsReparsePoint(fullPath, fullRoot))
        {
            reason = "Point de reanalyse refuse.";
            return false;
        }

        return true;
    }

    public static bool TryNormalizePath(
        string candidatePath,
        string rootPath,
        out string fullPath,
        out string fullRoot,
        out string reason)
    {
        fullPath = string.Empty;
        fullRoot = string.Empty;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
        {
            reason = "Chemin vide.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(candidatePath);
            fullRoot = Path.GetFullPath(rootPath);
        }
        catch
        {
            reason = "Chemin invalide.";
            return false;
        }

        if (string.Equals(TrimDirectorySeparator(fullPath), TrimDirectorySeparator(fullRoot), WindowsPathComparison))
        {
            reason = "Racine de zone refusee.";
            return false;
        }

        if (!fullPath.StartsWith(EnsureTrailingSeparator(fullRoot), WindowsPathComparison))
        {
            reason = "Chemin hors zone autorisee.";
            return false;
        }

        return true;
    }

    public static bool ContainsReparsePoint(string candidatePath, string rootPath)
    {
        if (!TryNormalizePath(candidatePath, rootPath, out var fullPath, out var fullRoot, out _))
        {
            return true;
        }

        if (HasReparsePointAttribute(fullRoot))
        {
            return true;
        }

        var relative = Path.GetRelativePath(fullRoot, fullPath);
        var current = TrimDirectorySeparator(fullRoot);

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrWhiteSpace(segment) || segment == ".")
            {
                continue;
            }

            current = Path.Combine(current, segment);

            if (HasReparsePointAttribute(current))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasReparsePoint(FileAttributes attributes)
    {
        return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
    }

    public static bool HasReparsePointAtPath(string path)
    {
        return HasReparsePointAttribute(path);
    }

    private static bool HasReparsePointAttribute(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return HasReparsePoint(File.GetAttributes(path));
            }

            if (Directory.Exists(path))
            {
                return HasReparsePoint(new DirectoryInfo(path).Attributes);
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (fullPath.EndsWith(Path.DirectorySeparatorChar) || fullPath.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return fullPath;
        }

        return fullPath + Path.DirectorySeparatorChar;
    }

    private static string TrimDirectorySeparator(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
