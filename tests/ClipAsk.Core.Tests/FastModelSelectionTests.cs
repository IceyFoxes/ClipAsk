using System.Text.Json;
using ClipAsk.Core.Providers;
using Xunit;

namespace ClipAsk.Core.Tests;

public sealed class FastModelSelectionTests
{
    [Fact]
    public void PrefersLunaLowWhenTheAccountAdvertisesIt()
    {
        var selected = CodexPolicy.SelectModel([
            Model("gpt-5.6-sol", isDefault: true),
            Model("gpt-6-luna"),
            Model("gpt-5.6-terra")
        ]);
        Assert.Equal("gpt-6-luna", selected.Model);
        Assert.Equal("low", selected.ReasoningEffort);
    }

    [Fact]
    public void KeepsTheSupportedCatalogDefaultWhenLunaIsAbsent()
    {
        var selected = CodexPolicy.SelectModel([
            Model("gpt-5.6-sol", isDefault: true),
            Model("gpt-5.6-terra")
        ]);
        Assert.Equal("gpt-5.6-sol", selected.Model);
        Assert.Equal("medium", selected.ReasoningEffort);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("text-only")]
    [InlineData("no-low-effort")]
    [InlineData("availability-warning")]
    public void DoesNotForceAnIneligiblePreferredModel(string condition)
    {
        var preferred = Model("gpt-6-luna",
            hidden: condition == "hidden",
            image: condition != "text-only",
            low: condition != "no-low-effort",
            warning: condition == "availability-warning");
        var selected = CodexPolicy.SelectModel([Model("gpt-5.6-sol", isDefault: true), preferred]);
        Assert.Equal("gpt-5.6-sol", selected.Model);
        Assert.Equal("medium", selected.ReasoningEffort);
    }

    [Fact]
    public void AmbiguousPreferredEntriesFallBackRatherThanGuess()
    {
        var preferred = Model("gpt-6-luna");
        var selected = CodexPolicy.SelectModel([Model("gpt-5.6-sol", isDefault: true), preferred, preferred]);
        Assert.Equal("gpt-5.6-sol", selected.Model);
    }

    [Fact]
    public void PreferredSelectionDoesNotRequireAnUnrelatedDefault()
    {
        var selected = CodexPolicy.SelectModel([Model("gpt-6-luna")]);
        Assert.Equal("gpt-6-luna", selected.Model);
        Assert.Equal("low", selected.ReasoningEffort);
    }

    [Fact]
    public void SelectedSettingsReachTheTurnWithoutChangingBillingTier()
    {
        var selected = CodexPolicy.SelectModel([Model("gpt-5.6-sol", isDefault: true), Model("gpt-6-luna")]);
        byte[] pngHeader = [137, 80, 78, 71, 13, 10, 26, 10];
        var parameters = CodexPolicy.TurnParameters("thread-test", pngHeader, selected);
        Assert.Equal("gpt-6-luna", parameters.GetProperty("model").GetString());
        Assert.Equal("low", parameters.GetProperty("effort").GetString());
        Assert.Equal("default", parameters.GetProperty("serviceTierForTurn").GetString());
        Assert.Equal("high", parameters.GetProperty("input")[1].GetProperty("detail").GetString());
    }

    [Fact]
    public void FastModeUsesPriorityServiceTierForThreadAndTurn()
    {
        var selected = CodexPolicy.SelectModel([Model("gpt-6-luna")]);
        byte[] pngHeader = [137, 80, 78, 71, 13, 10, 26, 10];

        var thread = CodexPolicy.ThreadParameters("C:\\ClipAsk", selected, fastMode: true);
        var turn = CodexPolicy.TurnParameters("thread-test", pngHeader, selected, fastMode: true);

        Assert.Equal("priority", thread.GetProperty("serviceTier").GetString());
        Assert.Equal("priority", turn.GetProperty("serviceTierForTurn").GetString());
    }

    [Fact]
    public void ListsOnlyEligibleScreenshotModelsAndAllowsExplicitSelection()
    {
        var catalog = new[]
        {
            Model("gpt-5.6-sol", isDefault: true),
            Model("gpt-5.6-terra"),
            Model("text-only", image: false),
            Model("hidden", hidden: true)
        };

        var choices = CodexPolicy.ListModels(catalog);

        Assert.Equal(2, choices.Count);
        Assert.Equal("gpt-5.6-sol", CodexPolicy.SelectModel(catalog, "gpt-5.6-sol").Model);
        Assert.Throws<InvalidOperationException>(() => CodexPolicy.SelectModel(catalog, "text-only"));
    }

    [Fact]
    public void ModelAndReasoningEffortCanBeSelectedIndependently()
    {
        var catalog = new[]
        {
            Model("gpt-5.6-sol", isDefault: true),
            Model("gpt-6-luna")
        };

        var selected = CodexPolicy.SelectModel(catalog, "gpt-6-luna", "low");

        Assert.Equal("gpt-6-luna", selected.Model);
        Assert.Equal("low", selected.ReasoningEffort);
        Assert.Contains("medium", selected.ReasoningEfforts);
        Assert.Throws<InvalidOperationException>(() => CodexPolicy.SelectModel(catalog, "gpt-6-luna", "high"));
    }

    private static JsonElement Model(string model, bool isDefault = false, bool hidden = false, bool image = true, bool low = true, bool warning = false) =>
        JsonSerializer.SerializeToElement(new
        {
            id = model,
            model,
            displayName = model,
            hidden,
            isDefault,
            defaultReasoningEffort = "medium",
            supportedReasoningEfforts = low
                ? new[] { new { reasoningEffort = "low" }, new { reasoningEffort = "medium" } }
                : new[] { new { reasoningEffort = "medium" } },
            inputModalities = image ? new[] { "text", "image" } : new[] { "text" },
            availabilityNux = warning ? new { message = "Not available for this account" } : null
        });
}
