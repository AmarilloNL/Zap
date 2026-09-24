namespace Zap.Engine.Tests;

public class CopierTests
{
    static JobProgress Copy(TempDir t, string source, OverwriteMode mode = OverwriteMode.SkipIdentical)
    {
        var p = new JobProgress();
        Copier.Run([Path.Combine(t.Path, source)], Path.Combine(t.Path, "dest"), mode, p, default);
        return p;
    }

    [Fact]
    public void Copies_tree_into_named_folder_with_timestamps()
    {
        using var t = new TempDir();
        var a = t.File(@"src\a.txt", "hello");
        t.File(@"src\sub\b.txt", "world");
        t.Dir(@"src\empty");
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, stamp);

        var p = Copy(t, "src");

        Assert.Equal("hello", File.ReadAllText(Path.Combine(t.Path, @"dest\src\a.txt")));
        Assert.Equal("world", File.ReadAllText(Path.Combine(t.Path, @"dest\src\sub\b.txt")));
        Assert.True(Directory.Exists(Path.Combine(t.Path, @"dest\src\empty")));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(t.Path, @"dest\src\a.txt")));
        Assert.Equal(2, p.FilesDone);
        Assert.Equal(10, p.BytesDone);
        Assert.Empty(p.Errors);
    }

    [Fact]
    public void Single_file_source_lands_in_destination()
    {
        using var t = new TempDir();
        t.File("one.txt", "1");

        Copy(t, "one.txt");

        Assert.Equal("1", File.ReadAllText(Path.Combine(t.Path, @"dest\one.txt")));
    }

    // Target has same size + timestamp but different content, so we can tell whether it was overwritten.
    static (TempDir t, string target) IdenticalLookingTarget()
    {
        var t = new TempDir();
        var src = t.File(@"src\a.txt", "good");
        var target = t.File(@"dest\src\a.txt", "BAD!");
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(src));
        return (t, target);
    }

    [Fact]
    public void SkipIdentical_skips_same_size_and_time()
    {
        var (t, target) = IdenticalLookingTarget();
        using (t)
        {
            var p = Copy(t, "src", OverwriteMode.SkipIdentical);
            Assert.Equal("BAD!", File.ReadAllText(target));
            Assert.Equal(1, p.FilesSkipped);
        }
    }

    [Fact]
    public void SkipIdentical_overwrites_when_time_differs()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt", "good");
        var target = t.File(@"dest\src\a.txt", "BAD!");
        File.SetLastWriteTimeUtc(target, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Copy(t, "src", OverwriteMode.SkipIdentical);

        Assert.Equal("good", File.ReadAllText(target));
    }

    [Fact]
    public void Always_overwrites_identical_looking_file()
    {
        var (t, target) = IdenticalLookingTarget();
        using (t)
        {
            Copy(t, "src", OverwriteMode.Always);
            Assert.Equal("good", File.ReadAllText(target));
        }
    }

    [Fact]
    public void Never_keeps_existing_file()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt", "new content");
        var target = t.File(@"dest\src\a.txt", "old");

        var p = Copy(t, "src", OverwriteMode.Never);

        Assert.Equal("old", File.ReadAllText(target));
        Assert.Equal(1, p.FilesSkipped);
    }

    [Fact]
    public void Overwrites_read_only_destination()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt", "good");
        var target = t.File(@"dest\src\a.txt", "old");
        File.SetAttributes(target, FileAttributes.ReadOnly);

        var p = Copy(t, "src", OverwriteMode.Always);

        Assert.Empty(p.Errors);
        Assert.Equal("good", File.ReadAllText(target));
    }

    [Fact]
    public void Locked_file_is_an_error_and_others_still_copy()
    {
        using var t = new TempDir();
        var locked = t.File(@"src\locked.txt");
        t.File(@"src\fine.txt", "ok");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var p = Copy(t, "src");

        Assert.Equal(1, p.FilesFailed);
        Assert.Equal(locked, Assert.Single(p.Errors).Path);
        Assert.Equal("ok", File.ReadAllText(Path.Combine(t.Path, @"dest\src\fine.txt")));
    }

    [Fact]
    public void Junction_is_skipped_and_reported()
    {
        using var t = new TempDir();
        t.File(@"outside\secret.txt");
        t.File(@"src\a.txt");
        TempDir.Junction(Path.Combine(t.Path, @"src\link"), Path.Combine(t.Path, "outside"));

        var p = Copy(t, "src");

        Assert.False(Path.Exists(Path.Combine(t.Path, @"dest\src\link")));
        Assert.Contains(p.Errors, e => e.Path.EndsWith(@"src\link"));
    }

    [Fact]
    public void Destination_inside_source_is_rejected()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt");

        Assert.Throws<ArgumentException>(() =>
            Copier.Run([Path.Combine(t.Path, "src")], Path.Combine(t.Path, @"src\inner"), OverwriteMode.Always, new JobProgress(), default));
    }

    [Fact]
    public void Copying_onto_itself_is_rejected()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt");

        Assert.Throws<ArgumentException>(() =>
            Copier.Run([Path.Combine(t.Path, "src")], t.Path, OverwriteMode.Always, new JobProgress(), default));
    }

    [Fact]
    public void Cancelled_before_start_copies_nothing()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt");

        Assert.ThrowsAny<OperationCanceledException>(() =>
            Copier.Run([Path.Combine(t.Path, "src")], Path.Combine(t.Path, "dest"), OverwriteMode.Always,
                new JobProgress(), new CancellationToken(true)));
        Assert.False(Directory.Exists(Path.Combine(t.Path, @"dest\src")));
    }

    [Fact]
    public void NativeCopy_cancel_mid_file_removes_partial_target()
    {
        using var t = new TempDir();
        var src = Path.Combine(t.Path, "big.bin");
        File.WriteAllBytes(src, new byte[64 * 1024 * 1024]);
        var target = Path.Combine(t.Path, "big-copy.bin");
        using var cts = new CancellationTokenSource();

        Assert.ThrowsAny<OperationCanceledException>(() => NativeCopy.Copy(src, target, _ => cts.Cancel(), cts.Token));
        Assert.False(File.Exists(target));
    }
}
