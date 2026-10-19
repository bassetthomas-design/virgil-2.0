using System.Text.RegularExpressions;
using Xunit;

namespace Virgil.Tests;

public sealed class ProcessLaunchArchitectureTests
{
    [Fact]
    public void ApplicationLaunchesMustUseProcessRunner()
    {
        var root = FindRepositoryRoot();
        var source = Path.Combine(root, "src");
        var pattern = new Regex(@"Process\.Start\s*\(|new\s+ProcessStartInfo\b|new\s+Process\s*[({]");
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (relative.Split('/').Any(part => part is "bin" or "obj")
                || relative.StartsWith("Virgil.Infrastructure/", StringComparison.Ordinal)
                || relative.StartsWith("Virgil.ElevatedHelper/", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (pattern.IsMatch(lines[index]))
                {
                    violations.Add($"src/{relative}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ViewsAndMainWindowMustReceiveServicesFromCompositionRoot()
    {
        var app = Path.Combine(FindRepositoryRoot(), "src", "Virgil.App");
        var files = Directory.EnumerateFiles(Path.Combine(app, "Views"), "*.cs")
            .Append(Path.Combine(app, "MainWindow.xaml.cs"));
        var pattern = new Regex(@"new\s+\w+Service\s*\(|ProcessRunner" + "Defaults");
        var violations = files.SelectMany(file => File.ReadAllLines(file)
            .Select((line, index) => (line, index))
            .Where(item => pattern.IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(app, file)}:{item.index + 1}: {item.line.Trim()}"));

        Assert.Empty(violations);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Virgil.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Virgil.sln introuvable depuis AppContext.BaseDirectory.");
    }
}
