
namespace FoulBot.Domain.Features;

public sealed class CustomNamesFeature : BotFeature
{
    private readonly INamesStorage _namesStorage;

    public CustomNamesFeature(INamesStorage namesStorage)
    {
        _namesStorage = namesStorage;
    }

    public override async ValueTask<bool> ProcessMessageAsync(FoulMessage message)
    {
        var name = CutKeyword(message.Text, "my name is ")
            ?? CutKeyword(message.Text, "меня зовут ");

        if (name is not null && message.Sender.UserId is not null)
        {
            await _namesStorage.SetNameAsync(message.Sender.UserId, name);

            return true;
        }

        return false;
    }

    public override ValueTask StopFeatureAsync()
    {
        return default;
    }
}
