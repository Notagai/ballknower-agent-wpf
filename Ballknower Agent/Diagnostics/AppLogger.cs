using System;
using System.IO;
using System.Text;

namespace Ballknower.Diagnostics;

public static class AppLogger
{
    private static readonly object Sync = new();

    public static string LogDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Ballknower",
            "Logs");

    public static void Error(
        string context,
        Exception exception)
    {
        Write(
            "ERROR",
            context,
            exception);
    }

    public static void Warning(
        string context,
        string message)
    {
        Write(
            "WARNING",
            context + ": " + message,
            null);
    }

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    private static void Write(
        string level,
        string message,
        Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(
                    LogDirectory);

                var path = Path.Combine(
                    LogDirectory,
                    $"ballknower-{DateTime.Now:yyyy-MM-dd}.log");

                var entry = new StringBuilder();

                entry.AppendLine(
                    $"[{DateTimeOffset.Now:O}] [{level}]");

                entry.AppendLine(message);

                if (exception is not null)
                {
                    entry.AppendLine(
                        exception.ToString());
                }

                entry.AppendLine(
                    new string('-', 60));

                File.AppendAllText(
                    path,
                    entry.ToString());
            }
        }
        catch
        {
            // Logging must not cause another exception.
        }
    }
}