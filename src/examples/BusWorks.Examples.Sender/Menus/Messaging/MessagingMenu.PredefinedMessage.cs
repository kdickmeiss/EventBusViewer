using System.Text.Json;
using System.Text.Json.Serialization;
using BusWorks.Examples.IntegrationEvents;
using Spectre.Console;

namespace BusWorks.Examples.Sender.Menus.Messaging;

internal sealed partial class MessagingMenu
{
    private static readonly JsonSerializerOptions PrettyPrint = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private async Task SendPredefinedMessageAsync(bool forQueue, CancellationToken cancellationToken)
    {
        string destination = forQueue ? QueueName : TopicName;

        AnsiConsole.Clear();
        RenderHeader(forQueue, "Pre-Defined Message");

        string messageBody = await SendMessage(forQueue);

        AnsiConsole.Write(new Panel(messageBody)
        {
            Header      = new PanelHeader(" [grey]Message Preview[/] "),
            Border      = BoxBorder.Rounded,
            BorderStyle = Style.Parse("grey"),
            Padding     = new Padding(1, 0)
        });
        AnsiConsole.WriteLine();

        await SendWithFeedbackAsync(destination, messageBody, cancellationToken);
    }

    private async Task<string> SendMessage(bool forQueue)
    {
        dynamic @event = forQueue
            ? new ParkingSpotReservedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                "GKL-22-P",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(12)))
            : new ParkingTicketBoughtIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                "GKL-22-P",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(12)));

        await publisher.PublishAsync(@event);
        return JsonSerializer.Serialize(@event, PrettyPrint);
    }
}
