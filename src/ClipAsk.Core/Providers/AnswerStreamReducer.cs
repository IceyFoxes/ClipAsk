using System.Text.Json;

namespace ClipAsk.Core.Providers;

public sealed class AnswerStreamReducer(string threadId, string turnId)
{
    public const string RetryingText = "Connection to ChatGPT dropped. Retrying…";
    public const string ServiceUnavailableStatus = "ChatGPT isn't responding";
    public const string ServiceUnavailableDetail =
        "**ChatGPT's Codex service isn't responding right now.**\n\n" +
        "This is usually a temporary outage on OpenAI's side, not a problem with your capture or account. " +
        "Your capture is still here, so you can try again once the service is back.\n\n" +
        "Check status.openai.com for updates.";
    public const string SignInStatus = "ChatGPT couldn't verify your sign-in";
    public const string SignInDetail =
        "**ChatGPT couldn't verify your sign-in.**\n\n" +
        "Choose Disconnect ChatGPT from the ClipAsk menu, then connect ChatGPT again.";

    private static readonly HashSet<string> ActionTypes = new(StringComparer.Ordinal)
    {
        "commandExecution",
        "fileChange",
        "webSearch",
        "mcpToolCall",
        "dynamicToolCall",
        "collabAgentToolCall",
        "agentDelegation",
        "imageGeneration"
    };

    private readonly List<Message> messages = [];
    private bool terminal;
    private bool retrying;

    public bool IsRetrying => retrying;
    public TurnErrorKind? FailureKind { get; private set; }

    public AnswerUpdate? Apply(string method, JsonElement parameters)
    {
        if (terminal || parameters.ValueKind != JsonValueKind.Object || !Matches(parameters))
            return null;
        if (method == "error")
            return ApplyError(parameters);
        if (method == "turn/completed")
            return CompleteTurn(parameters);
        if (method is "item/started" or "item/completed")
            return ApplyItem(method, parameters);
        if (method == "item/agentMessage/delta")
            return ApplyDelta(parameters);
        return null;
    }

    private AnswerUpdate? ApplyError(JsonElement parameters)
    {
        if (parameters.TryGetProperty("willRetry", out var retry) && retry.ValueKind == JsonValueKind.True)
        {
            if (retrying)
                return null;
            retrying = true;
            return new(AnswerUpdateKind.Retrying, RetryingText);
        }
        var error = parameters.TryGetProperty("error", out var errorElement) ? errorElement : default;
        return Fail(CodexPolicy.ClassifyTurnError(error));
    }

    // Called when Codex has retried past CodexPolicy.ServiceRetryLimit without progress.
    public AnswerUpdate GiveUpRetrying() => Fail(TurnErrorKind.ServiceUnavailable);

    private AnswerUpdate? ApplyItem(string method, JsonElement parameters)
    {
        if (!parameters.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
            return null;
        if (IsAction(item))
            return Fail("This response requested an unavailable tool action.");
        var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        if (type != "agentMessage")
            return null;
        if (item.TryGetProperty("phase", out var phase) && phase.GetString() is "commentary" or "reasoning")
            return null;

        var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var message = messages.FirstOrDefault(value => value.Id == id);
        if (message is null)
        {
            message = new(id, string.Empty);
            messages.Add(message);
        }
        if (method == "item/completed" && item.TryGetProperty("text", out var text))
            message.Text = text.GetString() ?? string.Empty;
        else if (method == "item/started" && item.TryGetProperty("text", out text))
            message.Text = text.GetString() ?? string.Empty;
        return VisibleTextUpdate();
    }

    private AnswerUpdate? ApplyDelta(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("itemId", out var itemIdElement))
            return null;
        var itemId = itemIdElement.GetString();
        var delta = parameters.TryGetProperty("delta", out var deltaElement) ? deltaElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(itemId) || delta is null)
            return null;
        var message = messages.FirstOrDefault(value => value.Id == itemId);
        if (message is null)
            return null;
        message.Text += delta;
        return VisibleTextUpdate();
    }

    private AnswerUpdate? CompleteTurn(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("turn", out var turn) || turn.ValueKind != JsonValueKind.Object)
            return Fail("Codex returned an incomplete turn.");
        if (turn.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (IsAction(item))
                    return Fail("This response requested an unavailable tool action.");
            }
            if (items.GetArrayLength() > 0)
                Reconcile(items);
        }
        var status = turn.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        if (status is "interrupted" or "cancelled")
        {
            terminal = true;
            return new(AnswerUpdateKind.Cancelled, "Answer stopped.");
        }
        if (turn.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            return Fail(CodexPolicy.ClassifyTurnError(error));
        var visible = VisibleText();
        if (status == "completed" && !string.IsNullOrWhiteSpace(visible))
        {
            terminal = true;
            return new(AnswerUpdateKind.Completed, visible);
        }
        return Fail("Codex returned no response text.");
    }

    private void Reconcile(JsonElement items)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("type", out var type) || type.GetString() != "agentMessage" ||
                (item.TryGetProperty("phase", out var phase) && phase.GetString() is "commentary" or "reasoning") ||
                !item.TryGetProperty("id", out var idElement) || !item.TryGetProperty("text", out var textElement))
                continue;
            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var message = messages.FirstOrDefault(value => value.Id == id);
            if (message is null)
                messages.Add(new(id, textElement.GetString() ?? string.Empty));
            else
                message.Text = textElement.GetString() ?? string.Empty;
        }
    }

    private bool Matches(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("threadId", out var thread) || thread.GetString() != threadId)
            return false;
        if (parameters.TryGetProperty("turnId", out var directTurn) && directTurn.GetString() != turnId)
            return false;
        if (parameters.TryGetProperty("turn", out var nestedTurn) && nestedTurn.ValueKind == JsonValueKind.Object &&
            nestedTurn.TryGetProperty("id", out var nestedId) && nestedId.GetString() != turnId)
            return false;
        return true;
    }

    private static bool IsAction(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String && ActionTypes.Contains(type.GetString()!);

    private AnswerUpdate? VisibleTextUpdate()
    {
        var visible = VisibleText();
        if (string.IsNullOrEmpty(visible))
            return null;
        retrying = false;
        return new(AnswerUpdateKind.Text, visible);
    }

    private string VisibleText() => string.Join("\n\n", messages.Select(message => message.Text).Where(text => !string.IsNullOrEmpty(text)));

    private AnswerUpdate Fail(string text)
    {
        terminal = true;
        return new(AnswerUpdateKind.Failed, text);
    }

    // A rejected sign-in is reported as a service problem until the provider
    // confirms that the account is actually signed out.
    private AnswerUpdate Fail(TurnErrorKind kind)
    {
        var update = kind switch
        {
            TurnErrorKind.Allowance => Fail("Your ChatGPT plan allowance is exhausted for this response."),
            TurnErrorKind.SignIn or TurnErrorKind.ServiceUnavailable => new(AnswerUpdateKind.Failed, ServiceUnavailableStatus, ServiceUnavailableDetail),
            _ => Fail("Codex could not complete this response.")
        };
        terminal = true;
        retrying = false;
        FailureKind = kind;
        return update;
    }

    private sealed class Message(string id, string text)
    {
        public string Id { get; } = id;
        public string Text { get; set; } = text;
    }
}
