namespace Zap.Engine.Tests;

public class DriveKindTests
{
    [Fact] public void Network_paths_are_not_hard_disks() => Assert.False(DriveKind.IsHardDisk(@"\\server\share\folder"));

    [Fact] public void Missing_drive_is_not_a_hard_disk() => Assert.False(DriveKind.IsHardDisk(@"Q:\nothing\here"));

    [Fact]
    public void Copy_records_the_parallelism_it_used()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt");
        var p = new JobProgress();

        Copier.Run([Path.Combine(t.Path, "src")], Path.Combine(t.Path, "dest"), OverwriteMode.Always, p, default);

        Assert.Equal(DriveKind.IsHardDisk(t.Path) ? 1 : Copier.Parallelism, p.Parallelism);
    }
}
