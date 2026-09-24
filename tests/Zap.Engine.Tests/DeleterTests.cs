namespace Zap.Engine.Tests;

public class DeleterTests
{
    static JobProgress Delete(params string[] items)
    {
        var p = new JobProgress();
        Deleter.DeletePermanent(Scanner.Scan(items, p, default), p, default);
        return p;
    }

    [Fact]
    public void Deletes_tree_including_read_only_files()
    {
        using var t = new TempDir();
        t.File(@"victim\a.txt", "12345");
        var ro = t.File(@"victim\sub\b.txt");
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        t.Dir(@"victim\empty");

        var p = Delete(Path.Combine(t.Path, "victim"));

        Assert.False(Directory.Exists(Path.Combine(t.Path, "victim")));
        Assert.Empty(p.Errors);
        Assert.Equal(2, p.FilesDone);
        Assert.Equal(6, p.BytesDone);
    }

    [Fact]
    public void Deletes_single_file()
    {
        using var t = new TempDir();
        var f = t.File("one.txt");

        Delete(f);

        Assert.False(File.Exists(f));
    }

    [Fact]
    public void Junction_is_removed_but_target_contents_survive()
    {
        using var t = new TempDir();
        var keep = t.File(@"outside\keep.txt");
        t.File(@"victim\a.txt");
        TempDir.Junction(Path.Combine(t.Path, @"victim\link"), Path.Combine(t.Path, "outside"));

        var p = Delete(Path.Combine(t.Path, "victim"));

        Assert.False(Directory.Exists(Path.Combine(t.Path, "victim")));
        Assert.True(File.Exists(keep));
        Assert.Empty(p.Errors);
    }

    [Fact]
    public void Locked_file_is_one_error_and_the_rest_is_deleted()
    {
        using var t = new TempDir();
        var locked = t.File(@"victim\deep\locked.txt");
        var other = t.File(@"victim\other.txt");
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var p = Delete(Path.Combine(t.Path, "victim"));

            Assert.Equal(locked, Assert.Single(p.Errors).Path); // no extra "folder not empty" noise
            Assert.False(File.Exists(other));
        }
    }

    [Fact]
    public void Protected_root_throws_before_deleting_anything()
    {
        var scan = new ScanResult();
        scan.Roots.Add(@"C:\");
        Assert.Throws<InvalidOperationException>(() => Deleter.DeletePermanent(scan, new JobProgress(), default));
        Assert.Throws<InvalidOperationException>(() => Deleter.Recycle([@"C:\Windows"]));
    }

    [Fact]
    public void Cancelled_token_deletes_nothing()
    {
        using var t = new TempDir();
        var f = t.File(@"victim\a.txt");
        var p = new JobProgress();
        var scan = Scanner.Scan([Path.Combine(t.Path, "victim")], p, default);

        Assert.ThrowsAny<OperationCanceledException>(() => Deleter.DeletePermanent(scan, p, new CancellationToken(true)));
        Assert.True(File.Exists(f));
    }
}
