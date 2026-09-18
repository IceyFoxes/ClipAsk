using System;
using System.IO;

namespace ClipAsk.Desktop;

internal static class SaveLocationStore
{
    private const string FileName = "last-save-folder.txt";

    public static string? Read(string? stateDirectory = null)
    {
        try
        {
            var path = Path.Combine(stateDirectory ?? DefaultStateDirectory(), FileName);
            if (stateDirectory is null && !File.Exists(path))
                path = Path.Combine(LegacyStateDirectory(), FileName);
            if (!File.Exists(path))
                return null;
            var directory = File.ReadAllText(path).Trim();
            return Path.IsPathFullyQualified(directory) && Directory.Exists(directory) ? directory : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Remember(string? directory, string? stateDirectory = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
                return;
            var state = stateDirectory ?? DefaultStateDirectory();
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, FileName), directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }

    private static string DefaultStateDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipAsk");

    private static string LegacyStateDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Screenshot");
}
