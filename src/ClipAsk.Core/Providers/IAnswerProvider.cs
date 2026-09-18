using ClipAsk.Core.Diagnostics;

namespace ClipAsk.Core.Providers;

public enum AnswerUpdateKind
{
    Status,
    Text,
    Completed,
    Failed,
    Cancelled,
    Model
}

public sealed record AnswerUpdate(AnswerUpdateKind Kind, string Text);

public sealed record AnswerRequestOptions(string? Instruction = null, string? Model = null)
{
    public static AnswerRequestOptions Default { get; } = new();
}

public sealed record AccountRateLimitWindow(double UsedPercent, TimeSpan Duration, DateTimeOffset ResetsAt);

public sealed record AccountRateLimits(AccountRateLimitWindow? Primary, AccountRateLimitWindow? Secondary);

public interface IAnswerProvider : IAsyncDisposable
{
    event Action? AccountChanged;
    event Action<AccountRateLimits?>? RateLimitsChanged;
    Task<SubscriptionAccount> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<AccountRateLimits?> GetRateLimitsAsync(CancellationToken cancellationToken = default);
    Task<Uri> BeginLoginAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<CodexModelSelection?> GetAutomaticModelAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CodexModelSelection>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<AnswerUpdate> AnswerAsync(ReadOnlyMemory<byte> png, CancellationToken cancellationToken = default, CaptureTiming? timing = null);
    IAsyncEnumerable<AnswerUpdate> AnswerAsync(ReadOnlyMemory<byte> png, AnswerRequestOptions options, CancellationToken cancellationToken = default, CaptureTiming? timing = null);
}

public sealed record CodexLaunchOptions(string ExecutablePath, string StateDirectory);
