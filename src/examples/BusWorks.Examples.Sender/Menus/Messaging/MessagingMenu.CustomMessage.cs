using System.Text.Json;
using BusWorks.Examples.IntegrationEvents;
using Spectre.Console;

namespace BusWorks.Examples.Sender.Menus.Messaging;

internal sealed partial class MessagingMenu
{
    private async Task SendCustomMessageAsync(bool forQueue, CancellationToken cancellationToken)
    {
        string destination = forQueue ? QueueName : TopicName;
        string eventLabel  = forQueue ? "Parking Spot Reserved" : "Parking Ticket Bought";

        AnsiConsole.Clear();
        RenderHeader(forQueue, "Custom Message");
        AnsiConsole.MarkupLine($"[grey]Event type:[/] [bold]{eventLabel}[/]\n");

        string licensePlate = await AnsiConsole.PromptAsync(
            new TextPrompt<string>("License plate:")
                .DefaultValue("GKL-22-P")
                .Validate(v => !string.IsNullOrWhiteSpace(v)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]License plate cannot be empty.[/]")),
            cancellationToken);

        string messageBody;

        if (forQueue)
        {
            int reservedDays = await AnsiConsole.PromptAsync(
                new TextPrompt<int>("Reserved days from today:")
                    .DefaultValue(12)
                    .Validate(v => v > 0
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Must be greater than 0.[/]")),
                cancellationToken);

            messageBody = await SendCustomEventAsync(
                new ParkingSpotReservedIntegrationEvent(
                    Guid.NewGuid(),
                    DateTime.UtcNow,
                    licensePlate,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(reservedDays))));
        }
        else
        {
            int daysOffset = await AnsiConsole.PromptAsync(
                new TextPrompt<int>("Purchase date offset (days from today):")
                    .DefaultValue(0)
                    .Validate(v => v >= 0
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Must be 0 or greater.[/]")),
                cancellationToken);

            messageBody = await SendCustomEventAsync(
                new ParkingTicketBoughtIntegrationEvent(
                    Guid.NewGuid(),
                    DateTime.UtcNow,
                    licensePlate,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysOffset))));
        }

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

    private async Task<string> SendCustomEventAsync(dynamic @event)
    {
        await publisher.PublishAsync(@event);
        return JsonSerializer.Serialize(@event, PrettyPrint);
    }
}

