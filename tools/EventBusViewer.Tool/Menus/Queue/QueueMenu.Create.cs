using System.Globalization;
using Spectre.Console;

namespace EventBusViewer.Tool.Menus.Queue;

public sealed partial class QueueMenu
{
    private async Task CreateQueueFlowAsync(CancellationToken cancellationToken)
    {
        // Step 1: valid, non-existing name (handles its own retry loop).
        string? name = await PromptNewQueueNameAsync(cancellationToken);
        if (name is null) return;

        // Step 2: settings.
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold deepskyblue1]Create Queue[/]  ·  [bold]{Markup.Escape(name)}[/]\n");

        bool requiresSession = AnsiConsole.Confirm("Require sessions?", defaultValue: false);

        int maxDeliveryCount = AnsiConsole.Prompt(
            new TextPrompt<int>("Max delivery count:")
                .DefaultValue(10)
                .Validate(n => n > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be greater than 0.[/]")));

        int lockSeconds = AnsiConsole.Prompt(
            new TextPrompt<int>("Lock duration (seconds):")
                .DefaultValue(60)
                .Validate(n => n is > 0 and <= 300
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be between 1 and 300.[/]")));

        bool deadLetterOnExpiry = AnsiConsole.Confirm("Dead-letter messages on expiration?", defaultValue: false);

        // Step 3: preview + confirm.
        if (!ShowCreatePreviewAndConfirm(name, requiresSession, maxDeliveryCount, lockSeconds, deadLetterOnExpiry))
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            QueueMenu.Pause();
            return;
        }

        // Step 4: create.
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("deepskyblue1"))
            .StartAsync("[grey]Creating queue…[/]", async _ =>
            {
                await queueService.CreateQueueAsync(
                    name, requiresSession, maxDeliveryCount, lockSeconds, deadLetterOnExpiry, cancellationToken);
            });

        AnsiConsole.MarkupLine(string.Create(CultureInfo.InvariantCulture,
            $"\n[green]✓ Queue '[bold]{Markup.Escape(name)}[/]' created.[/]"));
        QueueMenu.Pause();
    }

    // Loops until the user enters a name that does not yet exist, or cancels.
    private async Task<string?> PromptNewQueueNameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[bold deepskyblue1]Create Queue[/]\n");

            string name = AnsiConsole.Prompt(
                new TextPrompt<string>("Queue [deepskyblue1]name[/]:")
                    .Validate(n => !string.IsNullOrWhiteSpace(n)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Name cannot be empty.[/]")));

            bool exists = false;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("deepskyblue1"))
                .StartAsync("[grey]Checking…[/]", async _ =>
                {
                    exists = await queueService.QueueExistsAsync(name, cancellationToken);
                });

            if (!exists) return name;

            AnsiConsole.MarkupLine(string.Create(CultureInfo.InvariantCulture,
                $"\n[red]A queue named '[bold]{Markup.Escape(name)}[/]' already exists.[/]"));

            if (!AnsiConsole.Confirm("Try a different name?")) return null;
        }
    }

    private static bool ShowCreatePreviewAndConfirm(
        string name,
        bool requiresSession,
        int maxDeliveryCount,
        int lockSeconds,
        bool deadLetterOnExpiry)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[bold deepskyblue1]Create Queue[/]  —  Review\n");

        Table preview = new Table()
            .Border(TableBorder.Rounded)
            .HideHeaders()
            .AddColumn("")
            .AddColumn("");

        preview.AddRow("[grey]Name[/]",                    $"[bold]{Markup.Escape(name)}[/]");
        preview.AddRow("[grey]Requires Session[/]",        requiresSession    ? "[green]Yes[/]" : "No");
        preview.AddRow("[grey]Max Delivery Count[/]",      maxDeliveryCount.ToString(CultureInfo.InvariantCulture));
        preview.AddRow("[grey]Lock Duration[/]",           $"{lockSeconds}s");
        preview.AddRow("[grey]Dead-Letter on Expiry[/]",   deadLetterOnExpiry ? "[yellow]Yes[/]" : "No");

        AnsiConsole.Write(preview);
        AnsiConsole.WriteLine();

        return AnsiConsole.Confirm("Create this queue?");
    }
}

