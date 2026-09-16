using Screenshot.Core.Diagnostics;

namespace Screenshot.Core.Providers;

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

public interface IAnswerProvider : IAsyncDisposable
{
    event Action? AccountChanged;
    Task<SubscriptionAccount> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<Uri> BeginLoginAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CodexModelSelection>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<AnswerUpdate> AnswerAsync(ReadOnlyMemory<byte> png, CancellationToken cancellationToken = default, CaptureTiming? timing = null);
    IAsyncEnumerable<AnswerUpdate> AnswerAsync(ReadOnlyMemory<byte> png, AnswerRequestOptions options, CancellationToken cancellationToken = default, CaptureTiming? timing = null);
}

public sealed record CodexLaunchOptions(string ExecutablePath, string StateDirectory);
