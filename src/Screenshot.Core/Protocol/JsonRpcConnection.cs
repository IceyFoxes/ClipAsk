using System.Text.Json;

namespace Screenshot.Core.Protocol;

public sealed class JsonRpcException : Exception
{
    public JsonRpcException(int code, string message) : base(message) => Code = code;

    public int Code { get; }
}

public sealed class JsonRpcConnection : IAsyncDisposable
{
    private readonly TextReader reader;
    private readonly TextWriter writer;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly CancellationTokenSource ioCancellation = new();
    private readonly object pendingGate = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> pending = [];
    private readonly TaskCompletionSource<Exception> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? readerTask;
    private Exception? terminalFailure;
    private bool disposed;
    private int nextId;

    public JsonRpcConnection(TextReader reader, TextWriter writer)
    {
        this.reader = reader;
        this.writer = writer;
    }

    public event Action<string, JsonElement>? Notification;
    public event Action<string>? UnsupportedRequest;
    public Task<Exception> Closed => closed.Task;

    public void Start()
    {
        lock (pendingGate)
        {
            if (disposed || terminalFailure is not null)
                throw new ObjectDisposedException(nameof(JsonRpcConnection));
            if (readerTask is not null)
                throw new InvalidOperationException("The JSON-RPC connection is already started.");
            readerTask = ReadLoopAsync();
        }
    }

    public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("A method is required.", nameof(method));

        TaskCompletionSource<JsonElement> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Interlocked.Increment(ref nextId);
        lock (pendingGate)
        {
            ThrowIfTerminal();
            if (readerTask is null)
                throw new InvalidOperationException("The JSON-RPC connection is not available.");
            pending.Add(id, completion);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var registration = linked.Token.Register(() => CancelPending(id, completion, cancellationToken));
        try
        {
            await WriteAsync(new { id, method, @params = parameters }, linked.Token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            RemovePending(id, completion);
            throw new TimeoutException($"Codex RPC request '{method}' timed out after 20 seconds.");
        }
        catch
        {
            RemovePending(id, completion);
            throw;
        }
    }

    public Task NotifyAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("A method is required.", nameof(method));
        lock (pendingGate)
            ThrowIfTerminal();
        return WriteAsync(new { method, @params = parameters }, cancellationToken);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(ioCancellation.Token).ConfigureAwait(false);
                if (line is null)
                    throw new InvalidOperationException("Codex protocol ended before all responses arrived.");
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (message.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Codex returned a non-object JSON-RPC message.");

                var hasId = message.TryGetProperty("id", out var id);
                var hasMethod = message.TryGetProperty("method", out var methodElement);
                if (hasMethod)
                {
                    var method = methodElement.GetString() ?? throw new InvalidOperationException("Codex returned an invalid method.");
                    var parameters = message.TryGetProperty("params", out var paramsElement)
                        ? paramsElement.Clone()
                        : default;
                    if (hasId)
                    {
                        await WriteErrorAsync(id.Clone(), CancellationToken.None).ConfigureAwait(false);
                        UnsupportedRequest?.Invoke(method);
                    }
                    else
                    {
                        Notification?.Invoke(method, parameters);
                    }
                    continue;
                }

                if (!hasId || !id.TryGetInt32(out var responseId))
                    throw new InvalidOperationException("Codex returned a malformed response.");

                TaskCompletionSource<JsonElement>? completion;
                lock (pendingGate)
                    pending.Remove(responseId, out completion);
                if (completion is null)
                    continue;

                if (message.TryGetProperty("error", out var error))
                {
                    var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
                        ? parsedCode
                        : -32000;
                    var errorMessage = error.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString() ?? "Codex returned an RPC error."
                        : "Codex returned an RPC error.";
                    completion.TrySetException(new JsonRpcException(code, errorMessage));
                }
                else if (message.TryGetProperty("result", out var result))
                {
                    completion.TrySetResult(result.Clone());
                }
                else
                {
                    completion.TrySetException(new InvalidOperationException("Codex returned a response without result or error."));
                }
            }
        }
        catch (Exception exception)
        {
            FailTransport(exception);
        }
    }

    private async Task WriteErrorAsync(JsonElement id, CancellationToken cancellationToken)
    {
        await WriteAsync(new
        {
            id,
            error = new
            {
                code = -32601,
                message = "Screenshot supports image answers only; tool execution is unavailable."
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAsync(object message, CancellationToken cancellationToken)
    {
        var acquired = false;
        var writeStarted = false;
        try
        {
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            lock (pendingGate)
                ThrowIfTerminal();
            var line = JsonSerializer.Serialize(message);
            writeStarted = true;
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException || writeStarted)
                FailTransport(exception);
            throw;
        }
        finally
        {
            if (acquired)
                writeGate.Release();
        }
    }

    private void CancelPending(int id, TaskCompletionSource<JsonElement> completion, CancellationToken cancellationToken)
    {
        if (RemovePending(id, completion))
            completion.TrySetCanceled(cancellationToken);
    }

    private bool RemovePending(int id, TaskCompletionSource<JsonElement> completion)
    {
        lock (pendingGate)
        {
            if (!pending.TryGetValue(id, out var current) || !ReferenceEquals(current, completion))
                return false;
            pending.Remove(id);
            return true;
        }
    }

    private void FailTransport(Exception exception)
    {
        TaskCompletionSource<JsonElement>[] completions;
        lock (pendingGate)
        {
            if (terminalFailure is not null)
                return;
            terminalFailure = exception;
            completions = pending.Values.ToArray();
            pending.Clear();
        }
        foreach (var completion in completions)
            completion.TrySetException(exception);
        closed.TrySetResult(exception);
    }

    private void ThrowIfTerminal()
    {
        if (terminalFailure is not null)
            throw new InvalidOperationException("The Codex JSON-RPC connection is closed.", terminalFailure);
        if (disposed)
            throw new ObjectDisposedException(nameof(JsonRpcConnection));
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop;
        lock (pendingGate)
        {
            if (disposed)
                return;
            disposed = true;
            loop = readerTask;
        }
        FailTransport(new ObjectDisposedException(nameof(JsonRpcConnection)));
        ioCancellation.Cancel();
        reader.Dispose();
        writer.Dispose();
        if (loop is not null)
            await loop.ConfigureAwait(false);
    }
}
