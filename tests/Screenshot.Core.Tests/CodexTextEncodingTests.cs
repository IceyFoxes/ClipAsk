using System.Text;
using System.Text.Json;
using Screenshot.Core.Providers;
using Screenshot.Core.Protocol;
using Xunit;

namespace Screenshot.Core.Tests;

public sealed class CodexTextEncodingTests
{
    [Fact]
    public void NativeFactoryUsesNoBomUtf8ForAllStreamsAndPreservesArguments()
    {
        var plan = CodexProcessPlan.Create(new CodexLaunchOptions("C:\\tools\\codex.exe", "C:\\state\\Screenshot"));
        var defaultInfo = plan.CreateStartInfo();
        var versionInfo = plan.CreateStartInfo(["--version"]);
        foreach (var info in new[] { defaultInfo, versionInfo })
        {
            Assert.Equal(65001, info.StandardInputEncoding!.CodePage);
            Assert.Equal(65001, info.StandardOutputEncoding!.CodePage);
            Assert.Equal(65001, info.StandardErrorEncoding!.CodePage);
            Assert.Empty(info.StandardInputEncoding!.GetPreamble());
            Assert.Equal("C:\\state\\Screenshot\\Workspace", info.WorkingDirectory);
            Assert.Equal("C:\\state\\Screenshot\\Codex", info.Environment["CODEX_HOME"]);
            Assert.DoesNotContain("OPENAI_API_KEY", info.Environment.Keys);
            Assert.DoesNotContain("CODEX_API_KEY", info.Environment.Keys);
            Assert.DoesNotContain("CODEX_ACCESS_TOKEN", info.Environment.Keys);
            Assert.DoesNotContain("OPENAI_BASE_URL", info.Environment.Keys);
            Assert.DoesNotContain("OPENAI_API_BASE", info.Environment.Keys);
        }
        Assert.Equal(plan.Arguments, defaultInfo.ArgumentList);
        Assert.Equal(["--version"], versionInfo.ArgumentList);
    }

    [Fact]
    public async Task ActualFactoryEncodingDecodesRawUtf8JsonWithoutMojibake()
    {
        var plan = CodexProcessPlan.Create(new CodexLaunchOptions("C:\\tools\\codex.exe", "C:\\state\\Screenshot"));
        var info = plan.CreateStartInfo();
        const string expected = "“handshake with subscription-only set…”. There isn’t enough visible context — café.";
        var payload = "{\"method\":\"item/agentMessage/delta\",\"params\":{\"delta\":\"" + expected + "\"}}\n";
        await using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(payload));
        using var reader = new StreamReader(stream, info.StandardOutputEncoding!, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        using var writer = new StringWriter();
        await using var connection = new JsonRpcConnection(reader, writer);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Notification += (_, parameters) => received.TrySetResult(parameters.GetProperty("delta").GetString()!);
        connection.Start();
        Assert.Equal(expected, await received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }
}
