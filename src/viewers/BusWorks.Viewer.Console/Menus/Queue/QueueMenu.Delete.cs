using System.Globalization;
using BusWorks.Viewer.Console.Models;
using Spectre.Console;

namespace BusWorks.Viewer.Console.Menus.Queue;

public sealed partial class QueueMenu
{
    private async Task DeleteQueueFlowAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<QueueInfo> queues = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("deepskyblue1"))
                .StartAsync("[grey]Fetching queues…[/]", async _ =>
                {
                    queues = await queueService.GetAllQueuesAsync(cancellationToken);
                });

            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[bold deepskyblue1]Delete Queue[/]\n");

            if (queues.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No queues found.[/]");
                QueueMenu.Pause();
                return;
            }

            QueueMenu.RenderQueueTable(queues);

            string selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a [red]queue[/] to delete:")
                    .HighlightStyle(new Style(Color.Red, decoration: Decoration.Bold))
                    .AddChoices(queues.Select(q => q.Name).Append(OptionBack)));

            if (selected == OptionBack) return;

            // Confirmation warning
            AnsiConsole.Clear();

            AnsiConsole.Write(
                new Panel(
                    string.Create(CultureInfo.InvariantCulture,
                        $"You are about to permanently delete the queue:\n\n  [bold]{Markup.Escape(selected)}[/]\n\n" +
                        $"[red]This action cannot be undone.[/] All messages in the queue will be lost."))
                    .Header("[red bold] ⚠  Confirm Deletion [/]")
                    .Border(BoxBorder.Rounded)
                    .Padding(1, 0));

            AnsiConsole.WriteLine();

            if (!AnsiConsole.Confirm($"[red]Delete[/] '[bold]{Markup.Escape(selected)}[/]'?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
                QueueMenu.Pause();
                continue;
            }

            bool success = true;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("red"))
                .StartAsync("[grey]Deleting queue…[/]", async _ =>
                {
                    try
                    {
                        await queueService.DeleteQueueAsync(selected, cancellationToken);
                    }
                    catch
                    {
                        success = false;
                    }
                });

            if (success)
            {
                AnsiConsole.MarkupLine(string.Create(CultureInfo.InvariantCulture,
                    $"\n[green]✓ Queue '[bold]{Markup.Escape(selected)}[/]' deleted.[/]"));
            }
            else
            {
                AnsiConsole.MarkupLine(string.Create(CultureInfo.InvariantCulture,
                    $"\n[red]✗ Failed to delete queue '[bold]{Markup.Escape(selected)}[/]'. Please try again.[/]"));
            }

            QueueMenu.Pause();
            // Loop back to the queue list so the user can delete another
        }
    }
}

