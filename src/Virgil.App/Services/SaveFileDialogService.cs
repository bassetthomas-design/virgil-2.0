using Microsoft.Win32;

namespace Virgil.App.Services;

public sealed class SaveFileDialogService : IFileSaveDialog
{
    public string? PickSavePath(string title, string filter, string defaultExt, string fileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExt,
            FileName = fileName,
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
