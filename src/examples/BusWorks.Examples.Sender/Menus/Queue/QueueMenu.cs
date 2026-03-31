using BusWorks.Examples.Sender.Services;
using Spectre.Console;

namespace BusWorks.Examples.Sender.Menus.Queue;

internal sealed partial class QueueMenu(QueueService queueService)
{
    private const string OptionPreDefinedMessages = "Send Pre-Defined Messages";
    private const string OptionCustomMessage      = "Send Custom Message";
    private const string QueueName = "parking-spot-reserved";
    private const string OptionBack   = "← Back";

    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            AnsiConsole.Clear();

            AnsiConsole.Write(
                new FigletText("BusWorks")
                    .Centered()
                    .Color(Color.DeepSkyBlue1));

            AnsiConsole.WriteLine();

            string choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]What would you like to do?[/]")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(OptionPreDefinedMessages, OptionCustomMessage, OptionBack));

            switch (choice)
            {
                case OptionPreDefinedMessages:
                    await SendPredefinedMessage(cancellationToken);
                    break;
                
                case OptionCustomMessage:
                    await SendCustomMessage(cancellationToken);
                    break;
                
                case OptionBack:
                    return;
            }
        }
    }
    
    private static void Pause()
    {
        AnsiConsole.MarkupLine("\n[grey]Press any key to continue…[/]");
        Console.ReadKey(intercept: true);
    }
}
