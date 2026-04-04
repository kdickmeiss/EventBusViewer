using BusWorks.Abstractions;
using BusWorks.Examples.Sender.Services;
using Spectre.Console;

namespace BusWorks.Examples.Sender.Menus.Messaging;

internal sealed partial class MessagingMenu(
    MessagingService messagingService,
    IEventBusPublisher publisher
    )
{
    private const string QueueName = "parking-spot-reserved";
    private const string TopicName = "parking-ticket-bought";

    private const string OptionSendPredefined = "Send Pre-Defined Message";
    private const string OptionSendCustom     = "Send Custom Message";
    private const string OptionBack           = "← Back";

    public async Task ShowAsync(bool forQueue = true, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            AnsiConsole.Clear();
            RenderHeader(forQueue);

            string choice = await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    .Title("[bold]What would you like to do?[/]")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(OptionSendPredefined, OptionSendCustom, OptionBack),
                cancellationToken);

            switch (choice)
            {
                case OptionSendPredefined:
                    await SendPredefinedMessageAsync(forQueue, cancellationToken);
                    break;

                case OptionSendCustom:
                    await SendCustomMessageAsync(forQueue, cancellationToken);
                    break;

                case OptionBack:
                    return;
            }
        }
    }

    private static void RenderHeader(bool forQueue, string? subtitle = null)
    {
        AnsiConsole.Write(
            new FigletText("BusWorks Sender")
                .Centered()
                .Color(Color.DeepSkyBlue1));

        AnsiConsole.WriteLine();

        string modeLabel  = forQueue ? "[deepskyblue1]Queue[/]" : "[mediumpurple1]Topic[/]";
        string destination = forQueue ? QueueName : TopicName;
        string line       = $"  Destination: {modeLabel}  ·  [bold]{Markup.Escape(destination)}[/]";

        if (subtitle is not null)
            line += $"  ·  [grey]{Markup.Escape(subtitle)}[/]";

        AnsiConsole.MarkupLine(line + "\n");
    }

    private async Task SendWithFeedbackAsync(
        string destination,
        string messageBody,
        CancellationToken cancellationToken)
    {
        Exception? error = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("deepskyblue1"))
            .StartAsync("[grey]Sending…[/]", async _ =>
            {
                try
                {
                    await messagingService.SendMessageAsync(destination, messageBody, cancellationToken);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

        if (error is not null)
            AnsiConsole.MarkupLine($"[red]✗ Failed:[/] [grey]{Markup.Escape(error.Message)}[/]");
        else
            AnsiConsole.MarkupLine("[green]✓ Message sent successfully.[/]");

        Pause();
    }

    private static void Pause()
    {
        AnsiConsole.MarkupLine("\n[grey]Press any key to continue…[/]");
        Console.ReadKey(intercept: true);
    }
}

