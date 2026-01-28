using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;

namespace immensive;

public class CustomLogger
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    public class LoggerConfig
    {
        [JsonPropertyName("level")]
        public LogLevel Level { get; set; } = LogLevel.Info;

        [JsonPropertyName("stdOut")]
        public bool StdOut { get; set; } = true; 
    }

    public LoggerConfig Config { get; internal set; } = new ();

    private static void LogStdOut(object? obj, LogLevel level, string message, string user = "")
    {
        Console.WriteLine($"[{DateTime.Now}] [{level}] {message}" + (string.IsNullOrEmpty(user) ? "" : $" (User: {user})"));
    }

    public static void Log(object? obj, LogLevel level, string message, string user = "")
    {
        var customLogger = Global.Logger;
        if (customLogger == null)
        {
            LogStdOut(obj, level, message, user);
            return;
        }
        if (level < customLogger.Config.Level)
            return;
        if (customLogger.Config.StdOut)
            LogStdOut(obj, level, message, user);
    }
}