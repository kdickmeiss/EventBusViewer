using System.Globalization;
using Azure.Messaging.ServiceBus.Administration;
using BusWorks.Viewer.Console.Models;
using Spectre.Console;

namespace BusWorks.Viewer.Console.Menus.Queue;

public sealed partial class QueueMenu
{
    private async Task UpdateQueueFlowAsync(CancellationToken cancellationToken)
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
            AnsiConsole.MarkupLine("[bold deepskyblue1]Update Queue[/]\n");

            if (queues.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No queues found.[/]");
                Pause();
                return;
            }

            QueueMenu.RenderQueueTable(queues);

            string selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a [deepskyblue1]queue[/] to update:")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(queues.Select(q => q.Name).Append(OptionBack)));

            if (selected == OptionBack) return;

            QueueInfo info = queues.First(q => q.Name == selected);
            QueueProperties? props = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("deepskyblue1"))
                .StartAsync("[grey]Loading queue info…[/]", async _ =>
                {
                    props = await queueService.GetQueuePropertiesAsync(selected, cancellationToken);
                });

            AnsiConsole.Clear();
            QueueMenu.RenderQueueInfo(props!, info.ActiveMessageCount, info.DeadLetterMessageCount);

            const string renameLabel   = "✏  Rename";
            const string settingsLabel = "⚙  Edit Settings";

            string action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Update [deepskyblue1]{Markup.Escape(selected)}[/] — what would you like to change?")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(renameLabel, settingsLabel, OptionBack));

            if (action == OptionBack) continue;

            if (action == renameLabel)
                await RenameQueueAsync(selected, props!, cancellationToken);
            else
                await EditQueueSettingsAsync(props!, cancellationToken);
        }
    }

    private async Task RenameQueueAsync(
        string currentName,
        QueueProperties currentProps,
        CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold deepskyblue1]Rename Queue[/]  ·  [bold]{Markup.Escape(currentName)}[/]\n");

        AnsiConsole.Write(
            new Panel(
                "Renaming creates a new queue with the same settings and deletes the original.\n" +
                "[red]All existing messages will be permanently lost.[/]")
                .Header("[yellow bold] ⚠  Warning [/]")
                .Border(BoxBorder.Rounded)
                .Padding(1, 0));

        AnsiConsole.WriteLine();

        string newName;

        while (true)
        {
            string candidate = AnsiConsole.Prompt(
                new TextPrompt<string>("New queue [deepskyblue1]name[/]:")
                    .Validate(n => !string.IsNullOrWhiteSpace(n)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Name cannot be empty.[/]")));

            bool exists = false;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("deepskyblue1"))
                .StartAsync("[grey]Checking…[/]", async _ =>
                {
                    exists = await queueService.QueueExistsAsync(candidate, cancellationToken);
                });

            if (!exists)
            {
                newName = candidate;
                break;
            }

            AnsiConsole.MarkupLine($"\n[red]A queue named '[bold]{Markup.Escape(candidate)}[/]' already exists.[/]");

            if (!AnsiConsole.Confirm("Try a different name?"))
            {
                AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
                QueueMenu.Pause();
                return;
            }
        }

        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm(
                $"Rename '[bold]{Markup.Escape(currentName)}[/]' → '[bold]{Markup.Escape(newName)}[/]'?",
                defaultValue: false))
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            QueueMenu.Pause();
            return;
        }

        bool success = true;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("deepskyblue1"))
            .StartAsync("[grey]Renaming queue…[/]", async _ =>
            {
                try
                {
                    await queueService.CreateQueueAsync(
                        newName,
                        currentProps.RequiresSession,
                        currentProps.MaxDeliveryCount,
                        (int)currentProps.LockDuration.TotalSeconds,
                        currentProps.DeadLetteringOnMessageExpiration,
                        cancellationToken);

                    await queueService.DeleteQueueAsync(currentName, cancellationToken);
                }
                catch
                {
                    success = false;
                }
            });

        AnsiConsole.MarkupLine(success
            ? $"\n[green]✓ Queue renamed to '[bold]{Markup.Escape(newName)}[/]'.[/]"
            : $"\n[red]✗ Failed to rename queue. Please try again.[/]");

        QueueMenu.Pause();
    }

    private async Task EditQueueSettingsAsync(QueueProperties props, CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold deepskyblue1]Edit Settings[/]  ·  [bold]{Markup.Escape(props.Name)}[/]\n");

        int  origMaxDelivery = props.MaxDeliveryCount;
        int  origLockSeconds = (int)props.LockDuration.TotalSeconds;
        bool origDeadLetter  = props.DeadLetteringOnMessageExpiration;

        int maxDelivery = AnsiConsole.Prompt(
            new TextPrompt<int>("Max delivery count:")
                .DefaultValue(origMaxDelivery)
                .Validate(n => n > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be greater than 0.[/]")));

        int lockSeconds = AnsiConsole.Prompt(
            new TextPrompt<int>("Lock duration (seconds):")
                .DefaultValue(origLockSeconds)
                .Validate(n => n is > 0 and <= 300
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be between 1 and 300.[/]")));

        bool deadLetterOnExpiry = AnsiConsole.Confirm(
            "Dead-letter messages on expiration?",
            defaultValue: origDeadLetter);

        // Review: old vs new
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine(
            $"[bold deepskyblue1]Edit Settings[/]  ·  [bold]{Markup.Escape(props.Name)}[/]  —  [grey]Review[/]\n");

        Table preview = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("")
            .AddColumn(new TableColumn("[grey]Current[/]").Centered())
            .AddColumn(new TableColumn("[bold]New[/]").Centered());

        static string Diff(string updated, bool changed) =>
            changed ? $"[green]{updated}[/]" : updated;

        preview.AddRow(
            "[grey]Max Delivery Count[/]",
            origMaxDelivery.ToString(CultureInfo.InvariantCulture),
            Diff(
                maxDelivery.ToString(CultureInfo.InvariantCulture),
                origMaxDelivery != maxDelivery));

        preview.AddRow(
            "[grey]Lock Duration[/]",
            string.Create(CultureInfo.InvariantCulture, $"{origLockSeconds}s"),
            Diff(
                string.Create(CultureInfo.InvariantCulture, $"{lockSeconds}s"),
                origLockSeconds != lockSeconds));

        preview.AddRow(
            "[grey]Dead-Letter on Expiry[/]",
            origDeadLetter     ? "Yes" : "No",
            Diff(
                deadLetterOnExpiry ? "Yes" : "No",
                origDeadLetter != deadLetterOnExpiry));

        AnsiConsole.Write(preview);
        AnsiConsole.MarkupLine("\n[grey]Changed values are highlighted in [green]green[/].[/]\n");

        if (!AnsiConsole.Confirm("Apply these changes?"))
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            QueueMenu.Pause();
            return;
        }

        bool success = true;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("deepskyblue1"))
            .StartAsync("[grey]Updating queue…[/]", async _ =>
            {
                try
                {
                    props.MaxDeliveryCount                 = maxDelivery;
                    props.LockDuration                     = TimeSpan.FromSeconds(lockSeconds);
                    props.DeadLetteringOnMessageExpiration = deadLetterOnExpiry;

                    await queueService.UpdateQueueAsync(props, cancellationToken);
                }
                catch
                {
                    success = false;
                }
            });

        AnsiConsole.MarkupLine(success
            ? $"\n[green]✓ Queue '[bold]{Markup.Escape(props.Name)}[/]' updated.[/]"
            : $"\n[red]✗ Failed to update queue. Please try again.[/]");

        QueueMenu.Pause();
    }
}




