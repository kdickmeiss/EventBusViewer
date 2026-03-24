using System.Globalization;
using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace EventBusViewer.Tool.Menus.Queue;

public sealed partial class QueueMenu
{
    private async Task SendMessageToQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold deepskyblue1]Send Message[/]  ·  [bold]{Markup.Escape(queueName)}[/]\n");
        AnsiConsole.MarkupLine("[grey]Paste or type your JSON below.[/]");
        AnsiConsole.MarkupLine("[grey]Press [bold]Enter[/] on an empty line to continue, or type [red]cancel[/] to abort.[/]\n");

        string? json = ReadMultilineJson();
        if (json is null)
        {
            AnsiConsole.MarkupLine("\n[grey]Cancelled.[/]");
            Pause();
            return;
        }

        // Validate and pretty-print for the preview.
        string formatted;
        try
        {
            using var doc = JsonDocument.Parse(json);
            formatted = JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine(string.Create(CultureInfo.InvariantCulture,
                $"\n[red]Invalid JSON:[/] {Markup.Escape(ex.Message)}"));
            Pause();
            return;
        }

        // Preview + confirm.
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold deepskyblue1]Send Message[/]  ·  [bold]{Markup.Escape(queueName)}[/]\n");
        AnsiConsole.Write(
            new Panel(Markup.Escape(formatted))
                .Header("[deepskyblue1] Preview [/]")
                .Border(BoxBorder.Rounded));
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm("Send this message to the queue?"))
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            Pause();
            return;
        }

        // Send.
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("deepskyblue1"))
            .StartAsync("[grey]Sending…[/]", async _ =>
            {
                await queueService.SendMessageAsync(queueName, json, cancellationToken);
            });

        AnsiConsole.MarkupLine("\n[green]✓ Message sent.[/]");
        Pause();
    }

    // Reads lines from stdin until an empty line is entered.
    // Returns null if the user types "cancel" or enters nothing at all.
    private static string? ReadMultilineJson()
    {
        var sb = new StringBuilder();

        while (true)
        {
            string? line = Console.ReadLine();

            if (line is null || line.Trim().Equals("cancel", StringComparison.OrdinalIgnoreCase))
                return null;

            if (line == string.Empty)
                break;

            sb.AppendLine(line);
        }

        string result = sb.ToString().Trim();
        return result.Length == 0 ? null : result;
    }
}

