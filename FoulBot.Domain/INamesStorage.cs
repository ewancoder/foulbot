namespace FoulBot.Domain;

public interface INamesStorage
{
    ValueTask<string?> GetNameAsync(string nickname);
    ValueTask SetNameAsync(string nickname, string name);
}
