using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Screenshot.Core.Diagnostics;
using Screenshot.Core.Protocol;

namespace Screenshot.Core.Providers;

public sealed class CodexAnswerProvider : IAnswerProvider
{
    private readonly CodexLaunchOptions options;
    private readonly Func<CodexLaunchOptions, JsonRpcConnection>? transportFactory;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim answerGate = new(1, 1);
    private Process? process;
    private JsonRpcConnection? connection;
    private SubscriptionAccount? account;
    private CodexModelSelection? model;
    private string? loginId;
    private CancellationTokenSource? activeAnswerCancellation;
    private Task? activeAnswerTask;
    private int disposed;

    public CodexAnswerProvider(CodexLaunchOptions options)
    {
        this.options = options;
    }

    internal CodexAnswerProvider(CodexLaunchOptions options, Func<CodexLaunchOptions, JsonRpcConnection> transportFactory)
    {
        this.options = options;
        this.transportFactory = transportFactory;
    }

    public event Action? AccountChanged;

    public static CodexAnswerProvider CreateDefault()
    {
        var executable = Environment.GetEnvironmentVariable("SCREENSHOT_CODEX_PATH");
        if (string.IsNullOrWhiteSpace(executable))
            executable = Path.Combine(AppContext.BaseDirectory, "codex.exe");
        var state = Environment.GetEnvironmentVariable("SCREENSHOT_STATE_DIR");
        if (string.IsNullOrWhiteSpace(state))
            state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Screenshot");
        return new(new(Path.GetFullPath(executable), Path.GetFullPath(state)));
    }

