namespace Zap.Engine.Tests;

public class MoverTests
{
    static JobProgress Move(TempDir t, string source, OverwriteMode mode = OverwriteMode.SkipIdentical, bool crossVolume = false)
    {
        var p = new JobProgress();
        Mover.Run([Path.Combine(t.Path, source)], Path.Combine(t.Path, "dest"), mode, p, default, crossVolume);
        return p;
    }

    [Fact]
    public void Folder_is_renamed_whole_when_target_is_missing()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt", "hello");
        t.File(@"src\sub\b.txt", "world");
        t.Dir("dest");

        var p = Move(t, "src");

        Assert.False(Directory.Exists(Path.Combine(t.Path, "src")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(t.Path, @"dest\src\a.txt")));
        Assert.Equal("world", File.ReadAllText(Path.Combine(t.Path, @"dest\src\sub\b.txt")));
        Assert.Equal(1, p.TotalFiles); // one rename, not two file moves
        Assert.Empty(p.Errors);
    }

    [Fact]
    public void Single_file_is_moved()
    {
        using var t = new TempDir();
        t.File("one.txt", "1");
        t.Dir("dest");

        Move(t, "one.txt");

        Assert.False(File.Exists(Path.Combine(t.Path, "one.txt")));
        Assert.Equal("1", File.ReadAllText(Path.Combine(t.Path, @"dest\one.txt")));
    }

    [Fact]
    public void Existing_target_folder_is_merged_and_emptied_source_removed()
    {
        using var t = new TempDir();
        t.File(@"src\new.txt", "new");
        t.File(@"src\deep\newer.txt", "deep");
        t.File(@"dest\src\already.txt", "kept");

        var p = Move(t, "src");

        Assert.Equal("new", File.ReadAllText(Path.Combine(t.Path, @"dest\src\new.txt")));
        Assert.Equal("deep", File.ReadAllText(Path.Combine(t.Path, @"dest\src\deep\newer.txt")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(t.Path, @"dest\src\already.txt")));
        Assert.False(Directory.Exists(Path.Combine(t.Path, "src")));
        Assert.Empty(p.Errors);
    }

    [Fact]
    public void SkipIdentical_drops_the_duplicate_source_and_keeps_target()
    {
        using var t = new TempDir();
        var src = t.File(@"src\a.txt", "same");
        var target = t.File(@"dest\src\a.txt", "same");
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(src));

        var p = Move(t, "src");

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(target));
        Assert.Equal(1, p.FilesSkipped);
    }

    [Fact]
    public void SkipIdentical_overwrites_a_different_file()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt", "from source");
        var target = t.File(@"dest\src\a.txt", "old");

        Move(t, "src");

        Assert.Equal("from source", File.ReadAllText(target));
        Assert.False(Directory.Exists(Path.Combine(t.Path, "src")));
    }

    [Fact]
    public void Never_keeps_target_and_leaves_source_file_in_place()
    {
        using var t = new TempDir();
        var src = t.File(@"src\a.txt", "from source");
        t.File(@"src\b.txt", "moves");
        var target = t.File(@"dest\src\a.txt", "old");

        var p = Move(t, "src", OverwriteMode.Never);

        Assert.Equal("old", File.ReadAllText(target));
        Assert.Equal("from source", File.ReadAllText(src)); // left behind, not lost
        Assert.Equal("moves", File.ReadAllText(Path.Combine(t.Path, @"dest\src\b.txt")));
        Assert.Equal(1, p.FilesSkipped);
    }

    [Fact]
    public void Always_overwrites_read_only_target()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt", "new");
        var target = t.File(@"dest\src\a.txt", "old");
        File.SetAttributes(target, FileAttributes.ReadOnly);

        var p = Move(t, "src", OverwriteMode.Always);

        Assert.Empty(p.Errors);
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void Locked_file_stays_in_source_and_the_rest_moves()
    {
        using var t = new TempDir();
        var locked = t.File(@"src\locked.txt");
        t.File(@"src\fine.txt", "ok");
        t.Dir(@"dest\src"); // force a merge so files move one by one
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var p = Move(t, "src");

            Assert.Equal(locked, Assert.Single(p.Errors).Path);
            Assert.Equal("ok", File.ReadAllText(Path.Combine(t.Path, @"dest\src\fine.txt")));
        }
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void Cross_volume_copies_then_deletes_sources()
    {
        using var t = new TempDir();
        var a = t.File(@"src\a.txt", "hello");
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, stamp);
        t.File(@"src\sub\b.txt", "world");

        var p = Move(t, "src", crossVolume: true);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(t.Path, @"dest\src\a.txt")));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(t.Path, @"dest\src\a.txt")));
        Assert.Equal("world", File.ReadAllText(Path.Combine(t.Path, @"dest\src\sub\b.txt")));
        Assert.False(Directory.Exists(Path.Combine(t.Path, "src")));
        Assert.Equal(10, p.BytesDone);
    }

    [Fact]
    public void Cross_volume_never_overwrite_keeps_the_source()
    {
        using var t = new TempDir();
        var src = t.File(@"src\a.txt", "from source");
        t.File(@"dest\src\a.txt", "old");

        Move(t, "src", OverwriteMode.Never, crossVolume: true);

        Assert.True(File.Exists(src));
    }

    [Fact]
    public void Junction_inside_source_is_not_followed()
    {
        using var t = new TempDir();
        var keep = t.File(@"outside\keep.txt");
        t.File(@"src\a.txt");
        TempDir.Junction(Path.Combine(t.Path, @"src\link"), Path.Combine(t.Path, "outside"));
        t.Dir(@"dest\src"); // merge, so the walker meets the junction

        var p = Move(t, "src");

        Assert.True(File.Exists(keep));
        Assert.Contains(p.Errors, e => e.Path.EndsWith(@"src\link"));
    }

    [Fact]
    public void Moving_into_itself_is_rejected()
    {
        using var t = new TempDir();
        t.File(@"src\a.txt");

        Assert.Throws<ArgumentException>(() =>
            Mover.Run([Path.Combine(t.Path, "src")], Path.Combine(t.Path, @"src\inner"), OverwriteMode.Always, new JobProgress(), default));
    }

    [Fact]
    public void Protected_folder_cannot_be_moved()
    {
        using var t = new TempDir();
        Assert.Throws<InvalidOperationException>(() =>
            Mover.Run([Environment.GetFolderPath(Environment.SpecialFolder.Windows)], t.Path, OverwriteMode.Always, new JobProgress(), default));
    }

    [Fact]
    public void Cancelled_before_start_moves_nothing()
    {
        using var t = new TempDir();
        var f = t.File(@"src\a.txt");
        t.Dir("dest");

        Assert.ThrowsAny<OperationCanceledException>(() =>
            Mover.Run([Path.Combine(t.Path, "src")], Path.Combine(t.Path, "dest"), OverwriteMode.Always,
                new JobProgress(), new CancellationToken(true)));
        Assert.True(File.Exists(f));
    }
}
