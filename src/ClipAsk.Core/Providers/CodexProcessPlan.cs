using System.Diagnostics;
using System.Text;

namespace ClipAsk.Core.Providers;

public sealed record CodexProcessPlan(
    string ExecutablePath,
    string StateDirectory,
    string CodexHome,
    string SessionDatabases,
    string Workspace,
    IReadOnlyList<string> Arguments,
    IReadOnlySet<string> RemovedEnvironmentVariables)
{
    public static CodexProcessPlan Create(CodexLaunchOptions options)
    {
        var state = Path.GetFullPath(options.StateDirectory);
        if (OperatingSystem.IsWindows() && Uri.TryCreate(state, UriKind.Absolute, out var uri) && uri.IsUnc)
            throw new InvalidOperationException("Codex runtime state must be on a local Windows drive. Unset CLIPASK_STATE_DIR to use Windows app data.");
        var codexHome = Path.Combine(state, "Codex");
        var sessionDatabases = Path.Combine(state, "CodexSession");
        var workspace = Path.Combine(state, "Workspace");
        var arguments = new List<string>(CodexPolicy.StartupOverrides.Count * 2 + 3);
        foreach (var value in CodexPolicy.StartupOverrides)
        {
            arguments.Add("-c");
            arguments.Add(value);
        }
        arguments.Add("app-server");
        arguments.Add("--listen");
        arguments.Add("stdio://");
        return new(
            Path.GetFullPath(options.ExecutablePath),
            state,
            codexHome,
            sessionDatabases,
            workspace,
            arguments,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "OPENAI_API_KEY",
                "CODEX_API_KEY",
                "CODEX_ACCESS_TOKEN",
                "OPENAI_BASE_URL",
                "OPENAI_API_BASE"
            });
    }

    public ProcessStartInfo CreateStartInfo(IEnumerable<string>? arguments = null)
    {
        var utf8 = new UTF8Encoding(false, true);
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            WorkingDirectory = Workspace
        };
        foreach (var key in RemovedEnvironmentVariables)
            info.Environment.Remove(key);
        info.Environment["CODEX_HOME"] = CodexHome;
        info.Environment["CODEX_SQLITE_HOME"] = SessionDatabases;
        foreach (var argument in arguments ?? Arguments)
            info.ArgumentList.Add(argument);
        return info;
    }

    // Codex's SQLite log records every request it sends, including the
    // screenshot. ClipAsk keeps no history, so these databases are deleted
    // whenever the Codex process starts or stops. Databases that older builds
    // left in the Codex home are removed too; sign-in and the model cache are
    // stored outside SQLite and are kept.
    public bool DeleteSessionDatabases()
    {
        var deleted = true;
        try
        {
            if (Directory.Exists(SessionDatabases))
                Directory.Delete(SessionDatabases, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            deleted = false;
        }
        if (!Directory.Exists(CodexHome))
            return deleted;
        foreach (var pattern in new[] { "*.sqlite", "*.sqlite-shm", "*.sqlite-wal" })
        {
            foreach (var file in Directory.EnumerateFiles(CodexHome, pattern))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    deleted = false;
                }
            }
        }
        return deleted;
    }

    // Windows can keep the database files locked briefly after Codex exits.
    public void DeleteSessionDatabasesAfterExit()
    {
        for (var attempt = 0; attempt < 10 && !DeleteSessionDatabases(); attempt++)
            Thread.Sleep(100);
    }
}
