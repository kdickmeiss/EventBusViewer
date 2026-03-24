using EventBusViewer.Tool.Services;
using Spectre.Console;

namespace EventBusViewer.Tool.Menus.Queue;

public sealed partial class QueueMenu(QueueService queueService)
{
    private const int PageSize = 5;

    private const string OptionOverview = "Overview";
    private const string OptionCreate = "Create";
    private const string OptionUpdate = "Update";
    private const string OptionDelete = "Delete";
    private const string OptionBack   = "← Back";

    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            AnsiConsole.Clear();

            string choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold deepskyblue1]Queue Management[/]")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(OptionOverview, OptionCreate, OptionUpdate, OptionDelete, OptionBack));

            switch (choice)
            {
                case OptionOverview:
                    await OverviewQueuesAsync(cancellationToken);
                    break;

                case OptionCreate:
                    await CreateQueueFlowAsync(cancellationToken);
                    break;

                case OptionUpdate:
                    await UpdateQueueFlowAsync(cancellationToken);
                    break;

                case OptionDelete:
                    await DeleteQueueFlowAsync(cancellationToken);
                    break;

                case OptionBack:
                    return;
            }
        }
    }
}

