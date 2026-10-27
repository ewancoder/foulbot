using PidorBot.AI;
using PidorBot.Brain;
using PidorBot.Reminders;
using PidorBot.Storage;

namespace PidorBot.Chats;

/// <summary>
/// Everything a chat session needs, so sessions are easy to create.
/// </summary>
public sealed record ChatServices(
    ILoggerFactory Loggers,
    IMessenger Messenger,
    IChatStore Store,
    Responder Responder,
    MoodKeeper Moods,
    MemoryKeeper Memory,
    ReminderBook Reminders,
    IStickerBook Stickers,
    IImageGenerator Images,
    IPacing Pacing,
    IVoice Voice,
    Settings Settings,
    BotIdentity Bot,
    TimeProvider Time,
    Random Random);
