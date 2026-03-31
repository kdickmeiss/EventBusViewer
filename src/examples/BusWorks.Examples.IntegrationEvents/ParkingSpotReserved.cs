using BusWorks.Abstractions;

namespace BusWorks.Examples.IntegrationEvents;

public sealed record ParkingSpotReserved(Guid Id, DateTime OccurredOnUtc, string LicensePlate, DateOnly ArrivalDate)
    : IntegrationEvent(Id, OccurredOnUtc);
