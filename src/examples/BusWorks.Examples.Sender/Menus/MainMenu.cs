using BusWorks.Examples.Sender.Menus.Messaging;
using Spectre.Console;

namespace BusWorks.Examples.Sender.Menus;

internal sealed class MainMenu(MessagingMenu messagingMenu)
{
    private const string OptionPreDefinedMessages = "Send Pre-Defined Messages";
    private const string OptionCustomMessage      = "Send Custom Message";
    private const string OptionExit   = "Exit";
    
    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            AnsiConsole.Clear();

            AnsiConsole.Write(
                new FigletText("BusWorks Sender")
                    .Centered()
                    .Color(Color.DeepSkyBlue1));

            AnsiConsole.WriteLine();

            string choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]What would you liek to do?[/]")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(OptionPreDefinedMessages, OptionCustomMessage, OptionExit));

            switch (choice)
            {
                case OptionPreDefinedMessages:
                    await messagingMenu.SendPredefinedMessages(cancellationToken);
                    break;
                //
                // case OptionTopics:
                //     AnsiConsole.MarkupLine("[yellow]Topic Management is not implemented yet.[/]");
                //     AnsiConsole.MarkupLine("\n[grey]Press any key to continue…[/]");
                //     System.Console.ReadKey(intercept: true);
                //     break;

                case OptionExit:
                    AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                    return;
            }
        }
    }
}
