using System.Text.Json;

namespace ClipAsk.Core.Providers;

public sealed record CodexModelSelection(
    string Model,
    string ReasoningEffort,
    string DisplayName,
    IReadOnlyList<string>? SupportedReasoningEfforts = null)
{
    public IReadOnlyList<string> ReasoningEfforts => SupportedReasoningEfforts ?? [ReasoningEffort];
}

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
            throw new InvalidOperationException("ClipAsk requires ChatGPT account sign-in. API-key and alternative-provider authentication are not supported.");

        return new(true, account.GetProperty("planType").GetString());
    }

    public static AccountRateLimits? ReadRateLimits(JsonElement response)
    {
        if (!response.TryGetProperty("rateLimits", out var limits) || limits.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (limits.ValueKind != JsonValueKind.Object)
            return null;

        var primary = limits.TryGetProperty("primary", out var primaryElement) ? ReadRateLimitWindow(primaryElement) : null;
        var secondary = limits.TryGetProperty("secondary", out var secondaryElement) ? ReadRateLimitWindow(secondaryElement) : null;
        return primary is null && secondary is null ? null : new(primary, secondary);
    }

    private static AccountRateLimitWindow? ReadRateLimitWindow(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("usedPercent", out var usedElement) || !usedElement.TryGetDouble(out var usedPercent) ||
            !value.TryGetProperty("windowDurationMins", out var durationElement) || !durationElement.TryGetDouble(out var durationMinutes) ||
            !value.TryGetProperty("resetsAt", out var resetElement) || !resetElement.TryGetInt64(out var resetsAt) ||
            !double.IsFinite(usedPercent) || !double.IsFinite(durationMinutes) || durationMinutes <= 0)
            return null;

        try
        {
            return new(Math.Clamp(usedPercent, 0, 100), TimeSpan.FromMinutes(durationMinutes), DateTimeOffset.FromUnixTimeSeconds(resetsAt));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
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
                return new(
                    PreferredModel,
                    PreferredReasoningEffort,
                    string.IsNullOrWhiteSpace(displayName) ? PreferredModel : displayName,
                    ReadSupportedEfforts(candidate));
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

        return new(model, effort, name ?? model, ReadSupportedEfforts(selected));
    }

    public static IReadOnlyList<CodexModelSelection> ListModels(IEnumerable<JsonElement> models)
    {
        return models
            .Where(IsEligibleModel)
            .GroupBy(value => value.GetProperty("model").GetString(), StringComparer.Ordinal)
            .Where(group => group.Key is not null && group.Count() == 1)
            .Select(group => CreateSelection(group.Single()))
            .OrderBy(selection => selection.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static CodexModelSelection SelectModel(IEnumerable<JsonElement> models, string requestedModel)
        => SelectModel(models, requestedModel, null);

    public static CodexModelSelection SelectModel(IEnumerable<JsonElement> models, string? requestedModel, string? requestedEffort)
    {
        var catalog = models.ToArray();
        CodexModelSelection selected;
        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            selected = SelectModel(catalog);
        }
        else
        {
            var matches = ListModels(catalog).Where(model => model.Model == requestedModel).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("The selected model is not available for screenshots. No model request was sent.");
            selected = matches[0];
        }

        if (string.IsNullOrWhiteSpace(requestedEffort))
            return selected;
        if (!selected.ReasoningEfforts.Contains(requestedEffort, StringComparer.Ordinal))
            throw new InvalidOperationException("The selected reasoning effort is not available for this model. No model request was sent.");
        return selected with { ReasoningEffort = requestedEffort };
    }

    private static bool IsEligibleModel(JsonElement model)
    {
        var hasAvailabilityWarning = model.TryGetProperty("availabilityNux", out var availability) && availability.ValueKind != JsonValueKind.Null;
        if (hasAvailabilityWarning || model.GetProperty("hidden").GetBoolean() || !SupportsImages(model))
            return false;
        var name = model.GetProperty("model").GetString();
        var effort = name == PreferredModel && SupportsEffort(model, PreferredReasoningEffort)
            ? PreferredReasoningEffort
            : model.GetProperty("defaultReasoningEffort").GetString();
        return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(effort) && SupportsEffort(model, effort);
    }

    private static CodexModelSelection CreateSelection(JsonElement model)
    {
        var name = model.GetProperty("model").GetString()!;
        var effort = name == PreferredModel && SupportsEffort(model, PreferredReasoningEffort)
            ? PreferredReasoningEffort
            : model.GetProperty("defaultReasoningEffort").GetString()!;
        var displayName = model.GetProperty("displayName").GetString();
        return new(name, effort, string.IsNullOrWhiteSpace(displayName) ? name : displayName, ReadSupportedEfforts(model));
    }

    private static IReadOnlyList<string> ReadSupportedEfforts(JsonElement model) =>
        model.GetProperty("supportedReasoningEfforts")
            .EnumerateArray()
            .Select(option => option.GetProperty("reasoningEffort").GetString())
            .Where(effort => !string.IsNullOrWhiteSpace(effort))
            .Select(effort => effort!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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

    public static JsonElement TurnParameters(string threadId, ReadOnlyMemory<byte> png, CodexModelSelection model, string? instruction = null)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length > MaximumImageBytes || !png.Span.StartsWith(signature))
            throw new ArgumentException("Select a smaller region and supply a PNG image no larger than 20 MiB.", nameof(png));

        return JsonSerializer.SerializeToElement(new
        {
            threadId,
            input = new object[]
            {
                new { type = "text", text = AnswerPrompt.CreateRequest(instruction) },
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
