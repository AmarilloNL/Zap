namespace Zap.Engine.Tests;

public class ScannerTests
{
    [Fact]
    public void Counts_files_sizes_and_directories()
    {
        using var t = new TempDir();
        t.File(@"root\a.txt", "12345");
        t.File(@"root\sub\b.txt", "123");
        t.File(@"root\sub\deeper\c.txt", "1");

        var r = Scanner.Scan([Path.Combine(t.Path, "root")], new JobProgress(), default);

        Assert.Equal(3, r.Files.Count);
        Assert.Equal(9, r.TotalBytes);
        Assert.Equal(3, r.Directories.Count);
        Assert.Equal(Path.Combine(t.Path, "root"), r.Directories[0]); // parent first
    }

    [Fact]
    public void Single_file_item_is_scanned()
    {
        using var t = new TempDir();
        var f = t.File("one.txt", "abc");

        var r = Scanner.Scan([f], new JobProgress(), default);

        Assert.Single(r.Files);
        Assert.Empty(r.Directories);
    }

    [Fact]
    public void Junctions_are_reported_not_followed()
    {
        using var t = new TempDir();
        t.File(@"outside\secret.txt");
        t.File(@"root\a.txt");
        TempDir.Junction(Path.Combine(t.Path, @"root\link"), Path.Combine(t.Path, "outside"));

        var r = Scanner.Scan([Path.Combine(t.Path, "root")], new JobProgress(), default);

        Assert.Single(r.Files);
        Assert.Equal([Path.Combine(t.Path, @"root\link")], r.Links);
    }

    [Fact]
    public void Reports_live_found_counts()
    {
        using var t = new TempDir();
        t.File(@"a\one.txt", "12345");
        t.File(@"b\two.txt", "123");
        var p = new JobProgress();

        Scanner.Scan([Path.Combine(t.Path, "a"), Path.Combine(t.Path, "b")], p, default);

        Assert.Equal(2, p.ScannedFiles);
        Assert.Equal(8, p.ScannedBytes);
    }

    [Fact]
    public void Missing_item_is_an_error()
    {
        using var t = new TempDir();
        var p = new JobProgress();

        Scanner.Scan([Path.Combine(t.Path, "nope")], p, default);

        Assert.Single(p.Errors);
    }
}
