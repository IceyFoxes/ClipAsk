using Screenshot.Core.Diagnostics;

namespace Screenshot.Core.Providers;

public enum AnswerUpdateKind
{
    Status,
    Text,
    Completed,
    Failed,
    Cancelled
}

public sealed record AnswerUpdate(AnswerUpdateKind Kind, string Text);

public interface IAnswerProvider : IAsyncDisposable
{
    event Action? AccountChanged;
    Task<SubscriptionAccount> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<Uri> BeginLoginAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<AnswerUpdate> AnswerAsync(ReadOnlyMemory<byte> png, CancellationToken cancellationToken = default, CaptureTiming? timing = null);
}

public sealed record CodexLaunchOptions(string ExecutablePath, string StateDirectory);
