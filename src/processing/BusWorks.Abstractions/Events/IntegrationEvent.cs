namespace BusWorks.Abstractions.Events;

public abstract record IntegrationEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;
