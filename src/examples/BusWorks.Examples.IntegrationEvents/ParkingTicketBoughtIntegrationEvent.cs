using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Events;

namespace BusWorks.Examples.IntegrationEvents;

[TopicRoute("parking-ticket-bought")]
public record ParkingTicketBoughtIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string LicensePlate,
    DateOnly PurchaseDate) : IntegrationEvent(Id, OccurredOnUtc);
