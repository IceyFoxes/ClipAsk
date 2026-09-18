using System.Text;
using System.Threading.Channels;
using System.Text.Json;
using ClipAsk.Core.Protocol;
using Xunit;

namespace ClipAsk.Core.Tests;

public sealed class JsonRpcConnectionTests
{
    [Fact]
    public async Task CorrelatesOutOfOrderResponsesAndKeepsNotifications()
    {
        using var reader = new LineReader();
        using var writer = new LineWriter();
        await using var connection = new JsonRpcConnection(reader, writer);
        var notification = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Notification += (method, _) => notification.TrySetResult(method);
        connection.Start();
        var testToken = TestContext.Current.CancellationToken;
        var first = connection.RequestAsync("first", null, testToken);
        var second = connection.RequestAsync("second", null, testToken);
        await writer.WaitForCountAsync(2);
        var ids = writer.Lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("id").GetInt32()).ToArray();
        reader.Add($"{{\"method\":\"status\",\"params\":{{\"ready\":true}}}}");
        reader.Add($"{{\"id\":{ids[1]},\"result\":{{\"value\":2}}}}");
        reader.Add($"{{\"id\":{ids[0]},\"result\":{{\"value\":1}}}}");
        Assert.Equal("status", await notification.Task.WaitAsync(TimeSpan.FromSeconds(2), testToken));
        Assert.Equal(1, (await first).GetProperty("value").GetInt32());
        Assert.Equal(2, (await second).GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task CancellationLateResponseDoesNotAffectSecondRequest()
    {
        using var reader = new LineReader();
        using var writer = new LineWriter();
        await using var connection = new JsonRpcConnection(reader, writer);
        connection.Start();
        using var cancellation = new CancellationTokenSource();
        var first = connection.RequestAsync("first", null, cancellation.Token);
        await writer.WaitForCountAsync(1);
        var firstId = JsonDocument.Parse(writer.Lines[0]).RootElement.GetProperty("id").GetInt32();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var second = connection.RequestAsync("second", null, TestContext.Current.CancellationToken);
        await writer.WaitForCountAsync(2);
        var secondId = JsonDocument.Parse(writer.Lines[1]).RootElement.GetProperty("id").GetInt32();
        reader.Add($"{{\"id\":{firstId},\"result\":{{\"value\":1}}}}");
        reader.Add($"{{\"id\":{secondId},\"result\":{{\"value\":2}}}}");
        Assert.Equal(2, (await second).GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task EofFailsPendingAndFutureRequestsPromptly()
    {
        using var reader = new LineReader();
        using var writer = new LineWriter();
        await using var connection = new JsonRpcConnection(reader, writer);
        connection.Start();
        var request = connection.RequestAsync("wait", null, TestContext.Current.CancellationToken);
        await writer.WaitForCountAsync(1);
        reader.Complete();
        await Assert.ThrowsAsync<InvalidOperationException>(() => request);
        await connection.Closed.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.RequestAsync("future", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MalformedInputFailsPendingAndClosesTransport()
    {
        using var reader = new LineReader();
        using var writer = new LineWriter();
        await using var connection = new JsonRpcConnection(reader, writer);
        connection.Start();
        var request = connection.RequestAsync("wait", null, TestContext.Current.CancellationToken);
        await writer.WaitForCountAsync(1);
        reader.Add("not-json");
        await Assert.ThrowsAnyAsync<JsonException>(() => request);
        await connection.Closed.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WriteFailureClosesTransport()
    {
        using var reader = new LineReader();
        using var writer = new LineWriter { ThrowOnWrite = true };
        await using var connection = new JsonRpcConnection(reader, writer);
        connection.Start();
        await Assert.ThrowsAsync<IOException>(() => connection.RequestAsync("write", null, TestContext.Current.CancellationToken));
        await connection.Closed.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CancellationDuringStartedWriteClosesTransport()
    {
        using var reader = new LineReader();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var writer = new BlockingWriter(started);
        await using var connection = new JsonRpcConnection(reader, writer);
        connection.Start();
        using var cancellation = new CancellationTokenSource();
        var request = connection.RequestAsync("large", null, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await connection.Closed.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ServerRequestsAreRejectedWithoutApproval()
    {
        using var reader = new LineReader();
        using var writer = new LineWriter();
        await using var connection = new JsonRpcConnection(reader, writer);
        var unsupported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.UnsupportedRequest += method => unsupported.TrySetResult(method);
        connection.Start();
        reader.Add("{\"id\":\"abc\",\"method\":\"commandExecution/requestApproval\",\"params\":{}}");
        await writer.WaitForCountAsync(1);
        Assert.Equal("commandExecution/requestApproval", await unsupported.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        var rejected = JsonDocument.Parse(writer.Lines[0]).RootElement;
        Assert.Equal("abc", rejected.GetProperty("id").GetString());
        Assert.Equal(-32601, rejected.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("ClipAsk supports visual responses only; tool execution is unavailable.", rejected.GetProperty("error").GetProperty("message").GetString());
    }

    private sealed class LineReader : TextReader
    {
        private readonly Channel<string?> lines = Channel.CreateUnbounded<string?>();
        public void Add(string line) => lines.Writer.TryWrite(line);
        public void Complete() => lines.Writer.TryComplete();
        public override async Task<string?> ReadLineAsync() => await ReadLineAsync(CancellationToken.None);
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await lines.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }
        protected override void Dispose(bool disposing)
        {
            Complete();
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingWriter(TaskCompletionSource started) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class LineWriter : TextWriter
    {
        private readonly object gate = new();
        private readonly List<string> lines = [];
        private readonly SemaphoreSlim signal = new(0);
        public override Encoding Encoding => Encoding.UTF8;
        public bool ThrowOnWrite { get; init; }
        public IReadOnlyList<string> Lines { get { lock (gate) return lines.ToArray(); } }
        public override Task WriteLineAsync(string? value) => WriteLineCore(value ?? string.Empty);
        public override Task WriteLineAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken = default) => WriteLineCore(value.ToString());
        private Task WriteLineCore(string value)
        {
            if (ThrowOnWrite)
                throw new IOException("writer failure");
            lock (gate) lines.Add(value);
            signal.Release();
            return Task.CompletedTask;
        }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async Task WaitForCountAsync(int count)
        {
            while (Lines.Count < count && await signal.WaitAsync(TimeSpan.FromSeconds(2)))
            {
            }
            if (Lines.Count < count)
                throw new TimeoutException("Timed out waiting for JSON-RPC output.");
        }
    }
}
