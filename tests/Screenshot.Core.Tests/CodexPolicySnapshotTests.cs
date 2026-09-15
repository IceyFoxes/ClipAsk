using System.Text.Json;
using System.Text.Json.Nodes;
using Screenshot.Core.Diagnostics;
using Screenshot.Core.Providers;
using Xunit;

namespace Screenshot.Core.Tests;

public sealed class CodexPolicySnapshotTests
{
    [Fact]
    public void CapturesOnlyTheEffectiveConfigurationAndChecksEveryRequiredSetting()
    {
        var input = new JsonObject { ["config"] = Configuration(), ["origins"] = new JsonObject() };
        var snapshot = CodexPolicySnapshot.Create(new(false, null), JsonSerializer.SerializeToElement(input));
        Assert.False(snapshot.GetProperty("accountConnected").GetBoolean());
        Assert.Equal("chatgpt", snapshot.GetProperty("config").GetProperty("forced_login_method").GetString());
        Assert.False(snapshot.TryGetProperty("origins", out _));
    }

    [Fact]
    public void DoesNotSearchOtherObjectsForPlausiblePolicyValues()
    {
        var config = Configuration();
        config.Remove("forced_login_method");
        var input = new JsonObject
        {
            ["config"] = config,
            ["origins"] = new JsonObject { ["forced_login_method"] = "chatgpt" }
        };
        Assert.Throws<InvalidDataException>(() => CodexPolicySnapshot.Create(new(false, null), JsonSerializer.SerializeToElement(input)));
    }

    [Fact]
    public void RejectsAnIneffectiveSafetySetting()
    {
        var config = Configuration();
        config["sandbox_mode"] = "danger-full-access";
        var input = new JsonObject { ["config"] = config };
        Assert.Throws<InvalidDataException>(() => CodexPolicySnapshot.Create(new(false, null), JsonSerializer.SerializeToElement(input)));
    }

    private static JsonObject Configuration()
    {
        var config = new JsonObject();
        foreach (var setting in CodexPolicy.StartupOverrides)
        {
            var separator = setting.IndexOf('=');
            var path = setting[..separator].Split('.');
            var node = config;
            foreach (var segment in path[..^1])
            {
                if (node[segment] is not JsonObject child)
                {
                    child = new JsonObject();
                    node[segment] = child;
                }
                node = child;
            }
            node[path[^1]] = JsonNode.Parse(setting[(separator + 1)..]);
        }
        return config;
    }
}
