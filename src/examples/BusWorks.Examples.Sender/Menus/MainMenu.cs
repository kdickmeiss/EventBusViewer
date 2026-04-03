
using BusWorks.Examples.Sender.Menus.Messaging;
using Spectre.Console;

namespace BusWorks.Examples.Sender.Menus;

internal sealed class MainMenu(MessagingMenu messagingMenu)
{
    private const string OptionQueues = "Queue Management";
    private const string OptionTopics = "Topic Management";
    
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

            string choice = await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    .Title("[bold]What would you like to do?[/]")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(OptionQueues, OptionTopics, OptionExit), cancellationToken);        

            switch (choice)
            {
                case OptionQueues:
                    await messagingMenu.ShowAsync(true, cancellationToken);
                    break;

                case OptionTopics:
                    await messagingMenu.ShowAsync(false, cancellationToken);
                    break;
                
                case OptionExit:
                    AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                    return;
            }
        }
    }
}
