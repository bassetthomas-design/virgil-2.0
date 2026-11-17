using System.Text;
using Microsoft.VisualBasic.FileIO;
using Virgil.Core.Cleanup;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

[CollectionDefinition("Corbeille réelle", DisableParallelization = true)]
public sealed class RealRecycleBinCollection;

[Collection("Corbeille réelle")]
[Trait("Category", "Windows")]
public sealed class LotCorbeilleVidageWindowsTests
{
    [Fact]
    public async Task Real_recycle_bin_empties_only_the_deposited_test_file_and_reads_back_zero()
    {
        var bin = new CleanupDisk().RecycleBin;
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-t3b");
        Directory.CreateDirectory(directory);
        var lines = new List<string>();
        void Write() => File.WriteAllLines(Path.Combine(directory, "corbeille-runner.txt"), lines, new UTF8Encoding(false));
        async Task<RecycleBinActionResult> Empty() => await Task.Run(() =>
        {
            lines.Add("ApartmentState=" + Thread.CurrentThread.GetApartmentState());
            Write();
            return bin.Empty();
        }).WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var s0 = bin.Query();
            lines.Add("s0=" + s0);
            Write();
            Assert.True(s0.EstimateAvailable);
            Assert.Equal(0, s0.ItemCount);
            var r0 = await Empty();
            lines.Add("r0=" + r0);
            Write();
            Assert.Equal(new RecycleBinActionResult(true, 0, 0, null), r0);
            var path = Path.Combine(Path.GetTempPath(), "virgil-t3b-" + Guid.NewGuid().ToString("N") + ".txt");
            await File.WriteAllBytesAsync(path, [86, 73, 82, 71, 76]);
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            lines.Add("File.Exists=" + File.Exists(path));
            Write();
            Assert.False(File.Exists(path));
            var s1 = bin.Query();
            lines.Add("s1=" + s1);
            Write();
            Assert.Equal(1, s1.ItemCount);
            var r1 = await Empty();
            lines.Add("r1=" + r1);
            Write();
            Assert.Equal(new RecycleBinActionResult(true, 1, s1.SizeBytes, null), r1);
            var s2 = bin.Query();
            lines.Add("s2=" + s2);
            Write();
            Assert.Equal(new RecycleBinState(true, 0, 0), s2);
        }
        finally { Write(); }
    }
}
