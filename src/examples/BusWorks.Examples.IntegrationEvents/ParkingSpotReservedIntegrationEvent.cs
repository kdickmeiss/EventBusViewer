using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Events;

namespace BusWorks.Examples.IntegrationEvents;

[QueueRoute("parking-spot-reserved")]
public record ParkingSpotReservedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string LicensePlate,
    DateOnly ReservedUntil) : IntegrationEvent(Id, OccurredOnUtc);