    public async Task<SubscriptionAccount> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var rpc = connection ?? throw new InvalidOperationException("Codex transport is unavailable.");
        var response = await rpc.RequestAsync("account/read", new { refreshToken = false }, cancellationToken).ConfigureAwait(false);
        var next = CodexPolicy.ReadAccount(response);
        account = next;
        if (!next.IsConnected)
            model = null;
        return next;
    }

    public async Task<Uri> BeginLoginAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var rpc = connection ?? throw new InvalidOperationException("Codex transport is unavailable.");
        var response = await rpc.RequestAsync("account/login/start", CodexPolicy.LoginParameters(), cancellationToken).ConfigureAwait(false);
        if (!response.TryGetProperty("authUrl", out var authUrlElement) || !Uri.TryCreate(authUrlElement.GetString(), UriKind.Absolute, out var authUrl) || !CodexPolicy.IsAllowedLoginUri(authUrl))
            throw new InvalidOperationException("Codex returned a login URL outside the approved ChatGPT browser flow.");
        loginId = response.TryGetProperty("loginId", out var loginIdElement) ? loginIdElement.GetString() : null;
        return authUrl;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await CancelActiveAnswerAsync(cancellationToken).ConfigureAwait(false);
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rpc = connection;
            if (rpc is not null)
            {
                if (!string.IsNullOrWhiteSpace(loginId))
                {
                    await rpc.RequestAsync("account/login/cancel", new { loginId }, cancellationToken).ConfigureAwait(false);
                    loginId = null;
                }
                await rpc.RequestAsync("account/logout", null, cancellationToken).ConfigureAwait(false);
            }
            account = new(false, null);
            model = null;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task<JsonElement> ReadConfigAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var rpc = connection ?? throw new InvalidOperationException("Codex transport is unavailable.");
        return await rpc.RequestAsync("config/read", new { includeLayers = false }, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<AnswerUpdate> AnswerAsync(ReadOnlyMemory<byte> png, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default, CaptureTiming? timing = null)
    {
        await answerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateUnbounded<AnswerUpdate>();
        var producer = ProduceAnswerAsync(png, channel.Writer, linked, timing);
        lock (lifecycleGate)
        {
            activeAnswerCancellation = linked;
            activeAnswerTask = producer;
        }
        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync().ConfigureAwait(false))
                yield return update;
        }
        finally
        {
            linked.Cancel();
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            lock (lifecycleGate)
            {
                if (ReferenceEquals(activeAnswerCancellation, linked))
                    activeAnswerCancellation = null;
                if (ReferenceEquals(activeAnswerTask, producer))
                    activeAnswerTask = null;
            }
            answerGate.Release();
        }
    }

    private async Task ProduceAnswerAsync(ReadOnlyMemory<byte> png, ChannelWriter<AnswerUpdate> output, CancellationTokenSource linked, CaptureTiming? timing)
    {
        string? threadId = null;
        string? turnId = null;
        var turnRequestSent = false;
        var turnStarted = false;
        var shouldReset = false;
        var notifications = new List<(string Method, JsonElement Parameters)>();
        var sync = new object();
        var terminal = new TaskCompletionSource<AnswerUpdateKind>(TaskCreationOptions.RunContinuationsAsynchronously);
        AnswerStreamReducer? reducer = null;
        JsonRpcConnection? rpc = null;

        void Publish(AnswerUpdate? update)
        {
            if (update is null || terminal.Task.IsCompleted)
                return;
            if (update.Kind is AnswerUpdateKind.Text or AnswerUpdateKind.Completed)
                timing?.MarkFirstText(update.Text);
            if (update.Kind == AnswerUpdateKind.Completed)
                timing?.MarkCompleted();
            output.TryWrite(update);
            if (update.Kind is AnswerUpdateKind.Completed or AnswerUpdateKind.Failed or AnswerUpdateKind.Cancelled)
                terminal.TrySetResult(update.Kind);
        }

        void OnNotification(string method, JsonElement parameters)
        {
            lock (sync)
            {
                if (reducer is null)
                    notifications.Add((method, parameters.Clone()));
                else
                    Publish(reducer.Apply(method, parameters));
            }
        }

        void OnUnsupportedRequest(string _)
        {
            lock (sync)
                Publish(new(AnswerUpdateKind.Failed, "Codex requested an unavailable tool action."));
        }

        try
        {
            Publish(new(AnswerUpdateKind.Status, "Preparing answer…"));
            await EnsureStartedAsync(linked.Token).ConfigureAwait(false);
            rpc = connection ?? throw new InvalidOperationException("Codex transport is unavailable.");
            var connected = account?.IsConnected == true ? account : await GetAccountAsync(linked.Token).ConfigureAwait(false);
            if (!connected.IsConnected)
                throw new InvalidOperationException("Connect a ChatGPT account before answering a screenshot.");
            var selectedModel = await GetModelAsync(rpc, linked.Token).ConfigureAwait(false);
            var threadResponse = await rpc.RequestAsync("thread/start", CodexPolicy.ThreadParameters(GetWorkspace(), selectedModel), linked.Token).ConfigureAwait(false);
            threadId = ReadId(threadResponse, "threadId", "thread") ?? throw new InvalidOperationException("Codex did not return a thread identifier.");

            rpc.Notification += OnNotification;
            rpc.UnsupportedRequest += OnUnsupportedRequest;
            var turnParameters = CodexPolicy.TurnParameters(threadId, png, selectedModel);
            turnRequestSent = true;
            var turnResponse = await rpc.RequestAsync("turn/start", turnParameters, linked.Token).ConfigureAwait(false);
            turnId = ReadId(turnResponse, "turnId", "turn") ?? throw new InvalidOperationException("Codex did not return a turn identifier.");
            turnStarted = true;
            lock (sync)
            {
                reducer = new(threadId, turnId);
                foreach (var notification in notifications)
                    Publish(reducer.Apply(notification.Method, notification.Parameters));
                notifications.Clear();
            }

            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            var completed = await Task.WhenAny(terminal.Task, rpc.Closed, cancellationTask).ConfigureAwait(false);
            if (completed == rpc.Closed)
            {
                shouldReset = true;
                Publish(new(AnswerUpdateKind.Failed, "Codex transport closed before the answer completed."));
            }
            else if (completed == cancellationTask)
            {
                Publish(new(AnswerUpdateKind.Cancelled, "Answer stopped."));
            }

        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            Publish(new(AnswerUpdateKind.Cancelled, "Answer stopped."));
        }
        catch (Exception exception)
        {
            Publish(new(AnswerUpdateKind.Failed, DescribeException(exception)));
        }
        finally
        {
            if (rpc is not null)
            {
                rpc.Notification -= OnNotification;
                rpc.UnsupportedRequest -= OnUnsupportedRequest;
            }
            var kind = terminal.Task.IsCompletedSuccessfully ? terminal.Task.Result : AnswerUpdateKind.Failed;
            if (turnRequestSent && kind is not AnswerUpdateKind.Completed)
            {
                if (turnStarted && rpc is not null && threadId is not null && turnId is not null)
                {
                    if (!await InterruptAsync(rpc, threadId, turnId).ConfigureAwait(false))
                        shouldReset = true;
                }
                else
                {
                    shouldReset = true;
                }
            }
            if (rpc?.Closed.IsCompleted == true && kind is not AnswerUpdateKind.Completed)
                shouldReset = true;
            if (shouldReset)
                await ResetProcessAsync().ConfigureAwait(false);
            output.TryComplete();
        }
    }

    private async Task<CodexModelSelection> GetModelAsync(JsonRpcConnection rpc, CancellationToken cancellationToken)
    {
        if (model is not null)
            return model;
        var entries = new List<JsonElement>();
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            object parameters = cursor is null
                ? new { limit = 50, includeHidden = false }
                : new { limit = 50, includeHidden = false, cursor };
            var response = await rpc.RequestAsync("model/list", parameters, cancellationToken).ConfigureAwait(false);
            if (response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                entries.AddRange(data.EnumerateArray().Select(value => value.Clone()));
            else if (response.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                entries.AddRange(models.EnumerateArray().Select(value => value.Clone()));
            var next = response.TryGetProperty("nextCursor", out var nextElement) ? nextElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(next))
                break;
            if (!seen.Add(next))
                throw new InvalidOperationException("Codex returned a repeated model catalog cursor.");
            cursor = next;
        }
        return model = CodexPolicy.SelectModel(entries);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref disposed) != 0)
            throw new ObjectDisposedException(nameof(CodexAnswerProvider));
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (connection is not null)
            {
                if (!connection.Closed.IsCompleted)
                    return;
                await ResetProcessCoreAsync().ConfigureAwait(false);
            }
            if (transportFactory is not null)
            {
                connection = transportFactory(options);
                connection.Notification += OnConnectionNotification;
                connection.Start();
                await InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (!Path.IsPathRooted(options.ExecutablePath) || !File.Exists(options.ExecutablePath))
                throw new FileNotFoundException("The pinned native Codex executable was not found.", options.ExecutablePath);
            Directory.CreateDirectory(options.StateDirectory);
            var plan = CodexProcessPlan.Create(options);
            Directory.CreateDirectory(plan.CodexHome);
            Directory.CreateDirectory(plan.Workspace);
            var version = await ReadVersionAsync(plan, cancellationToken).ConfigureAwait(false);
            if (!CodexProcessVersion.IsCompatible(version))
                throw new InvalidOperationException($"Incompatible Codex runtime. Expected {CodexProcessVersion.Expected}.");
            var startInfo = plan.CreateStartInfo();
            process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
                throw new InvalidOperationException("The native Codex process could not be started.");
            _ = DrainAsync(process.StandardError);
            connection = new(process.StandardOutput, process.StandardInput);
            connection.Notification += OnConnectionNotification;
            connection.Start();
            await InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await ResetProcessCoreAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private static async Task InitializeAsync(JsonRpcConnection rpc, CancellationToken cancellationToken)
    {
        var initialize = JsonSerializer.SerializeToElement(new
        {
            clientInfo = new { name = "screenshot_desktop", title = "Screenshot", version = "0.1.0" }
        });
        await rpc.RequestAsync("initialize", initialize, cancellationToken).ConfigureAwait(false);
        await rpc.NotifyAsync("initialized", null, cancellationToken).ConfigureAwait(false);
    }

    private void OnConnectionNotification(string method, JsonElement _)
    {
        if (method is "account/login/completed" or "account/updated")
        {
            account = null;
            model = null;
            AccountChanged?.Invoke();
        }
    }

    private async Task<string> ReadVersionAsync(CodexProcessPlan plan, CancellationToken cancellationToken)
    {
        var info = plan.CreateStartInfo(["--version"]);
        using var versionProcess = Process.Start(info) ?? throw new InvalidOperationException("The native Codex version process could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var standardOutput = versionProcess.StandardOutput.ReadToEndAsync(timeout.Token);
        var standardError = versionProcess.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await versionProcess.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!versionProcess.HasExited)
                versionProcess.Kill(true);
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new InvalidOperationException("The native Codex version check timed out.");
        }
        return await standardOutput.ConfigureAwait(false);
    }

    private static async Task<bool> InterruptAsync(JsonRpcConnection rpc, string threadId, string turnId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await rpc.RequestAsync("turn/interrupt", new { threadId, turnId }, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task CancelActiveAnswerAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (lifecycleGate)
        {
            cts = activeAnswerCancellation;
            task = activeAnswerTask;
        }
        if (cts is null || task is null)
            return;
        cts.Cancel();
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
            await ResetProcessAsync().ConfigureAwait(false);
        }
    }

    private async Task ResetProcessAsync()
    {
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ResetProcessCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task ResetProcessCoreAsync()
    {
        var oldConnection = connection;
        var oldProcess = process;
        connection = null;
        process = null;
        account = null;
        model = null;
        if (oldProcess is not null)
        {
            try
            {
                if (!oldProcess.HasExited)
                    oldProcess.Kill(true);
                await oldProcess.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            oldProcess.Dispose();
        }
        if (oldConnection is not null)
            await oldConnection.DisposeAsync().ConfigureAwait(false);
    }

    private string GetWorkspace() => Path.Combine(Path.GetFullPath(options.StateDirectory), "Workspace");

    private static async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is not null)
        {
        }
    }

    private static string? ReadId(JsonElement response, string directName, string nestedName)
    {
        if (response.TryGetProperty(directName, out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString();
        if (response.TryGetProperty(nestedName, out var nested) && nested.ValueKind == JsonValueKind.Object && nested.TryGetProperty("id", out var id))
            return id.GetString();
        return null;
    }

    private static string DescribeException(Exception exception) => exception switch
    {
        JsonRpcException rpc => $"Codex operation failed ({rpc.Code}).",
        TimeoutException => "Codex operation timed out.",
        _ => exception.Message
    };

    public void StopOwnedProcessForShutdown()
    {
        var owned = process;
        if (owned is null)
            return;
        try
        {
            if (!owned.HasExited)
                owned.Kill(true);
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        await CancelActiveAnswerAsync(CancellationToken.None).ConfigureAwait(false);
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ResetProcessCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }
}
