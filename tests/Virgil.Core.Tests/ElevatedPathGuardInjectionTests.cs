using Virgil.Contracts.Elevated;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class ElevatedPathGuardInjectionTests
{
    [Fact]
    public void Missing_volume_is_checked_before_any_entry_using_only_injected_inspection()
    {
        var inspected = new List<string>();
        var guard = new ElevatedPathGuard(
            path => throw new InvalidOperationException("Entry inspection must not run."),
            volume => { inspected.Add(volume); return false; });

        var error = Assert.Throws<InvalidOperationException>(() => guard.ValidateRoot(@"C:\nonexistent", true));

        Assert.Equal("Racine Windows LocalAppData invalide.", error.Message);
        Assert.Equal(new[] { @"C:\" }, inspected);
    }

    [Fact]
    public void Injected_entries_are_checked_in_original_order_without_touching_disk()
    {
        var inspected = new List<string>();
        var guard = new ElevatedPathGuard(path =>
        {
            inspected.Add(path);
            return new ElevatedPathEntry(true, FileAttributes.Directory);
        }, volume => { inspected.Add(volume); return true; });

        Assert.Equal(@"C:\nonexistent\Virgil\Temp", guard.ValidateRoot(@"C:\nonexistent", false));
        Assert.Equal(new[] { @"C:\", @"C:\nonexistent", @"C:\nonexistent\Virgil", @"C:\nonexistent\Virgil\Temp" }, inspected);
    }

    [Fact]
    public void Inspection_failure_keeps_original_exception_and_message()
    {
        var failure = new IOException("injected failure");
        var guard = new ElevatedPathGuard(_ => throw failure, _ => true);

        var error = Assert.Throws<InvalidOperationException>(() => guard.ValidateRoot(@"C:\nonexistent", false));

        Assert.Equal("Verification physique du chemin impossible.", error.Message);
        Assert.Same(failure, error.InnerException);
    }
}
