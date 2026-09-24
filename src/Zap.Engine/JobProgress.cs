using System.Collections.Concurrent;

namespace Zap.Engine;

public sealed record JobError(string Path, string Message);

/// <summary>Thread-safe counters the engine writes and the UI polls.</summary>
public sealed class JobProgress
{
    long _totalFiles, _totalBytes, _filesDone, _bytesDone, _filesSkipped, _bytesSkipped, _filesFailed, _scannedFiles, _scannedBytes;
    readonly ConcurrentQueue<JobError> _errors = new();

    public long TotalFiles => Interlocked.Read(ref _totalFiles);
    public long TotalBytes => Interlocked.Read(ref _totalBytes);
    public long FilesDone => Interlocked.Read(ref _filesDone);
    public long BytesDone => Interlocked.Read(ref _bytesDone);
    public long FilesSkipped => Interlocked.Read(ref _filesSkipped);
    public long BytesSkipped => Interlocked.Read(ref _bytesSkipped);
    public long FilesFailed => Interlocked.Read(ref _filesFailed);
    /// <summary>Files found so far while scanning (before totals are known).</summary>
    public long ScannedFiles => Interlocked.Read(ref _scannedFiles);
    public long ScannedBytes => Interlocked.Read(ref _scannedBytes);
    public volatile string? CurrentItem;
    public IReadOnlyCollection<JobError> Errors => _errors;

    internal void SetTotals(long files, long bytes)
    {
        Interlocked.Exchange(ref _totalFiles, files);
        Interlocked.Exchange(ref _totalBytes, bytes);
    }

    internal void FileScanned(long size)
    {
        Interlocked.Increment(ref _scannedFiles);
        Interlocked.Add(ref _scannedBytes, size);
    }

    internal void AddBytes(long bytes) => Interlocked.Add(ref _bytesDone, bytes);
    internal void FileDone() => Interlocked.Increment(ref _filesDone);

    internal void FileSkipped(long size)
    {
        Interlocked.Increment(ref _filesSkipped);
        Interlocked.Add(ref _bytesSkipped, size);
        AddBytes(size);
        FileDone();
    }

    internal void FileFailed(string path, string message)
    {
        Interlocked.Increment(ref _filesFailed);
        AddError(path, message);
        FileDone();
    }

    internal void AddError(string path, string message) => _errors.Enqueue(new JobError(path, message));
}
