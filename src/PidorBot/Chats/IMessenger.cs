namespace PidorBot.Chats;

public interface IMessenger
{
    Task SendTextAsync(ChatRef chat, string text, CancellationToken cancellationToken);
    Task SendVoiceAsync(ChatRef chat, Stream voice, CancellationToken cancellationToken);
    Task SendStickerAsync(ChatRef chat, string stickerId, CancellationToken cancellationToken);
    Task SendPhotoAsync(ChatRef chat, byte[] photo, string? caption, CancellationToken cancellationToken);
    Task ReactAsync(ChatRef chat, int messageId, string emoji, CancellationToken cancellationToken);
    Task NotifyTypingAsync(ChatRef chat, bool isVoice, CancellationToken cancellationToken);
    Task<byte[]> DownloadAsync(string fileId, CancellationToken cancellationToken);
}

/// <summary>
/// The bot can't write to the chat: it was kicked, or the chat doesn't exist anymore.
/// </summary>
public sealed class ChatUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
