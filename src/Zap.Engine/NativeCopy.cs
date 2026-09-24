using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Zap.Engine;

/// <summary>Win32 CopyFileEx: native speed, keeps timestamps/attributes, reports bytes, cancellable.</summary>
internal static class NativeCopy
{
    delegate uint CopyProgressRoutine(long totalFileSize, long totalBytesTransferred, long streamSize,
        long streamBytesTransferred, uint streamNumber, uint callbackReason, IntPtr sourceFile, IntPtr destinationFile, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CopyFileEx(string existingFileName, string newFileName, CopyProgressRoutine progressRoutine,
        IntPtr data, IntPtr cancel, uint copyFlags);

    const uint ProgressContinue = 0, ProgressCancel = 1; // PROGRESS_CANCEL also deletes the partial target
    const int ErrorRequestAborted = 1235;

    public static void Copy(string source, string target, Action<long> onTransferred, CancellationToken ct)
    {
        CopyProgressRoutine routine = (_, transferred, _, _, _, _, _, _, _) =>
        {
            if (ct.IsCancellationRequested) return ProgressCancel;
            onTransferred(transferred);
            return ct.IsCancellationRequested ? ProgressCancel : ProgressContinue;
        };
        bool ok = CopyFileEx(PathUtil.LongPath(source), PathUtil.LongPath(target), routine, IntPtr.Zero, IntPtr.Zero, 0);
        GC.KeepAlive(routine);
        if (ok) return;
        int error = Marshal.GetLastPInvokeError();
        if (error == ErrorRequestAborted) throw new OperationCanceledException(ct);
        throw new IOException(new Win32Exception(error).Message);
    }
}
