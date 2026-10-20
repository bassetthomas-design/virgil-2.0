namespace Virgil.Core.Cleanup;

public enum KnownFolder { Pictures, Videos, Music, Documents, Desktop, Downloads, OneDrive }

public sealed record KnownFolderPath(KnownFolder Kind, string Path);

public interface IKnownFoldersProvider
{
    IReadOnlyList<KnownFolderPath> GetFolders();
}
