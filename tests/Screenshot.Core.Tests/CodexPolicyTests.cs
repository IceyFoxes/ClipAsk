using System.Text.Json;
using Screenshot.Core.Providers;
using Xunit;

namespace Screenshot.Core.Tests;

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
