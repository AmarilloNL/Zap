using Microsoft.VisualBasic.FileIO;

namespace Zap.Engine;

public static class Deleter
{
    public const int Parallelism = 8;

    public static void DeletePermanent(ScanResult scan, JobProgress progress, CancellationToken ct)
    {
        EnsureNotProtected(scan.Roots);
        progress.SetTotals(scan.Files.Count, scan.TotalBytes);
        ct.ThrowIfCancellationRequested();

        Parallel.ForEach(scan.Files, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct }, file =>
        {
            progress.CurrentItem = file.Path;
            try
            {
                ClearReadOnlyAndRetry(file.Path, File.Delete);
                progress.AddBytes(file.Size);
                progress.FileDone();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                progress.AddBytes(file.Size);
                progress.FileFailed(file.Path, ex.Message);
            }
        });

        // A link is removed as a link: Directory.Delete(non-recursive) / File.Delete never touch the target.
        foreach (var link in scan.Links)
            Try(link, progress, () =>
            {
                if (File.GetAttributes(link).HasFlag(FileAttributes.Directory)) Directory.Delete(link);
                else File.Delete(link);
            });

        for (int i = scan.Directories.Count - 1; i >= 0; i--) // children before parents
        {
            ct.ThrowIfCancellationRequested();
            var dir = scan.Directories[i];
            Try(dir, progress, () =>
            {
                try { ClearReadOnlyAndRetry(dir, d => Directory.Delete(d)); }
                catch (IOException) when (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    // Not empty because a file inside failed; that file is already reported.
                }
            });
        }
    }

    public static void Recycle(IReadOnlyList<string> items)
    {
        EnsureNotProtected(items);
        foreach (var item in items)
        {
            if (Directory.Exists(item))
                FileSystem.DeleteDirectory(item, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else if (File.Exists(item))
                FileSystem.DeleteFile(item, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }

    static void EnsureNotProtected(IEnumerable<string> roots)
    {
        foreach (var root in roots)
            if (ProtectedPaths.Check(root) is { } reason) throw new InvalidOperationException(reason);
    }

    static void ClearReadOnlyAndRetry(string path, Action<string> delete)
    {
        try { delete(path); }
        catch (UnauthorizedAccessException) when (File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            delete(path);
        }
    }

    static void Try(string path, JobProgress progress, Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { progress.AddError(path, ex.Message); }
    }
}
