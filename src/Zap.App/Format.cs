namespace Zap.App;

static class Format
{
    public static string Bytes(double b)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int u = 0;
        while (b >= 1024 && u < units.Length - 1) { b /= 1024; u++; }
        return u == 0 ? $"{b:0} B" : $"{b:0.0} {units[u]}";
    }

    public static string Time(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    public static string Count(long n) => n.ToString("N0");
}
