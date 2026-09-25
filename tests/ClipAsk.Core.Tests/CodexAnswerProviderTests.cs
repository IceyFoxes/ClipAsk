using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ClipAsk.Core.Providers;
using ClipAsk.Core.Protocol;
using Xunit;

namespace ClipAsk.Core.Tests;

public sealed class CodexAnswerProviderTests
{
    [Fact]
    public async Task LoginUsesStartEndpointAndAllowedAuthUrl()
    {
        await using var host = new FakeHost();
        await using var provider = CreateProvider(host);
        var login = provider.BeginLoginAsync(TestContext.Current.CancellationToken);
        await CompleteInitializeAsync(host);
        var request = await host.NextAsync();
        Assert.Equal("account/login/start", request.GetProperty("method").GetString());
        host.Respond(request, Json("""{"authUrl":"https://chatgpt.com/authorize","loginId":"login-1"}"""));
        Assert.Equal("https://chatgpt.com/authorize", (await login).ToString());
    }

    [Fact]
    public async Task DisconnectStartsProviderAndClearsStoredAccount()
    {
        await using var host = new FakeHost();
        await using var provider = CreateProvider(host);
        var disconnect = provider.DisconnectAsync(TestContext.Current.CancellationToken);
        await CompleteInitializeAsync(host);
        var logout = await host.NextAsync();
        Assert.Equal("account/logout", logout.GetProperty("method").GetString());
        host.Respond(logout, Json("{}"));
        await disconnect;
    }

