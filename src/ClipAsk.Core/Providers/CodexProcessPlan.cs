using System.Diagnostics;
using System.Text;

namespace ClipAsk.Core.Providers;

public sealed record CodexProcessPlan(
    string ExecutablePath,
    string StateDirectory,
    string CodexHome,
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
        foreach (var argument in arguments ?? Arguments)
            info.ArgumentList.Add(argument);
        return info;
    }
}
