using Spectre.Console;

namespace BusWorks.Viewer.Console.Menus;

public sealed class MainMenu(Queue.QueueMenu queueMenu)
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
                new FigletText("BusWorks")
                    .Centered()
                    .Color(Color.DeepSkyBlue1));

            AnsiConsole.WriteLine();

            string choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]What would you like to manage?[/]")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(OptionQueues, OptionTopics, OptionExit));

            switch (choice)
            {
                case OptionQueues:
                    await queueMenu.ShowAsync(cancellationToken);
                    break;

                case OptionTopics:
                    AnsiConsole.MarkupLine("[yellow]Topic Management is not implemented yet.[/]");
                    AnsiConsole.MarkupLine("\n[grey]Press any key to continue…[/]");
                    System.Console.ReadKey(intercept: true);
                    break;

                case OptionExit:
                    AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                    return;
            }
        }
    }
}
