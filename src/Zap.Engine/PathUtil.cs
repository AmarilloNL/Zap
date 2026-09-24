namespace Zap.Engine;

public static class PathUtil
{
    /// <summary>Full path without a trailing separator (except for roots like C:\).</summary>
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool IsSameOrInside(string path, string folder)
    {
        var p = Normalize(path);
        var f = Normalize(folder);
        if (p.Equals(f, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = f.EndsWith('\\') ? f : f + '\\';
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>\\?\ form so raw Win32 calls accept paths over 260 chars.</summary>
    public static string LongPath(string fullPath) =>
        fullPath.StartsWith(@"\\?\") ? fullPath
        : fullPath.StartsWith(@"\\") ? @"\\?\UNC\" + fullPath[2..]
        : @"\\?\" + fullPath;
}
