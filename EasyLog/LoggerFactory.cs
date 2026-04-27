namespace EasyLog;

public enum LogFormat { Json, Xml }

public static class LoggerFactory
{
    public static ILogger Create(LogFormat format, Func<string> logDirectoryProvider)
    {
        return format switch
        {
            LogFormat.Xml => new XmlAppendLogger(() => BuildPath(logDirectoryProvider(), "xml")),
            _             => new JsonLineLogger(() => BuildPath(logDirectoryProvider(), "json")),
        };
    }

    public static LogFormat Parse(string? raw) =>
        string.Equals(raw, "xml", StringComparison.OrdinalIgnoreCase) ? LogFormat.Xml : LogFormat.Json;

    private static string BuildPath(string directory, string extension) =>
        Path.Combine(directory, $"{DateTime.Now:yyyy-MM-dd}.{extension}");
}
