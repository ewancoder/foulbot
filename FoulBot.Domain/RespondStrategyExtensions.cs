namespace FoulBot.Domain;

public static class RespondStrategyExtensions
{
    public static bool ShouldRespond(this IMessageRespondStrategy strategy, FoulMessage message)
        => strategy.GetReasonForResponding(message) != null;
}
