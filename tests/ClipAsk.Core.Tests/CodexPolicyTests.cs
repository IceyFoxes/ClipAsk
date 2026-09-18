using System.Text.Json;
using ClipAsk.Core.Providers;
using Xunit;

namespace ClipAsk.Core.Tests;

public sealed class CodexPolicyTests
{
    [Fact]
    public void AcceptsSubscriptionAccountsWithoutRequiringANewPaidPlan()
    {
        var account = CodexPolicy.ReadAccount(Json("""{"account":{"type":"chatgpt","planType":"free","email":null},"requiresOpenaiAuth":true}"""));
        Assert.True(account.IsConnected);
        Assert.Equal("free", account.Plan);
    }

    [Fact]
    public void DisconnectedAccountIsNotAnApiFallback()
    {
        Assert.False(CodexPolicy.ReadAccount(Json("""{"account":null,"requiresOpenaiAuth":true}""")).IsConnected);
        Assert.Throws<InvalidOperationException>(() => CodexPolicy.ReadAccount(Json("""{"account":{"type":"apiKey"},"requiresOpenaiAuth":true}""")));
        Assert.Throws<InvalidOperationException>(() => CodexPolicy.ReadAccount(Json("""{"account":{"type":"amazonBedrock"},"requiresOpenaiAuth":false}""")));
    }

