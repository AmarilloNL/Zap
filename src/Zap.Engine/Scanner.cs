namespace Zap.Engine;

public sealed record ScannedFile(string Path, long Size, DateTime LastWriteUtc);

public sealed class ScanResult
{
    public List<string> Roots { get; } = [];
    public List<ScannedFile> Files { get; } = [];
    /// <summary>Real directories, each parent listed before its children.</summary>
    public List<string> Directories { get; } = [];
    /// <summary>Junctions and symlinks. Never followed.</summary>
    public List<string> Links { get; } = [];
    public long TotalBytes => Files.Sum(f => f.Size);
}

public static class Scanner
{
    static readonly EnumerationOptions NoSkipping = new() { AttributesToSkip = 0 };

    public static ScanResult Scan(IEnumerable<string> items, JobProgress progress, CancellationToken ct)
    {
        var result = new ScanResult();
        foreach (var item in items)
        {
            var path = PathUtil.Normalize(item);
            result.Roots.Add(path);
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (!info.Exists) { progress.AddError(path, "Not found"); continue; }
            Add(info, result, progress, ct);
        }
        return result;
    }

    static void Add(FileSystemInfo root, ScanResult result, JobProgress progress, CancellationToken ct)
    {
        var stack = new Stack<FileSystemInfo>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var info = stack.Pop();
            if (IsLink(info)) { result.Links.Add(info.FullName); continue; }
            if (info is FileInfo f) { result.Files.Add(new ScannedFile(f.FullName, f.Length, f.LastWriteTimeUtc)); continue; }

            var dir = (DirectoryInfo)info;
            result.Directories.Add(dir.FullName);
            progress.CurrentItem = dir.FullName;
            try
            {
                foreach (var child in dir.EnumerateFileSystemInfos("*", NoSkipping)) stack.Push(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                progress.AddError(dir.FullName, ex.Message);
            }
        }
    }

    // Cloud placeholders (OneDrive) are reparse points too, but have no LinkTarget: treat them as normal.
    static bool IsLink(FileSystemInfo info) =>
        info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget != null;
}
