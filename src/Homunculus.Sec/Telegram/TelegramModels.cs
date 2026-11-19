namespace Homunculus.Sec.Telegram;

public sealed class TelegramException(string message) : Exception(message);

/// <summary>An inline button under a message. <paramref name="Data"/> comes back when it is pressed (Telegram allows 64 bytes).</summary>
public sealed record Button(string Text, string Data);

public sealed record IncomingMessage(long ChatId, long FromId, long MessageId, string? Text, long? ReplyToMessageId);

public sealed record ButtonPress(string Id, long FromId, long ChatId, long MessageId, string? Data);

/// <summary>One update from Telegram: either a message or a button press.</summary>
public sealed record Update(long Id, IncomingMessage? Message, ButtonPress? Press);

public interface ITelegramApi
{
    /// <summary>Long-polls for updates after <paramref name="offset"/>.</summary>
    Task<IReadOnlyList<Update>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellation);

    /// <summary>Sends an HTML-formatted message and returns its message id.</summary>
    Task<long> SendMessageAsync(long chatId, string html, IReadOnlyList<Button>? buttons, CancellationToken cancellation);

    Task AnswerPressAsync(string pressId, string? text, CancellationToken cancellation);

    /// <summary>Removes the buttons from a message, so an answered alert cannot be answered twice.</summary>
    Task ClearButtonsAsync(long chatId, long messageId, CancellationToken cancellation);
}
