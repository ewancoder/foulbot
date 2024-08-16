namespace FoulBot.Domain;

public interface ISharedRandomGenerator
{
    int Generate(int minInclusive, int maxInclusive);
}

public sealed class SharedRandomGenerator : ISharedRandomGenerator
{
    private readonly Random _random = Random.Shared;

    public int Generate(int minInclusive, int maxInclusive)
    {
        return _random.Next(minInclusive, maxInclusive + 1);
    }
}
