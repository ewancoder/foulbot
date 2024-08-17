namespace FoulBot.Domain;

public interface IAllowedChatsProvider
{
    bool IsAllowedChat(FoulChatId chatId);
    void AddAllowedChat(FoulChatId chatId);
    void RemoveAllowedChat(FoulChatId chatId);
}

public sealed class AllowedChatsProvider : IAllowedChatsProvider
{
    private readonly string _fileName;
    private readonly object _lock = new();
    private readonly List<string> _allowedChats;

    public AllowedChatsProvider(string fileName = "allowed_chats")
    {
        _fileName = fileName;
        if (!File.Exists(_fileName))
            File.WriteAllText(_fileName, "[]");

        var content = File.ReadAllText(_fileName);
        _allowedChats = JsonSerializer.Deserialize<List<string>>(content)!;
    }

    public void AddAllowedChat(FoulChatId chatId)
    {
        lock (_lock)
        {
            _allowedChats.Add(chatId.ToString());
            File.WriteAllText(_fileName, JsonSerializer.Serialize(_allowedChats));
        }
    }

    public void RemoveAllowedChat(FoulChatId chatId)
    {
        lock (_lock)
        {
            _allowedChats.Remove(chatId.ToString());
            File.WriteAllText(_fileName, JsonSerializer.Serialize(_allowedChats));
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
