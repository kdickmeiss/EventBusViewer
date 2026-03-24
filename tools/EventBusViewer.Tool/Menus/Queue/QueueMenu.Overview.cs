using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using EventBusViewer.Tool.Models;
using Spectre.Console;

namespace EventBusViewer.Tool.Menus.Queue;

public sealed partial class QueueMenu
{
    private async Task OverviewQueuesAsync(CancellationToken cancellationToken)
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

            if (queues.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No queues found.[/]");
                QueueMenu.Pause();
                return;
            }

            QueueMenu.RenderQueueTable(queues);

            string selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a [deepskyblue1]queue[/] to browse:")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(queues.Select(q => q.Name).Append(OptionBack)));

            if (selected == OptionBack) return;

            QueueInfo selectedQueue = queues.First(q => q.Name == selected);

            await FetchAndRenderQueueInfoAsync(selected, selectedQueue, cancellationToken);

            string activeLabel     = $"Active ({selectedQueue.ActiveMessageCount})";
            string deadLetterLabel = $"Dead Letter ({selectedQueue.DeadLetterMessageCount})";
            const string sendLabel = "✉  Send Message";

            string subChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Browse [deepskyblue1]{Markup.Escape(selected)}[/] — what would you like to do?")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(activeLabel, deadLetterLabel, sendLabel, OptionBack));

            if (subChoice == OptionBack) continue;

            if (subChoice == sendLabel)
            {
                await SendMessageToQueueAsync(selected, cancellationToken);
                continue;
            }

            bool fromDeadLetter = subChoice == deadLetterLabel;
            await ShowQueueBrowseAsync(selected, fromDeadLetter, cancellationToken);
        }
    }

    private async Task ShowQueueBrowseAsync(
        string queueName,
        bool fromDeadLetter,
        CancellationToken cancellationToken)
    {
        var history = new Stack<long>();
        long fromSeq = 0;

        while (true)
        {
            IReadOnlyList<ServiceBusReceivedMessage> messages = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("deepskyblue1"))
                .StartAsync("[grey]Loading messages…[/]", async _ =>
                {
                    messages = await queueService.PeekMessagesPagedAsync(
                        queueName, PageSize, fromSeq, fromDeadLetter, cancellationToken);
                });

            AnsiConsole.Clear();
            QueueMenu.RenderBrowseScreen(queueName, fromDeadLetter, fromSeq, messages);

            long? next = QueueMenu.NavigatePage(messages, history, fromSeq);
            if (next is null) return;
            fromSeq = next.Value;
        }
    }

    private async Task FetchAndRenderQueueInfoAsync(
        string queueName,
        QueueInfo counts,
        CancellationToken cancellationToken)
    {
        QueueProperties? props = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("deepskyblue1"))
            .StartAsync("[grey]Loading queue info…[/]", async _ =>
            {
                props = await queueService.GetQueuePropertiesAsync(queueName, cancellationToken);
            });

        AnsiConsole.Clear();

        if (props is not null)
            QueueMenu.RenderQueueInfo(props, counts.ActiveMessageCount, counts.DeadLetterMessageCount);
    }
}

