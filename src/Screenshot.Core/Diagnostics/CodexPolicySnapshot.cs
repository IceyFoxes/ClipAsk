using System.Text.Json;
using Screenshot.Core.Providers;

namespace Screenshot.Core.Diagnostics;

public static class CodexPolicySnapshot
{
    private static readonly string[] Fields =
    [
        "forced_login_method", "cli_auth_credentials_store", "model_provider",
        "sandbox_mode", "approval_policy", "web_search", "project_doc_max_bytes",
        "history", "mcp_servers", "agents", "features", "analytics", "feedback"
    ];

    public static JsonElement Create(SubscriptionAccount account, JsonElement configResponse)
    {
        if (!configResponse.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Codex config/read did not return its expected config object.");

        foreach (var setting in CodexPolicy.StartupOverrides)
        {
            var separator = setting.IndexOf('=');
            var path = setting[..separator];
            var actual = config;
            foreach (var segment in path.Split('.'))
            {
                if (actual.ValueKind != JsonValueKind.Object || !actual.TryGetProperty(segment, out actual))
                    throw new InvalidDataException($"Codex did not expose the configured policy field: {path}.");
            }

            using var expected = JsonDocument.Parse(setting[(separator + 1)..]);
            if (!JsonElement.DeepEquals(actual, expected.RootElement))
                throw new InvalidDataException($"Codex did not apply the required policy field: {path}.");
        }

        var selected = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            if (!config.TryGetProperty(field, out var value))
                throw new InvalidDataException($"Codex did not expose the configured policy field: {field}.");
            selected.Add(field, value.Clone());
        }

        return JsonSerializer.SerializeToElement(new
        {
            accountConnected = account.IsConnected,
            plan = account.Plan,
            config = selected
        });
    }
}
