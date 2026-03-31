using System.Text.Json;
using BusWorks.Examples.IntegrationEvents;
using Spectre.Console;

namespace BusWorks.Examples.Sender.Menus.Queue;

internal sealed partial class QueueMenu
{
    private async Task SendCustomMessage(CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold deepskyblue1]Message for Queue:[/]  ·  [bold]{Markup.Escape(QueueName)}[/]\n");

        string licensePlate = AnsiConsole.Prompt(
            new TextPrompt<string>("License plate:")
                .DefaultValue("GKL-22-P")
                .Validate(n => !string.IsNullOrWhiteSpace(n)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]License plate cannot be empty.[/]")));

        int reservedDaysFromToday = AnsiConsole.Prompt(
            new TextPrompt<int>("Reserved days from today:")
                .DefaultValue(12)
                .Validate(n => n > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be greater than 0.[/]")));

        string messageBody = JsonSerializer.Serialize(new ParkingSpotReservedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow,
            licensePlate, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(reservedDaysFromToday))));
        await queueService.SendMessageAsync(QueueName, messageBody, cancellationToken);
    }
}
