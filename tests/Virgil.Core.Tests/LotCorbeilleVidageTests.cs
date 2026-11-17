using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class LotCorbeilleVidageTests
{
    [Theory]
    [InlineData("videe")]
    [InlineData("sfalse")]
    [InlineData("dejavide")]
    [InlineData("incomplet")]
    [InlineData("refus")]
    [InlineData("inverifiable")]
    [InlineData("avantillisible")]
    public void Empty_result_comes_from_the_state_read_back_after_the_call(string scenario)
    {
        var before = new RecycleBinState(true, 3, 1024);
        var after = new RecycleBinState(true, 0, 0);
        var hresult = 0;
        var expected = new RecycleBinActionResult(true, 3, 1024, null);
        switch (scenario)
        {
            case "sfalse": hresult = 1; break;
            case "dejavide":
                before = new(true, 0, 0);
                hresult = unchecked((int)0x8000FFFF);
                expected = new(true, 0, 0, null);
                break;
            case "incomplet":
                after = new(true, 1, 100);
                expected = new(false, 2, 924, "Windows n'a pas vidé toute la corbeille : 1 élément(s) restant(s) (code 0x00000000).");
                break;
            case "refus":
                hresult = unchecked((int)0x80004005);
                after = new(true, 3, 1024);
                expected = new(false, 0, 0, "Windows n'a pas vidé toute la corbeille : 3 élément(s) restant(s) (code 0x80004005).");
                break;
            case "inverifiable":
                after = new(false, 0, 0, "Estimation de la corbeille indisponible.");
                expected = new(false, 0, 0, "Windows ne permet pas de confirmer le vidage de la corbeille (code 0x00000000). Ouvrez la Corbeille pour vérifier.");
                break;
            case "avantillisible":
                before = new(false, 0, 0, "Estimation de la corbeille indisponible.");
                expected = new(true, 0, 0, null);
                break;
        }
        var type = typeof(RecycleBinState).Assembly.GetType("Virgil.Core.Cleanup.RecycleBinEmptyPolicy");
        Assert.NotNull(type);
        var method = type.GetMethod("Interpret");
        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [before, hresult, after]));
    }

    [Fact]
    public void Core_recycle_bin_file_has_no_native_call()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var nativeLines = File.ReadAllLines(Path.Combine(root, "src", "Virgil.Core", "Cleanup", "RecycleBinService.cs"))
            .Where(line => line.Contains("DllImport", StringComparison.Ordinal) ||
                line.Contains("LibraryImport", StringComparison.Ordinal) || line.Contains("shell32", StringComparison.Ordinal)).ToArray();
        Assert.Empty(nativeLines);
    }

    [Fact]
    public void Recycle_bin_runs_with_the_session_account_only()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        Assert.Equal(new[] { "        <requestedExecutionLevel level=\"asInvoker\" uiAccess=\"false\" />" },
            File.ReadAllLines(Path.Combine(root, "src", "Virgil.App", "app.manifest"))
                .Where(line => line.Contains("requestedExecutionLevel", StringComparison.Ordinal)).ToArray());
        var elevatedFiles = Directory.EnumerateFiles(Path.Combine(root, "src", "Virgil.ElevatedHelper"), "*", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => File.ReadAllText(path).Contains("Recycle", StringComparison.OrdinalIgnoreCase) ||
                File.ReadAllText(path).Contains("Corbeille", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Empty(elevatedFiles);
    }
}
