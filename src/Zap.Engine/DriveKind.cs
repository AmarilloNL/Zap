using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Zap.Engine;

/// <summary>
/// Asks Windows whether a path is on a spinning hard disk. On those, parallel copies make the
/// read head jump between files and are slower than one-at-a-time (measured ~2x on a USB HDD).
/// </summary>
public static class DriveKind
{
    public static bool IsHardDisk(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return false; // network shares benefit from parallelism
        using var volume = CreateFile(@"\\.\" + root.TrimEnd('\\'), 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (volume.IsInvalid) return false;
        var query = new StoragePropertyQuery { PropertyId = StorageDeviceSeekPenaltyProperty };
        return DeviceIoControl(volume, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(),
                   out SeekPenaltyDescriptor result, Marshal.SizeOf<SeekPenaltyDescriptor>(), out _, IntPtr.Zero)
               && result.IncursSeekPenalty;
    }

    const uint IoctlStorageQueryProperty = 0x2D1400;
    const int StorageDeviceSeekPenaltyProperty = 7;

    [StructLayout(LayoutKind.Sequential)]
    struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType; // 0 = PropertyStandardQuery
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SeekPenaltyDescriptor
    {
        public uint Version;
        public uint Size;
        [MarshalAs(UnmanagedType.U1)] public bool IncursSeekPenalty;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes,
        FileMode creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, ref StoragePropertyQuery inBuffer, int inBufferSize,
        out SeekPenaltyDescriptor outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);
}
