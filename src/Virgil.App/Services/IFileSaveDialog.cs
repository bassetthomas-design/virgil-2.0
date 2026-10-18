namespace Virgil.App.Services;

public interface IFileSaveDialog
{
    string? PickSavePath(string title, string filter, string defaultExt, string fileName);
}
