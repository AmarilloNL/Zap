namespace Zap.Engine;

/// <summary>
/// Moves items into a folder. On the same volume a folder whose target doesn't exist yet is renamed
/// whole (instant); an existing target is merged file by file, each file a rename. Across volumes each
/// file is copied and its source deleted only after the copy succeeded. Files that stay behind
/// (errors, "never overwrite") keep their source folder alive; emptied source folders are removed.
/// </summary>
public static class Mover
{
    sealed record Op(string Source, string Target, long Size, DateTime LastWriteUtc, bool IsDirectory, bool SameVolume);

    public static void Run(IReadOnlyList<string> sources, string destination, OverwriteMode mode,
        JobProgress progress, CancellationToken ct) => Run(sources, destination, mode, progress, ct, forceCrossVolume: false);

    internal static void Run(IReadOnlyList<string> sources, string destination, OverwriteMode mode,
        JobProgress progress, CancellationToken ct, bool forceCrossVolume)
    {
        destination = PathUtil.Normalize(destination);
        var ops = new List<Op>();
        var createDirs = new List<string>();
        var sourceDirs = new List<string>(); // parents before children

        foreach (var source in sources.Select(PathUtil.Normalize))
        {
            if (ProtectedPaths.Check(source) is { } reason) throw new InvalidOperationException(reason);
            var name = Path.GetFileName(source);
            if (name == "") throw new ArgumentException($"Can't move a whole drive ({source}). Pick a folder instead.");
            var target = Path.Combine(destination, name);
            if (PathUtil.IsSameOrInside(target, source)) throw new ArgumentException($"Can't move {source} into itself.");

            bool sameVolume = !forceCrossVolume &&
                string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(source)) Plan(new DirectoryInfo(source), target, sameVolume, ops, createDirs, sourceDirs, progress, ct);
            else if (File.Exists(source)) AddFile(new FileInfo(source), target, sameVolume, ops, progress);
            else progress.AddError(source, "Not found");
        }

        progress.SetTotals(ops.Count, ops.Sum(o => o.Size));
        progress.Parallelism = Copier.UsesHardDisk(sources, destination) ? 1 : Copier.Parallelism;
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);
        foreach (var dir in createDirs)
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { progress.AddError(dir, ex.Message); }
        }

        Parallel.ForEach(ops, new ParallelOptions { MaxDegreeOfParallelism = progress.Parallelism, CancellationToken = ct },
            op => MoveOne(op, mode, progress, ct));

        for (int i = sourceDirs.Count - 1; i >= 0; i--) // children before parents
        {
            ct.ThrowIfCancellationRequested();
            try { Directory.Delete(sourceDirs[i]); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still holds files that stayed behind; those are already reported or intentionally kept.
            }
        }
    }

    static void Plan(DirectoryInfo dir, string target, bool sameVolume, List<Op> ops, List<string> createDirs,
        List<string> sourceDirs, JobProgress progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Scanner.IsLink(dir)) { progress.AddError(dir.FullName, "Skipped: link/junction not followed"); return; }
        if (sameVolume && !Path.Exists(target))
        {
            ops.Add(new Op(dir.FullName, target, 0, default, IsDirectory: true, SameVolume: true));
            return;
        }

        progress.CurrentItem = dir.FullName;
        createDirs.Add(target);
        sourceDirs.Add(dir.FullName);
        List<FileSystemInfo> children;
        try { children = dir.EnumerateFileSystemInfos("*", Scanner.NoSkipping).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progress.AddError(dir.FullName, ex.Message);
            return;
        }
        foreach (var child in children)
        {
            var childTarget = Path.Combine(target, child.Name);
            if (child is DirectoryInfo sub) Plan(sub, childTarget, sameVolume, ops, createDirs, sourceDirs, progress, ct);
            else if (Scanner.IsLink(child)) progress.AddError(child.FullName, "Skipped: link/junction not followed");
            else AddFile((FileInfo)child, childTarget, sameVolume, ops, progress);
        }
    }

    static void AddFile(FileInfo file, string target, bool sameVolume, List<Op> ops, JobProgress progress)
    {
        ops.Add(new Op(file.FullName, target, file.Length, file.LastWriteTimeUtc, IsDirectory: false, sameVolume));
        progress.FileScanned(file.Length);
    }

    static void MoveOne(Op op, OverwriteMode mode, JobProgress progress, CancellationToken ct)
    {
        progress.CurrentItem = op.Source;
        long reported = 0;
        try
        {
            if (op.IsDirectory)
            {
                Directory.Move(op.Source, op.Target);
                progress.FileDone();
                return;
            }

            var existing = new FileInfo(op.Target);
            if (existing.Exists)
            {
                if (mode == OverwriteMode.Never) { progress.FileSkipped(op.Size); return; } // source stays put
                if (mode == OverwriteMode.SkipIdentical && Copier.IsIdentical(existing, op.Size, op.LastWriteUtc))
                {
                    DeleteSource(op.Source); // already at the target: the source is a duplicate
                    progress.FileSkipped(op.Size);
                    return;
                }
                Copier.ClearBlockingAttributes(existing);
            }

            if (op.SameVolume)
                File.Move(op.Source, op.Target, overwrite: true);
            else
            {
                NativeCopy.Copy(op.Source, op.Target, transferred =>
                {
                    progress.AddBytes(transferred - reported);
                    reported = transferred;
                }, ct);
                DeleteSource(op.Source); // only after the copy succeeded
            }
            progress.AddBytes(op.Size - reported);
            progress.FileDone();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            progress.AddBytes(op.Size - reported);
            progress.FileFailed(op.Source, ex.Message);
        }
    }

    static void DeleteSource(string path) => Deleter.ClearReadOnlyAndRetry(path, File.Delete);
}
