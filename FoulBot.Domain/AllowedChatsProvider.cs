using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FoulBot.Domain;

namespace FoulBot.Domain;

public interface IAllowedChatsProvider
{
    bool IsAllowedChat(FoulChatId chatId);
    void AddAllowedChat(FoulChatId chatId);
}

public sealed class AllowedChatsProvider : IAllowedChatsProvider
{
    private const string FileName = "allowed_chats";
    private readonly object _lock = new object();
    private readonly List<string> _allowedChats;

    public AllowedChatsProvider()
    {
        if (!File.Exists(FileName))
            File.WriteAllText(FileName, "[]");

        var content = File.ReadAllText(FileName);
        _allowedChats = JsonSerializer.Deserialize<List<string>>(content)!;
    }

    public void AddAllowedChat(FoulChatId chatId)
    {
        lock (_lock)
        {
            _allowedChats.Add(chatId.ToString());
            File.WriteAllText(FileName, JsonSerializer.Serialize(_allowedChats));
        }
    }

    public bool IsAllowedChat(FoulChatId chatId)
    {
        lock (_lock)
        {
            return _allowedChats.Contains(chatId.ToString());
        }
    }
}
