using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;

namespace BusWorks.Examples.IntegrationEvents;

[TopicRoute("parking-ticket-bought")]
public record ParkingTicketBoughtIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string LicensePlate,
    DateOnly PurchaseDate) : IntegrationEvent(Id, OccurredOnUtc);
