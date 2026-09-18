using System;
using System.IO;
using Microsoft.Win32;

namespace ClipAsk.Desktop;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipAsk";
    private const string LegacyValueName = "ScreenAsk";

    public static void MigrateLegacyRegistration()
    {
        var executablePath = GetExecutablePath();
        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
            return;

        using var existing = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var legacyCommand = existing?.GetValue(LegacyValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        var expectedLegacyCommand = BuildCommand(Path.Combine(executableDirectory, "Screenshot.exe"));
        if (!string.Equals(legacyCommand, expectedLegacyCommand, StringComparison.OrdinalIgnoreCase))
            return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
        key.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var registered = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        return string.Equals(registered, BuildCommand(GetExecutablePath()), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
            key.SetValue(ValueName, BuildCommand(GetExecutablePath()), RegistryValueKind.String);
            return;
        }

        using var existing = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        existing?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    internal static string BuildCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"'))
            throw new ArgumentException("A valid executable path is required.", nameof(executablePath));
        return $"\"{Path.GetFullPath(executablePath)}\" --startup";
    }

    private static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("ClipAsk could not determine its executable path.");

        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var appHost = Path.ChangeExtension(typeof(App).Assembly.Location, ".exe");
            if (File.Exists(appHost))
                return appHost;
        }

        return processPath;
    }
}
