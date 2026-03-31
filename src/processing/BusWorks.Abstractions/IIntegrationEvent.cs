namespace BusWorks.Abstractions;

public interface IIntegrationEvent
{
    Guid Id { get; }

    DateTime OccurredOnUtc { get; }
}

