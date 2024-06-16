namespace FoulBot.Domain;

public static class RespondStrategyExtensions
{
    public static bool ShouldRespond(this IRespondStrategy strategy, FoulMessage message)
        => strategy.GetReasonForResponding(message) != null;
}
