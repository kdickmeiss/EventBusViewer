// using System.Text.Json;
// using BusWorks.Examples.IntegrationEvents;
// using Spectre.Console;
//
// namespace BusWorks.Examples.Sender.Menus.Queue;
//
// internal sealed partial class QueueMenu
// {
//     public async Task SendPredefinedMessage(CancellationToken cancellationToken)
//     {
//         AnsiConsole.Clear();
//         AnsiConsole.MarkupLine(
//             $"[bold deepskyblue1]Send Pre Defined Message[/]  ·  [bold]{Markup.Escape(QueueName)}[/]\n");
//         AnsiConsole.MarkupLine("[grey]Paste or type your JSON below.[/]");
//         AnsiConsole.MarkupLine(
//             "[grey]Press [bold]Enter[/] on an empty line to continue, or type [red]cancel[/] to abort.[/]\n");
//
//         var parkingSpot = new ParkingSpotReserved(
//             Guid.NewGuid(),
//             DateTime.UtcNow,
//             "GKL-22-P",
//             DateOnly.FromDateTime(DateTime.UtcNow.AddDays(12)
//             ));
//
//         await AnsiConsole.Status()
//             .Spinner(Spinner.Known.Dots)
//             .SpinnerStyle(Style.Parse("deepskyblue1"))
//             .StartAsync("[grey]Sending…[/]", async _ =>
//             {
//                 await queueService.SendMessageAsync(QueueName, JsonSerializer.Serialize(parkingSpot),
//                     cancellationToken);
//             });
//
//         AnsiConsole.MarkupLine("\n[green]✓ Message sent.[/]");
//         Pause();
//     }
// }
