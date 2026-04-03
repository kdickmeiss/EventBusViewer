namespace BusWorks.BackgroundServices;

internal record ServiceBusEndpoint(
    string QueueOrTopicName,
    string? SubscriptionName = null,
    bool RequireSession = false,
    int MaxDeliveryCount = 5)
{
    public bool IsQueue => SubscriptionName is null;
    public bool IsTopic => SubscriptionName is not null;
}
