namespace BusWorks;

public abstract record IntegrationEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;

