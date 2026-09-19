using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClipAsk.Desktop;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipAsk";
    private const string LegacyValueName = "ScreenAsk";
    private const int ErrorInsufficientBuffer = 122;

    public static bool UsesPackagedStartupTask => IsPackaged();

    public static void MigrateLegacyRegistration()
    {
        if (IsPackaged())
        {
            using var packagedKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            packagedKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            packagedKey?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            return;
        }

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
        if (IsPackaged())
            throw new InvalidOperationException("Packaged startup is managed by Windows Settings.");

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

    public static void OpenPackagedStartupSettings()
    {
        if (!IsPackaged())
            throw new InvalidOperationException("Windows Startup Apps settings are only used by the packaged edition.");
        Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
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

    private static bool IsPackaged()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        var length = 0;
        var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        return result == ErrorInsufficientBuffer;
    }

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);
}
