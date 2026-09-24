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

    /// <summary>Plain words: "8 sec", "3 min 45 sec", "22 hr 6 min".</summary>
    public static string Time(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{Math.Max(0, (int)t.TotalSeconds)} sec"
        : t.TotalHours < 1 ? $"{t.Minutes} min {t.Seconds} sec"
        : $"{(int)t.TotalHours} hr {t.Minutes} min";

    public static string Count(long n) => n.ToString("N0");
}
