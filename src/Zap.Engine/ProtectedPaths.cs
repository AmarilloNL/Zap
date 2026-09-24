namespace Zap.Engine;

/// <summary>Stops the app from deleting drive roots and system folders.</summary>
public static class ProtectedPaths
{
    static string Folder(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

    // Deleting these, or anything that contains them, is refused.
    static readonly string[] NoDeleteOrAncestor = new[]
    {
        Folder(Environment.SpecialFolder.Windows),
        Folder(Environment.SpecialFolder.ProgramFiles),
        Folder(Environment.SpecialFolder.ProgramFilesX86),
        Folder(Environment.SpecialFolder.CommonApplicationData),
        Folder(Environment.SpecialFolder.UserProfile),
        Path.GetDirectoryName(Folder(Environment.SpecialFolder.UserProfile)) ?? "",
    }.Where(p => p != "").ToArray();

    // Nothing inside these may be deleted either.
    static readonly string[] NoDeleteInside = new[]
    {
        Folder(Environment.SpecialFolder.Windows),
        Folder(Environment.SpecialFolder.ProgramFiles),
        Folder(Environment.SpecialFolder.ProgramFilesX86),
    }.Where(p => p != "").ToArray();

    public static string? Check(string path)
    {
        var full = PathUtil.Normalize(path);
        if (string.Equals(PathUtil.Normalize(Path.GetPathRoot(full)!), full, StringComparison.OrdinalIgnoreCase))
            return $"{full} is a whole drive.";
        foreach (var p in NoDeleteOrAncestor)
            if (PathUtil.IsSameOrInside(p, full)) return $"{full} is or contains the system folder {p}.";
        foreach (var p in NoDeleteInside)
            if (PathUtil.IsSameOrInside(full, p)) return $"{full} is inside the system folder {p}.";
        return null;
    }
}
