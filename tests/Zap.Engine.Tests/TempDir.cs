using System.Diagnostics;

namespace Zap.Engine.Tests;

sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zap-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string relative, string content = "x")
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string Dir(string relative) => Directory.CreateDirectory(System.IO.Path.Combine(Path, relative)).FullName;

    public static void Junction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
            { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("mklink failed");
    }

    public void Dispose()
    {
        if (!Directory.Exists(Path)) return;
        // Recursive Directory.Delete fails on junctions, so remove those (not their targets) first.
        var all = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
        foreach (var d in Directory.EnumerateDirectories(Path, "*", all).ToList())
            if (System.IO.File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(d);
        foreach (var f in Directory.EnumerateFiles(Path, "*", all))
            System.IO.File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Path, true);
    }
}
