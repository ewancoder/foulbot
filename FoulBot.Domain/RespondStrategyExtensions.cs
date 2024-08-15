namespace FoulBot.Domain;

public static class RespondStrategyExtensions
{
    public static bool ShouldRespond(this IMessageRespondStrategy strategy, IEnumerable<FoulMessage> snapshot)
        => snapshot.Any(strategy.ShouldRespond);

    public static bool ShouldRespond(this IMessageRespondStrategy strategy, FoulMessage message)
        => strategy.GetReasonForResponding(message) != null;
}