    [Fact]
    public async Task ReadsChatGptRateLimitsFromTheAccountEndpoint()
    {
        await using var host = new FakeHost();
        await using var provider = CreateProvider(host);
        var read = provider.GetRateLimitsAsync(TestContext.Current.CancellationToken);
        await CompleteInitializeAsync(host);
        var account = await host.NextAsync();
        Assert.Equal("account/read", account.GetProperty("method").GetString());
        host.Respond(account, Json("""{"account":{"type":"chatgpt","planType":"plus"},"requiresOpenaiAuth":true}"""));
        var limits = await host.NextAsync();
        Assert.Equal("account/rateLimits/read", limits.GetProperty("method").GetString());
        host.Respond(limits, Json("""{"rateLimits":{"primary":{"usedPercent":25,"windowDurationMins":300,"resetsAt":1784246400},"secondary":null}}"""));

        var result = await read;
        Assert.Equal(25, result?.Primary?.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), result?.Primary?.Duration);
    }

    [Fact]
    public async Task ApiKeyAccountFailsBeforeModelOrTurnRequests()
    {
        await using var host = new FakeHost();
        await using var provider = CreateProvider(host);
        var answer = CollectAsync(provider.AnswerAsync(Png(), TestContext.Current.CancellationToken));
        await CompleteInitializeAsync(host);
        var account = await host.NextAsync();
        Assert.Equal("account/read", account.GetProperty("method").GetString());
        host.Respond(account, Json("""{"account":{"type":"apiKey"},"requiresOpenaiAuth":false}"""));
        var updates = await answer;
        Assert.Contains(updates, update => update.Kind == AnswerUpdateKind.Failed);
        Assert.DoesNotContain(host.Methods, method => method is "model/list" or "thread/start" or "turn/start");
    }

    [Fact]
    public async Task BuffersNotificationsBeforeTurnResponseAndCompletes()
    {
        await using var host = new FakeHost();
        await using var provider = CreateProvider(host);
        var answer = CollectAsync(provider.AnswerAsync(Png(), new AnswerRequestOptions("Show the calculation.", "request-model", "medium", FastMode: true), TestContext.Current.CancellationToken));
        await CompleteInitializeAsync(host);
        await RespondConnectedAccountAndCatalogAsync(host);
        var thread = await host.NextAsync();
        Assert.Equal("priority", thread.GetProperty("params").GetProperty("serviceTier").GetString());
        host.Respond(thread, Json("""{"thread":{"id":"t"}}"""));
        var turn = await host.NextAsync();
        Assert.Equal("request-model", turn.GetProperty("params").GetProperty("model").GetString());
        Assert.Equal("medium", turn.GetProperty("params").GetProperty("effort").GetString());
        Assert.Equal("priority", turn.GetProperty("params").GetProperty("serviceTierForTurn").GetString());
        Assert.Contains("Show the calculation.", turn.GetProperty("params").GetProperty("input")[0].GetProperty("text").GetString());
        host.Notify("item/started", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"a","phase":"final_answer","text":""}}"""));
        host.Notify("item/agentMessage/delta", Json("""{"threadId":"t","turnId":"u","itemId":"a","delta":"42."}"""));
        host.Notify("item/completed", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"a","phase":"final_answer","text":"42."}}"""));
        host.Respond(turn, Json("""{"turn":{"id":"u"}}"""));
        host.Notify("turn/completed", Json("""{"threadId":"t","turn":{"id":"u","status":"completed","items":[],"error":null}}"""));
        var updates = await answer;
        Assert.Contains(updates, update => update.Kind == AnswerUpdateKind.Model && update.Text.Contains("Test vision", StringComparison.Ordinal));
        Assert.Equal("42.", updates.Last(update => update.Kind == AnswerUpdateKind.Completed).Text);
    }

    [Fact]
    public async Task EofDuringAcceptedTurnFailsPromptly()
    {
        await using var host = new FakeHost();
        await using var provider = CreateProvider(host);
        var answer = CollectAsync(provider.AnswerAsync(Png(), TestContext.Current.CancellationToken));
        await CompleteInitializeAsync(host);
        await RespondConnectedAccountAndCatalogAsync(host);
        var thread = await host.NextAsync();
        host.Respond(thread, Json("""{"thread":{"id":"t"}}"""));
        var turn = await host.NextAsync();
        host.Respond(turn, Json("""{"turn":{"id":"u"}}"""));
        host.Close();
        var updates = await answer;
        Assert.Contains(updates, update => update.Kind == AnswerUpdateKind.Failed);
    }

    [Fact]
    public async Task CancellationInterruptsKnownTurnAndSecondAnswerWorks()
    {
        await using var host = new FakeHost();
        await using var provider = CreateProvider(host);
        using var cancellation = new CancellationTokenSource();
        var observedText = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CollectAsync(provider.AnswerAsync(Png(), cancellation.Token), update =>
        {
            if (update.Kind == AnswerUpdateKind.Text && update.Text == "42.")
                observedText.TrySetResult();
        });
        await CompleteInitializeAsync(host);
        await RespondConnectedAccountAndCatalogAsync(host);
        var thread = await host.NextAsync();
        host.Respond(thread, Json("""{"thread":{"id":"t"}}"""));
        var turn = await host.NextAsync();
        host.Respond(turn, Json("""{"turn":{"id":"u"}}"""));
        host.Notify("item/started", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"a","phase":"final_answer","text":""}}"""));
        host.Notify("item/agentMessage/delta", Json("""{"threadId":"t","turnId":"u","itemId":"a","delta":"42."}"""));
        await observedText.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var interrupt = await host.NextAsync();
        Assert.Equal("turn/interrupt", interrupt.GetProperty("method").GetString());
        host.Respond(interrupt, Json("{}"));
        Assert.Contains(await first, update => update.Kind == AnswerUpdateKind.Cancelled);

        var second = CollectAsync(provider.AnswerAsync(Png(), TestContext.Current.CancellationToken));
        var secondThread = await host.NextAsync();
        host.Respond(secondThread, Json("""{"thread":{"id":"t2"}}"""));
        var secondTurn = await host.NextAsync();
        host.Respond(secondTurn, Json("""{"turn":{"id":"u2"}}"""));
        host.Notify("item/started", Json("""{"threadId":"t2","turnId":"u2","item":{"type":"agentMessage","id":"a2","phase":"final_answer","text":""}}"""));
        host.Notify("item/agentMessage/delta", Json("""{"threadId":"t2","turnId":"u2","itemId":"a2","delta":"42."}"""));
        host.Notify("turn/completed", Json("""{"threadId":"t2","turn":{"id":"u2","status":"completed","items":[],"error":null}}"""));
        Assert.Contains(await second, update => update.Kind == AnswerUpdateKind.Completed);
    }

    [Fact]
    public async Task CancellationBeforeTurnAckResetsWithoutInterrupt()
    {
        await using var host = new FakeHost();
        await using var provider = CreateProvider(host);
        using var cancellation = new CancellationTokenSource();
        var answer = CollectAsync(provider.AnswerAsync(Png(), cancellation.Token));
        await CompleteInitializeAsync(host);
        await RespondConnectedAccountAndCatalogAsync(host);
        var thread = await host.NextAsync();
        host.Respond(thread, Json("""{"thread":{"id":"t"}}"""));
        _ = await host.NextAsync();
        cancellation.Cancel();
        Assert.Contains(await answer, update => update.Kind == AnswerUpdateKind.Cancelled);
        Assert.DoesNotContain(host.Methods, method => method == "turn/interrupt");
        await host.Connection.Closed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FailedTurnStartResetsUnknownTurnWithoutInterrupt()
    {
        await using var host = new FakeHost();
        await using var provider = CreateProvider(host);
        var answer = CollectAsync(provider.AnswerAsync(Png(), TestContext.Current.CancellationToken));
        await CompleteInitializeAsync(host);
        await RespondConnectedAccountAndCatalogAsync(host);
        var thread = await host.NextAsync();
        host.Respond(thread, Json("""{"thread":{"id":"t"}}"""));
        var turn = await host.NextAsync();
        host.Fail(turn, -32000, "turn failed");
        Assert.Contains(await answer, update => update.Kind == AnswerUpdateKind.Failed);
        Assert.DoesNotContain(host.Methods, method => method == "turn/interrupt");
        await host.Connection.Closed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DisposeWithActiveConsumerSettlesWithoutSemaphoreRace()
    {
        await using var host = new FakeHost();
        var provider = CreateProvider(host);
        var answer = CollectAsync(provider.AnswerAsync(Png(), TestContext.Current.CancellationToken));
        await CompleteInitializeAsync(host);
        await RespondConnectedAccountAndCatalogAsync(host);
        var thread = await host.NextAsync();
        host.Respond(thread, Json("""{"thread":{"id":"t"}}"""));
        _ = await host.NextAsync();
        await provider.DisposeAsync();
        await answer;
    }

    [Fact]
    public async Task NormalCompletionDoesNotInterrupt()
    {
        await using var host = new FakeHost();
        await using var provider = CreateProvider(host);
        var answer = CollectAsync(provider.AnswerAsync(Png(), TestContext.Current.CancellationToken));
        await CompleteInitializeAsync(host);
        await RespondConnectedAccountAndCatalogAsync(host);
        var thread = await host.NextAsync();
        host.Respond(thread, Json("""{"thread":{"id":"t"}}"""));
        var turn = await host.NextAsync();
        host.Respond(turn, Json("""{"turn":{"id":"u"}}"""));
        host.Notify("item/started", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"a","phase":"final_answer","text":"42."}}"""));
        host.Notify("turn/completed", Json("""{"threadId":"t","turn":{"id":"u","status":"completed","items":[],"error":null}}"""));
        Assert.Contains(await answer, update => update.Kind == AnswerUpdateKind.Completed);
        Assert.DoesNotContain(host.Methods, method => method == "turn/interrupt");
    }

    private static CodexAnswerProvider CreateProvider(FakeHost host) => new(new CodexLaunchOptions("C:\\fake\\codex.exe", "C:\\Screenshot\\Tests"), _ => host.Connection);

    private static async Task CompleteInitializeAsync(FakeHost host)
    {
        var initialize = await host.NextAsync();
        Assert.Equal("initialize", initialize.GetProperty("method").GetString());
        host.Respond(initialize, Json("{}"));
        var initialized = await host.NextAsync();
        Assert.Equal("initialized", initialized.GetProperty("method").GetString());
    }

    private static async Task RespondConnectedAccountAndCatalogAsync(FakeHost host)
    {
        var account = await host.NextAsync();
        Assert.Equal("account/read", account.GetProperty("method").GetString());
        host.Respond(account, Json("""{"account":{"type":"chatgpt","planType":"free"},"requiresOpenaiAuth":true}"""));
        var model = await host.NextAsync();
        Assert.Equal("model/list", model.GetProperty("method").GetString());
        host.Respond(model, Json("""{"data":[{"id":"catalog","model":"request-model","displayName":"Test vision","hidden":false,"isDefault":true,"defaultReasoningEffort":"low","supportedReasoningEfforts":[{"reasoningEffort":"low"},{"reasoningEffort":"medium"}],"inputModalities":["text","image"]}],"nextCursor":null}"""));
    }

    private static byte[] Png() => [137, 80, 78, 71, 13, 10, 26, 10];

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static async Task<List<AnswerUpdate>> CollectAsync(IAsyncEnumerable<AnswerUpdate> updates, Action<AnswerUpdate>? observe = null)
    {
        var result = new List<AnswerUpdate>();
        await using var enumerator = updates.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        while (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
        {
            result.Add(enumerator.Current);
            observe?.Invoke(enumerator.Current);
        }
        return result;
    }

    private sealed class FakeHost : IAsyncDisposable
    {
        private readonly LineReader reader = new();
        private readonly LineWriter writer;
        private readonly object methodsGate = new();
        private readonly List<string> methods = [];

        public FakeHost()
        {
            writer = new(RecordMethod);
            Connection = new JsonRpcConnection(reader, writer);
        }

        public JsonRpcConnection Connection { get; }
        public IReadOnlyList<string> Methods { get { lock (methodsGate) return methods.ToArray(); } }

        public async Task<JsonElement> NextAsync()
        {
            var line = await writer.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            return JsonDocument.Parse(line).RootElement.Clone();
        }

        public void Respond(JsonElement request, JsonElement result) => reader.Add(JsonSerializer.Serialize(new { id = request.GetProperty("id"), result }));
        public void Fail(JsonElement request, int code, string message) => reader.Add(JsonSerializer.Serialize(new { id = request.GetProperty("id"), error = new { code, message } }));
        public void Notify(string method, JsonElement parameters) => reader.Add(JsonSerializer.Serialize(new { method, @params = parameters }));
        public void Close() => reader.Complete();
        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();

        private void RecordMethod(string line)
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("method", out var method))
                lock (methodsGate) methods.Add(method.GetString()!);
        }
    }

    private sealed class LineReader : TextReader
    {
        private readonly Channel<string?> channel = Channel.CreateUnbounded<string?>();
        public void Add(string line) => channel.Writer.TryWrite(line);
        public void Complete() => channel.Writer.TryComplete();
        public override async Task<string?> ReadLineAsync() => await ReadLineAsync(CancellationToken.None);
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try { return await channel.Reader.ReadAsync(cancellationToken); }
            catch (ChannelClosedException) { return null; }
        }
        protected override void Dispose(bool disposing) { Complete(); base.Dispose(disposing); }
    }

    private sealed class LineWriter(Action<string> wrote) : TextWriter
    {
        private readonly Channel<string> channel = Channel.CreateUnbounded<string>();
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(string? value)
        {
            var line = value ?? string.Empty;
            wrote(line);
            channel.Writer.TryWrite(line);
            return Task.CompletedTask;
        }
        public override Task WriteLineAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken = default)
        {
            var line = value.ToString();
            wrote(line);
            channel.Writer.TryWrite(line);
            return Task.CompletedTask;
        }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask<string> ReadAsync(CancellationToken cancellationToken) => channel.Reader.ReadAsync(cancellationToken);
    }
}