    [Fact]
    public void ReadsDocumentedRateLimitWindowAndClampsPercent()
    {
        var limits = CodexPolicy.ReadRateLimits(Json("""{"rateLimits":{"primary":{"usedPercent":27.5,"windowDurationMins":300,"resetsAt":1784246400},"secondary":null}}"""));

        Assert.NotNull(limits?.Primary);
        Assert.Equal(27.5, limits.Primary.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), limits.Primary.Duration);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784246400), limits.Primary.ResetsAt);

        var clamped = CodexPolicy.ReadRateLimits(Json("""{"rateLimits":{"primary":{"usedPercent":140,"windowDurationMins":60,"resetsAt":1784246400}}}"""));
        Assert.Equal(100, clamped?.Primary?.UsedPercent);
        Assert.Null(CodexPolicy.ReadRateLimits(Json("""{"rateLimits":null}""")));
    }

    [Fact]
    public void UsesAdvertisedDefaultModelAndEffortInsteadOfGuessingAFastModel()
    {
        var model = CodexPolicy.SelectModel([DefaultModel()]);
        Assert.Equal("model-for-requests", model.Model);
        Assert.Equal("low", model.ReasoningEffort);
        Assert.Equal("Default vision model", model.DisplayName);
        Assert.Throws<InvalidOperationException>(() => CodexPolicy.SelectModel([]));
        Assert.Throws<InvalidOperationException>(() => CodexPolicy.SelectModel([DefaultModel(), DefaultModel()]));
    }

    [Fact]
    public void RefusesTextOnlyOrUnadvertisedReasoningSettings()
    {
        var textOnly = DefaultModel().GetRawText().Replace("[\"text\",\"image\"]", "[\"text\"]", StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => CodexPolicy.SelectModel([Json(textOnly)]));
        var badEffort = DefaultModel().GetRawText().Replace("\"reasoningEffort\":\"low\"", "\"reasoningEffort\":\"high\"", StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => CodexPolicy.SelectModel([Json(badEffort)]));
    }

    [Fact]
    public void OlderCatalogWithoutModalitiesKeepsDocumentedImageCompatibility()
    {
        var older = DefaultModel().GetRawText().Replace(",\"inputModalities\":[\"text\",\"image\"]", "", StringComparison.Ordinal);
        Assert.Equal("model-for-requests", CodexPolicy.SelectModel([Json(older)]).Model);
    }

    [Fact]
    public void RequestsAreEphemeralReadOnlyAndUseTheProvidedPngInline()
    {
        var model = CodexPolicy.SelectModel([DefaultModel()]);
        var thread = CodexPolicy.ThreadParameters("C:\\Screenshot\\Workspace", model);
        Assert.Equal("read-only", thread.GetProperty("sandbox").GetString());
        Assert.Equal("never", thread.GetProperty("approvalPolicy").GetString());
        Assert.True(thread.GetProperty("ephemeral").GetBoolean());
        Assert.Equal("default", thread.GetProperty("serviceTier").GetString());
        Assert.Equal(AnswerPrompt.Instructions, thread.GetProperty("baseInstructions").GetString());
        byte[] header = [137, 80, 78, 71, 13, 10, 26, 10];
        var turn = CodexPolicy.TurnParameters("thread-a", header, model);
        Assert.Equal(AnswerPrompt.Request, turn.GetProperty("input")[0].GetProperty("text").GetString());
        Assert.Equal("image", turn.GetProperty("input")[1].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", turn.GetProperty("input")[1].GetProperty("url").GetString());
        Assert.Equal("high", turn.GetProperty("input")[1].GetProperty("detail").GetString());
        Assert.Equal("readOnly", turn.GetProperty("sandboxPolicy").GetProperty("type").GetString());
        Assert.False(turn.GetProperty("sandboxPolicy").GetProperty("networkAccess").GetBoolean());
        Assert.Equal("default", turn.GetProperty("serviceTierForTurn").GetString());
        Assert.Equal("none", turn.GetProperty("summary").GetString());
        Assert.Throws<ArgumentException>(() => CodexPolicy.TurnParameters("thread-a", Array.Empty<byte>(), model));
    }

    [Fact]
    public void OptionalInstructionIsAddedWithoutReplacingTheSafeRequest()
    {
        var model = CodexPolicy.SelectModel([DefaultModel()]);
        byte[] header = [137, 80, 78, 71, 13, 10, 26, 10];

        var turn = CodexPolicy.TurnParameters("thread-a", header, model, "Explain each algebra step.");
        var text = turn.GetProperty("input")[0].GetProperty("text").GetString();

        Assert.StartsWith(AnswerPrompt.Request, text, StringComparison.Ordinal);
        Assert.Contains("Explain each algebra step.", text, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => AnswerPrompt.CreateRequest(new string('x', 2001)));
    }

    [Fact]
    public void AutomaticPromptInfersTheUsefulTaskWithoutAssumingAQuestion()
    {
        Assert.Contains("solve, explain, diagnose, summarize, interpret, identify, translate, or suggest a next step", AnswerPrompt.Instructions, StringComparison.Ordinal);
        Assert.Contains("If its purpose is ambiguous", AnswerPrompt.Instructions, StringComparison.Ordinal);
        Assert.Contains("do not omit useful detail merely for brevity", AnswerPrompt.Instructions, StringComparison.Ordinal);
        Assert.Contains("Let formatting serve comprehension", AnswerPrompt.Instructions, StringComparison.Ordinal);
        Assert.Contains("Return valid CommonMark-compatible Markdown", AnswerPrompt.Instructions, StringComparison.Ordinal);
        Assert.Contains("Do not turn a simple answer into an outline", AnswerPrompt.Instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("Be concise", AnswerPrompt.Instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("Answer the question in the attached screenshot", AnswerPrompt.Request, StringComparison.Ordinal);
        Assert.Equal("Analyze this selected screen region and provide the most useful response.", AnswerPrompt.CreateRequest(null));
    }

    [Theory]
    [InlineData("https://auth.openai.com/oauth/authorize", true)]
    [InlineData("https://chatgpt.com/", true)]
    [InlineData("http://auth.openai.com/", false)]
    [InlineData("https://auth.openai.com.attacker.invalid/", false)]
    [InlineData("https://user@chatgpt.com/", false)]
    [InlineData("https://chatgpt.com:8443/", false)]
    public void BrowserLoginIsRestrictedToProviderHttpsOrigins(string url, bool expected)
    {
        Assert.Equal(expected, CodexPolicy.IsAllowedLoginUri(new Uri(url)));
    }

    [Fact]
    public void StartupForcesAccountAccessAndDisablesSideEffectCapabilities()
    {
        Assert.Contains("forced_login_method=\"chatgpt\"", CodexPolicy.StartupOverrides);
        Assert.Contains("cli_auth_credentials_store=\"keyring\"", CodexPolicy.StartupOverrides);
        Assert.Contains("features.shell_tool=false", CodexPolicy.StartupOverrides);
        Assert.Contains("features.code_mode=false", CodexPolicy.StartupOverrides);
        Assert.Contains("features.browser_use=false", CodexPolicy.StartupOverrides);
        Assert.Contains("features.plugins=false", CodexPolicy.StartupOverrides);
        Assert.Contains("features.skip_host_skill_discovery=true", CodexPolicy.StartupOverrides);
        Assert.Contains("history.persistence=\"none\"", CodexPolicy.StartupOverrides);
        Assert.Contains("mcp_servers={}", CodexPolicy.StartupOverrides);
        Assert.Equal("chatgpt", CodexPolicy.LoginParameters().GetProperty("type").GetString());
    }

    private static JsonElement DefaultModel() => Json("""{"id":"catalog-id","model":"model-for-requests","displayName":"Default vision model","hidden":false,"isDefault":true,"defaultReasoningEffort":"low","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"inputModalities":["text","image"]}""");

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
