using System.Text.Json;

namespace Screenshot.Core.Providers;

public sealed record CodexModelSelection(string Model, string ReasoningEffort, string DisplayName);

public sealed record SubscriptionAccount(bool IsConnected, string? Plan);

public static class CodexPolicy
{
    public const string RuntimeVersion = "0.151.0";
    public const string PreferredModel = "gpt-5.6-terra";
    public const string PreferredReasoningEffort = "low";
    public const int MaximumImageBytes = 20 * 1024 * 1024;

    public static IReadOnlyList<string> StartupOverrides { get; } = Array.AsReadOnly<string>(
    [
        "forced_login_method=\"chatgpt\"",
        "cli_auth_credentials_store=\"keyring\"",
        "model_provider=\"openai\"",
        "sandbox_mode=\"read-only\"",
        "approval_policy=\"never\"",
        "web_search=\"disabled\"",
        "project_doc_max_bytes=0",
        "history.persistence=\"none\"",
        "mcp_servers={}",
        "agents.enabled=false",
        "analytics.enabled=false",
        "feedback.enabled=false",
        "features.shell_tool=false",
        "features.unified_exec=false",
        "features.apply_patch_freeform=false",
        "features.code_mode=false",
        "features.js_repl=false",
        "features.multi_agent=false",
        "features.multi_agent_v2=false",
        "features.apps=false",
        "features.plugins=false",
        "features.connectors=false",
        "features.browser_use=false",
        "features.in_app_browser=false",
        "features.computer_use=false",
        "features.image_generation=false",
        "features.view_image=false",
        "features.memory_tool=false",
        "features.memories=false",
        "features.hooks=false",
        "features.codex_hooks=false",
        "features.plugin_hooks=false",
        "features.request_permissions_tool=false",
        "features.request_permissions=false",
        "features.skip_host_skill_discovery=true",
        "features.skill_search=false",
        "features.search_tool=false",
        "features.tool_search=false",
        "features.tool_suggest=false",
        "features.remote_control=false",
        "features.in_app_local_automation=false",
        "features.shell_snapshot=false",
        "features.external_agent_memory_import=false",
        "features.external_migration=false",
        "features.recommended_plugins=false",
        "features.remote_plugin=false",
        "features.workspace_dependencies=false",
        "features.goals=false",
        "features.enable_fanout=false",
        "features.fast_mode=false"
    ]);

    public static JsonElement LoginParameters() => JsonSerializer.SerializeToElement(new
    {
        type = "chatgpt",
        useHostedLoginSuccessPage = true,
        appBrand = "chatgpt"
    });

    public static bool IsAllowedLoginUri(Uri uri) =>
        uri.IsAbsoluteUri &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("openai.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase));

    public static SubscriptionAccount ReadAccount(JsonElement response)
    {
        if (!response.TryGetProperty("account", out var account) || account.ValueKind == JsonValueKind.Null)
            return new(false, null);

        if (account.GetProperty("type").GetString() != "chatgpt" ||
            !response.GetProperty("requiresOpenaiAuth").GetBoolean())
            throw new InvalidOperationException("Screenshot requires ChatGPT account sign-in. API-key and alternative-provider authentication are not supported.");

        return new(true, account.GetProperty("planType").GetString());
    }

    public static CodexModelSelection SelectModel(IEnumerable<JsonElement> models)
    {
        var catalog = models.ToArray();
        var preferred = catalog.Where(model =>
            model.GetProperty("model").GetString() == PreferredModel &&
            !model.GetProperty("hidden").GetBoolean()).ToArray();
        if (preferred.Length == 1)
        {
            var candidate = preferred[0];
            var hasAvailabilityWarning = candidate.TryGetProperty("availabilityNux", out var availability) &&
                availability.ValueKind != JsonValueKind.Null;
            if (!hasAvailabilityWarning && SupportsImages(candidate) && SupportsEffort(candidate, PreferredReasoningEffort))
            {
                var displayName = candidate.GetProperty("displayName").GetString();
                return new(PreferredModel, PreferredReasoningEffort, string.IsNullOrWhiteSpace(displayName) ? PreferredModel : displayName);
            }
        }

        var defaults = catalog.Where(model =>
            model.GetProperty("isDefault").GetBoolean() &&
            !model.GetProperty("hidden").GetBoolean()).ToArray();
        if (defaults.Length != 1)
            throw new InvalidOperationException("Codex did not advertise a unique default model. No model request was sent.");

        var selected = defaults[0];
        if (!SupportsImages(selected))
            throw new InvalidOperationException("The default model does not support screenshots. No model request was sent.");

        var model = selected.GetProperty("model").GetString();
        var effort = selected.GetProperty("defaultReasoningEffort").GetString();
        var name = selected.GetProperty("displayName").GetString();
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(effort) || !SupportsEffort(selected, effort))
            throw new InvalidOperationException("Codex returned an unsupported default model configuration. No model request was sent.");

        return new(model, effort, name ?? model);
    }

    private static bool SupportsImages(JsonElement model) =>
        !model.TryGetProperty("inputModalities", out var modalities) ||
        (modalities.ValueKind == JsonValueKind.Array && modalities.EnumerateArray().Any(value => value.GetString() == "image"));

    private static bool SupportsEffort(JsonElement model, string effort) =>
        model.TryGetProperty("supportedReasoningEfforts", out var options) &&
        options.ValueKind == JsonValueKind.Array &&
        options.EnumerateArray().Any(option => option.GetProperty("reasoningEffort").GetString() == effort);

    public static JsonElement ThreadParameters(string workspace, CodexModelSelection model) =>
        JsonSerializer.SerializeToElement(new
        {
            cwd = workspace,
            model = model.Model,
            modelProvider = "openai",
            approvalPolicy = "never",
            sandbox = "read-only",
            baseInstructions = AnswerPrompt.Instructions,
            personality = "none",
            ephemeral = true,
            serviceTier = "default"
        });

    public static JsonElement TurnParameters(string threadId, ReadOnlyMemory<byte> png, CodexModelSelection model)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length > MaximumImageBytes || !png.Span.StartsWith(signature))
            throw new ArgumentException("Select a smaller region and supply a PNG image no larger than 20 MiB.", nameof(png));

        return JsonSerializer.SerializeToElement(new
        {
            threadId,
            input = new object[]
            {
                new { type = "text", text = AnswerPrompt.Request },
                new { type = "image", url = "data:image/png;base64," + Convert.ToBase64String(png.Span), detail = "high" }
            },
            model = model.Model,
            effort = model.ReasoningEffort,
            summary = "none",
            approvalPolicy = "never",
            sandboxPolicy = new { type = "readOnly", networkAccess = false },
            serviceTierForTurn = "default"
        });
    }
}
