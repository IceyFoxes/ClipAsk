using System;
using System.IO;

namespace ClipAsk.Desktop;

internal static class FirstRunStateStore
{
    private const string FileName = "first-run-v1.seen";

    public static bool ShouldShow(string? stateDirectory = null)
    {
        try
        {
            return !File.Exists(Path.Combine(stateDirectory ?? DefaultStateDirectory(), FileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static void MarkSeen(string? stateDirectory = null)
    {
        try
        {
            var state = stateDirectory ?? DefaultStateDirectory();
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, FileName), "seen");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }

    private static string DefaultStateDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipAsk");
}
