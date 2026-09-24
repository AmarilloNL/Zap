namespace Zap.Engine.Tests;

public class ProtectedPathsTests
{
    static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Fact] public void Drive_root_is_blocked() => Assert.NotNull(ProtectedPaths.Check(@"C:\"));
    [Fact] public void Windows_is_blocked() => Assert.NotNull(ProtectedPaths.Check(Windows));
    [Fact] public void Inside_windows_is_blocked() => Assert.NotNull(ProtectedPaths.Check(Path.Combine(Windows, "System32")));
    [Fact] public void Inside_program_files_is_blocked() =>
        Assert.NotNull(ProtectedPaths.Check(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Foo")));
    [Fact] public void Users_folder_is_blocked() => Assert.NotNull(ProtectedPaths.Check(Path.GetDirectoryName(Profile)!));
    [Fact] public void Profile_is_blocked() => Assert.NotNull(ProtectedPaths.Check(Profile + @"\"));
    [Fact] public void Folder_inside_profile_is_allowed() => Assert.Null(ProtectedPaths.Check(Path.Combine(Profile, @"Downloads\junk")));
    [Fact] public void Temp_is_allowed() => Assert.Null(ProtectedPaths.Check(Path.Combine(Path.GetTempPath(), "x")));
}
