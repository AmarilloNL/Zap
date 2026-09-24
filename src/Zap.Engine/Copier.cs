namespace Zap.Engine;

public enum OverwriteMode { SkipIdentical, Always, Never }

public static class Copier
{
    public const int Parallelism = 8;
    static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2); // FAT/exFAT store times in 2 s steps

    public static void Run(IReadOnlyList<string> sources, string destination, OverwriteMode mode,
        JobProgress progress, CancellationToken ct)
    {
        destination = PathUtil.Normalize(destination);
        var jobs = new List<(ScannedFile File, string Target)>();
        var dirs = new List<string>();

        foreach (var source in sources.Select(PathUtil.Normalize))
        {
            var name = Path.GetFileName(source);
            if (name == "") throw new ArgumentException($"Can't copy a whole drive ({source}). Pick a folder instead.");
            var targetRoot = Path.Combine(destination, name);
            if (PathUtil.IsSameOrInside(targetRoot, source))
                throw new ArgumentException($"Can't copy {source} into itself.");

            var scan = Scanner.Scan([source], progress, ct);
            string Map(string path) => targetRoot + path[source.Length..];
            dirs.AddRange(scan.Directories.Select(Map));
            jobs.AddRange(scan.Files.Select(f => (f, Map(f.Path))));
            foreach (var link in scan.Links) progress.AddError(link, "Skipped: link/junction not followed");
        }

        progress.SetTotals(jobs.Count, jobs.Sum(j => j.File.Size));
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);
        foreach (var dir in dirs)
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { progress.AddError(dir, ex.Message); }
        }

        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            job => CopyOne(job.File, job.Target, mode, progress, ct));
    }

    static void CopyOne(ScannedFile file, string target, OverwriteMode mode, JobProgress progress, CancellationToken ct)
    {
        progress.CurrentItem = file.Path;
        long reported = 0;
        try
        {
            var existing = new FileInfo(target);
            if (existing.Exists)
            {
                bool skip = mode switch
                {
                    OverwriteMode.Never => true,
                    OverwriteMode.Always => false,
                    _ => existing.Length == file.Size && (existing.LastWriteTimeUtc - file.LastWriteUtc).Duration() <= TimeTolerance,
                };
                if (skip) { progress.FileSkipped(file.Size); return; }
                const FileAttributes blocking = FileAttributes.ReadOnly | FileAttributes.Hidden;
                if ((existing.Attributes & blocking) != 0) existing.Attributes &= ~blocking;
            }

            NativeCopy.Copy(file.Path, target, transferred =>
            {
                progress.AddBytes(transferred - reported);
                reported = transferred;
            }, ct);
            progress.AddBytes(file.Size - reported);
            progress.FileDone();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            progress.AddBytes(file.Size - reported); // keep the overall bar honest
            progress.FileFailed(file.Path, ex.Message);
        }
    }
}
