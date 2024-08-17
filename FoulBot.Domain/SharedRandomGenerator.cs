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
#pragma warning disable CA5394 // Do not use insecure randomness
        return _random.Next(minInclusive, maxInclusive + 1);
#pragma warning restore CA5394 // Do not use insecure randomness
    }
}
