using ClipAsk.Core.Providers;
using Xunit;

namespace ClipAsk.Core.Tests;

public sealed class CodexProcessPlanTests
{
    [Fact]
    public void PlanUsesIsolatedStateAndEveryFrozenOverride()
    {
        var plan = CodexProcessPlan.Create(new CodexLaunchOptions("C:\\tools\\codex.exe", "C:\\state\\Screenshot"));
        Assert.Equal("C:\\state\\Screenshot\\Codex", plan.CodexHome);
        Assert.Equal("C:\\state\\Screenshot\\Workspace", plan.Workspace);
        Assert.Equal("C:\\state\\Screenshot\\CodexSession", plan.SessionDatabases);
        Assert.Equal(plan.SessionDatabases, plan.CreateStartInfo().Environment["CODEX_SQLITE_HOME"]);
        Assert.Equal("app-server", plan.Arguments[^3]);
        Assert.Equal("--listen", plan.Arguments[^2]);
        Assert.Equal("stdio://", plan.Arguments[^1]);
        for (var index = 0; index < CodexPolicy.StartupOverrides.Count; index++)
        {
            Assert.Equal("-c", plan.Arguments[index * 2]);
            Assert.Equal(CodexPolicy.StartupOverrides[index], plan.Arguments[index * 2 + 1]);
        }
        Assert.Contains("OPENAI_API_KEY", plan.RemovedEnvironmentVariables);
        Assert.Contains("CODEX_API_KEY", plan.RemovedEnvironmentVariables);
        Assert.Contains("CODEX_ACCESS_TOKEN", plan.RemovedEnvironmentVariables);
        Assert.Contains("OPENAI_BASE_URL", plan.RemovedEnvironmentVariables);
        Assert.Contains("OPENAI_API_BASE", plan.RemovedEnvironmentVariables);
    }

    [Fact]
    public void DeletesSessionDatabasesAndLegacyLogsButKeepsSignInState()
    {
        var state = Path.Combine(Path.GetTempPath(), "clipask-plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var plan = CodexProcessPlan.Create(new CodexLaunchOptions("C:\\tools\\codex.exe", state));
            Directory.CreateDirectory(Path.Combine(plan.CodexHome, "secrets"));
            Directory.CreateDirectory(plan.SessionDatabases);
            File.WriteAllText(Path.Combine(plan.SessionDatabases, "logs_2.sqlite"), "capture");
            File.WriteAllText(Path.Combine(plan.CodexHome, "logs_2.sqlite"), "capture");
            File.WriteAllText(Path.Combine(plan.CodexHome, "logs_2.sqlite-wal"), "capture");
            File.WriteAllText(Path.Combine(plan.CodexHome, "models_cache.json"), "{}");
            File.WriteAllText(Path.Combine(plan.CodexHome, "secrets", "codex_auth.age"), "sign-in");

            plan.DeleteSessionDatabases();

            Assert.False(Directory.Exists(plan.SessionDatabases));
            Assert.Empty(Directory.EnumerateFiles(plan.CodexHome, "*.sqlite*"));
            Assert.True(File.Exists(Path.Combine(plan.CodexHome, "models_cache.json")));
            Assert.True(File.Exists(Path.Combine(plan.CodexHome, "secrets", "codex_auth.age")));
            plan.DeleteSessionDatabases();
        }
        finally
        {
            if (Directory.Exists(state))
                Directory.Delete(state, true);
        }
    }

    [Fact]
    public void RejectsUncRuntimeStateOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var exception = Assert.Throws<InvalidOperationException>(() => CodexProcessPlan.Create(new CodexLaunchOptions("C:\\tools\\codex.exe", "\\\\wsl.localhost\\Ubuntu\\home\\user\\project\\.devin\\state")));
        Assert.Equal("Codex runtime state must be on a local Windows drive. Unset CLIPASK_STATE_DIR to use Windows app data.", exception.Message);
    }
}
