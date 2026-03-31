using BusWorks.Abstractions;

namespace BusWorks.Examples.IntegrationEvents;

public record ParkingSpotReservedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string LicensePlate,
    DateOnly ReservedUntil) : IntegrationEvent(Id, OccurredOnUtc);
