using System.Globalization;

namespace LsrXmlEditor.Infrastructure;

public static class Format
{
    private const long BytesPerKilobyte = 1L << 10;
    private const long BytesPerMegabyte = 1L << 20;

    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");

    public static string Size(long bytes) => bytes switch
    {
        >= BytesPerMegabyte => ((double)bytes / BytesPerMegabyte).ToString("0.0", Culture) + " MB",
        >= BytesPerKilobyte => ((double)bytes / BytesPerKilobyte).ToString("0", Culture) + " KB",
        _ => bytes + " B"
    };

    public static string Number(int value) => value.ToString("N0", Culture);

    public static string Time(DateTime value) => value.ToString("yyyy-MM-dd HH:mm", Culture);

    public static string TimeWithSeconds(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss", Culture);
}
