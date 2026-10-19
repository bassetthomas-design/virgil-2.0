using Virgil.Contracts.Elevated;

namespace Virgil.ElevatedHelper;

internal static class ElevatedDiskInspector
{
    public static ElevatedPathGuard CreatePathGuard() => new(InspectPath, Directory.Exists);

    private static ElevatedPathEntry InspectPath(string path)
    {
        try
        {
            return new ElevatedPathEntry(true, File.GetAttributes(path));
        }
        catch (FileNotFoundException)
        {
            return ElevatedPathEntry.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return ElevatedPathEntry.Missing;
        }
    }
}
