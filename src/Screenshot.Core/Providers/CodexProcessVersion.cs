namespace Screenshot.Core.Providers;

public static class CodexProcessVersion
{
    public static string Expected => "codex-cli " + CodexPolicy.RuntimeVersion;

    public static bool IsCompatible(string output) =>
        string.Equals(output.Trim(), Expected, StringComparison.Ordinal);
}
