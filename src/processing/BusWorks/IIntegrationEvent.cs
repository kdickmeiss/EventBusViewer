namespace BusWorks;

public interface IIntegrationEvent
{
    Guid Id { get; }

    DateTime OccurredOnUtc { get; }

}
