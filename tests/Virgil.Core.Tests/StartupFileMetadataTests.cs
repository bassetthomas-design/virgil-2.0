using Virgil.Infrastructure.Windows.Startup;
using Xunit;

namespace Virgil.Core.Tests;

public sealed class StartupFileMetadataTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --start", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Apps\\app.exe /background", "C:\\Apps\\app.exe")]
    [InlineData("  C:\\Apps\\app.EXE\t/start  ", "C:\\Apps\\app.EXE")]
    [InlineData("C:\\Program Files\\App\\app.exe --start", null)]
    [InlineData("\"C:\\Apps\\app.exe\"unexpected", null)]
    [InlineData("\"C:\\Apps\\app.exe", null)]
    [InlineData("app.exe /start", null)]
    [InlineData("\\\\server\\share\\app.exe", null)]
    [InlineData("https://example.com/app.exe", null)]
    [InlineData("", null)]
    public void Command_parser_never_guesses_ambiguous_or_remote_paths(string command, string? expected) =>
        Assert.Equal(expected, StartupFileMetadata.ParseCommandPath(command));

    [Theory]
    [InlineData("C:\\Program Files\\App\\app.exe", "C:\\Program Files\\App\\app.exe")]
    [InlineData("\"C:\\Program Files\\App\\app.exe\"", "C:\\Program Files\\App\\app.exe")]
    [InlineData("\\\\server\\share\\app.exe", null)]
    public void Explicit_file_paths_preserve_spaces_without_parsing_arguments(string command, string? expected) =>
        Assert.Equal(expected, StartupFileMetadata.ParseCommandPath(command, directFile: true));

    [Theory]
    [InlineData(0u, "Signature valide (révocation non vérifiée)")]
    [InlineData(0x800B0100u, "Fichier non signé")]
    [InlineData(0x800B0109u, "Signature non validée (0x800B0109)")]
    public void Only_successful_trust_verification_is_reported_valid(uint code, string expected) =>
        Assert.Equal(expected, StartupFileMetadata.SignatureStatus(code));

    [Fact]
    [Trait("Category", "Windows")]
    public void Missing_local_file_is_explicitly_unverified()
    {
        var metadata = StartupFileMetadata.Read(@"C:\Virgil-missing-file-for-test.exe");
        Assert.Equal("Éditeur indisponible", metadata.Publisher);
        Assert.Contains("non vérifiée", metadata.Signature);
    }
}
