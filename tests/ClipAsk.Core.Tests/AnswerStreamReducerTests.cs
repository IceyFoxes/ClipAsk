using System.Text.Json;
using ClipAsk.Core.Providers;
using Xunit;

namespace ClipAsk.Core.Tests;

public sealed class AnswerStreamReducerTests
{
    [Fact]
    public void DeltasAreReconciledWithCanonicalCompletion()
    {
        var reducer = new AnswerStreamReducer("t", "u");
        Assert.Null(reducer.Apply("item/started", Json("""{"threadId":"other","turnId":"u","item":{"type":"agentMessage","id":"a","phase":"final_answer","text":"x"}}""")));
        Assert.Null(reducer.Apply("item/started", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"a","phase":"final_answer","text":""}}""")));
        Assert.Equal("4", reducer.Apply("item/agentMessage/delta", Json("""{"threadId":"t","turnId":"u","itemId":"a","delta":"4"}"""))!.Text);
        Assert.Equal("42", reducer.Apply("item/agentMessage/delta", Json("""{"threadId":"t","turnId":"u","itemId":"a","delta":"2"}"""))!.Text);
        Assert.Equal("42.", reducer.Apply("item/completed", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"a","phase":"final_answer","text":"42."}}"""))!.Text);
        var completed = reducer.Apply("turn/completed", Json("""{"threadId":"t","turn":{"id":"u","status":"completed","items":[],"error":null}}"""));
        Assert.Equal(AnswerUpdateKind.Completed, completed!.Kind);
        Assert.Equal("42.", completed.Text);
        Assert.Null(reducer.Apply("turn/completed", Json("""{"threadId":"t","turn":{"id":"u","status":"completed","items":[],"error":null}}""")));
    }

    [Fact]
    public void IgnoresWrongTurnAndHiddenPhasesAndCanonicalTextCanStandAlone()
    {
        var reducer = new AnswerStreamReducer("t", "u");
        Assert.Null(reducer.Apply("item/started", Json("""{"threadId":"t","turnId":"wrong","item":{"type":"agentMessage","id":"a","phase":"final_answer","text":"bad"}}""")));
        Assert.Null(reducer.Apply("item/started", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"r","phase":"reasoning","text":"secret"}}""")));
        Assert.Null(reducer.Apply("item/started", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"c","phase":"commentary","text":"progress"}}""")));
        var completed = reducer.Apply("turn/completed", Json("""{"threadId":"t","turn":{"id":"u","status":"completed","items":[{"type":"agentMessage","id":"a","phase":"final_answer","text":"42."}],"error":null}}"""));
        Assert.Equal(AnswerUpdateKind.Completed, completed!.Kind);
        Assert.Equal("42.", completed.Text);
    }

    [Fact]
    public void UsageLimitAndActionsFailWithoutRetry()
    {
        var limited = new AnswerStreamReducer("t", "u");
        var notificationFailure = limited.Apply("error", Json("""{"threadId":"t","turnId":"u","error":{"message":"Limit reached","codexErrorInfo":"usageLimitExceeded"},"willRetry":false}"""));
        Assert.Equal(AnswerUpdateKind.Failed, notificationFailure!.Kind);
        Assert.Contains("allowance", notificationFailure.Text, StringComparison.OrdinalIgnoreCase);
        var failure = new AnswerStreamReducer("t", "u").Apply("turn/completed", Json("""{"threadId":"t","turn":{"id":"u","status":"failed","items":[],"error":{"message":"Limit reached","codexErrorInfo":"usageLimitExceeded"}}}"""));
        Assert.Equal(AnswerUpdateKind.Failed, failure!.Kind);
        Assert.Contains("allowance", failure.Text, StringComparison.OrdinalIgnoreCase);

        var action = new AnswerStreamReducer("t", "u");
        var actionResult = action.Apply("item/started", Json("""{"threadId":"t","turnId":"u","item":{"type":"commandExecution","id":"c"}}"""));
        Assert.Equal(AnswerUpdateKind.Failed, actionResult!.Kind);
    }

    [Fact]
    public void ObjectCodexErrorInfoFailsWithoutExposingPayload()
    {
        var reducer = new AnswerStreamReducer("t", "u");
        var update = reducer.Apply("error", Json("""{"threadId":"t","turnId":"u","error":{"message":"temporary connection error","codexErrorInfo":{"httpConnectionFailed":{"httpStatusCode":503}}},"willRetry":false}"""));
        Assert.Equal(AnswerUpdateKind.Failed, update!.Kind);
        Assert.DoesNotContain("503", update.Text, StringComparison.Ordinal);

        var turnUpdate = new AnswerStreamReducer("t", "u").Apply("turn/completed", Json("""{"threadId":"t","turn":{"id":"u","status":"failed","items":[],"error":{"message":"temporary connection error","codexErrorInfo":{"httpConnectionFailed":{"httpStatusCode":503}}}}}"""));
        Assert.Equal(AnswerUpdateKind.Failed, turnUpdate!.Kind);
    }

    [Fact]
    public void RetryableErrorsWaitAndPublicItemsAreSeparated()
    {
        var reducer = new AnswerStreamReducer("t", "u");
        Assert.Equal(AnswerUpdateKind.Retrying, reducer.Apply("error", Json("""{"threadId":"t","turnId":"u","error":{"message":"retry"},"willRetry":true}"""))!.Kind);
        reducer.Apply("item/started", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"a","phase":"final_answer","text":"first"}}"""));
        var update = reducer.Apply("item/started", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"b","phase":"final_answer","text":"second"}}"""));
        Assert.Equal("first\n\nsecond", update!.Text);
        var action = new AnswerStreamReducer("t", "u");
        var completed = action.Apply("turn/completed", Json("""{"threadId":"t","turn":{"id":"u","status":"completed","items":[{"type":"collabAgentToolCall","id":"x"}],"error":null}}"""));
        Assert.Equal(AnswerUpdateKind.Failed, completed!.Kind);
    }

    [Fact]
    public void RetriesAreReportedOnceAndClearedByProgress()
    {
        var reducer = new AnswerStreamReducer("t", "u");
        const string retry = """{"threadId":"t","turnId":"u","error":{"message":"stream disconnected","codexErrorInfo":{"responseStreamDisconnected":{"httpStatusCode":502}}},"willRetry":true}""";
        var first = reducer.Apply("error", Json(retry));
        Assert.Equal(AnswerUpdateKind.Retrying, first!.Kind);
        Assert.Equal(AnswerStreamReducer.RetryingText, first.Text);
        Assert.Null(reducer.Apply("error", Json(retry)));
        Assert.True(reducer.IsRetrying);
        reducer.Apply("item/started", Json("""{"threadId":"t","turnId":"u","item":{"type":"agentMessage","id":"a","phase":"final_answer","text":"42."}}"""));
        Assert.False(reducer.IsRetrying);
        Assert.Equal(AnswerUpdateKind.Retrying, reducer.Apply("error", Json(retry))!.Kind);
    }

    [Fact]
    public void ServiceFailuresExplainTheOutageWithoutCodexDetails()
    {
        var reducer = new AnswerStreamReducer("t", "u");
        var update = reducer.Apply("error", Json("""{"threadId":"t","turnId":"u","error":{"message":"unexpected status 401 Unauthorized: Incorrect API key provided: sk-svcac","codexErrorInfo":{"responseTooManyFailedAttempts":{"httpStatusCode":401}}},"willRetry":false}"""));
        Assert.Equal(AnswerUpdateKind.Failed, update!.Kind);
        Assert.Equal(AnswerStreamReducer.ServiceUnavailableStatus, update.Text);
        Assert.Equal(AnswerStreamReducer.ServiceUnavailableDetail, update.Detail);
        Assert.DoesNotContain("API key", update.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TurnErrorKind.ServiceUnavailable, reducer.FailureKind);

        var retrying = new AnswerStreamReducer("t", "u");
        retrying.Apply("error", Json("""{"threadId":"t","turnId":"u","error":{"message":"retry"},"willRetry":true}"""));
        var gaveUp = retrying.GiveUpRetrying();
        Assert.Equal(AnswerStreamReducer.ServiceUnavailableDetail, gaveUp.Detail);
        Assert.False(retrying.IsRetrying);
        Assert.Null(retrying.Apply("turn/completed", Json("""{"threadId":"t","turn":{"id":"u","status":"completed","items":[],"error":null}}""")));
    }

    [Fact]
    public void RejectedSignInIsMarkedForConfirmation()
    {
        var reducer = new AnswerStreamReducer("t", "u");
        var update = reducer.Apply("turn/completed", Json("""{"threadId":"t","turn":{"id":"u","status":"failed","items":[],"error":{"message":"unauthorized","codexErrorInfo":"unauthorized"}}}"""));
        Assert.Equal(AnswerUpdateKind.Failed, update!.Kind);
        Assert.Equal(TurnErrorKind.SignIn, reducer.FailureKind);
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
