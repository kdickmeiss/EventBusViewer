using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using EventBusViewer.Tool.Models;
using Spectre.Console;

namespace EventBusViewer.Tool.Menus.Queue;

public sealed partial class QueueMenu
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    private static void RenderQueueTable(IReadOnlyList<QueueInfo> queues)
    {
        Table table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Queues[/]")
            .AddColumn(new TableColumn("[bold]Name[/]").NoWrap())
            .AddColumn(new TableColumn("[bold]Active[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Dead Letter[/]").RightAligned());

        foreach (QueueInfo queue in queues)
        {
            string active     = FormatCount(queue.ActiveMessageCount,     "[green]", "[/]");
            string deadLetter = FormatCount(queue.DeadLetterMessageCount, "[red]",   "[/]");
            table.AddRow($"[deepskyblue1]{queue.Name}[/]", active, deadLetter);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n[grey]{queues.Count} queue(s) found. Counts via peek (max 250).[/]\n");
    }

    private static void RenderBrowseScreen(
        string queueName,
        bool fromDeadLetter,
        long fromSeq,
        IReadOnlyList<ServiceBusReceivedMessage> messages)
    {
        string subLabel = fromDeadLetter ? "[red]Dead Letter[/]" : "[green]Active[/]";
        AnsiConsole.MarkupLine(string.Create(CultureInfo.InvariantCulture,
            $"[bold deepskyblue1]{Markup.Escape(queueName)}[/]  ·  {subLabel}  " +
            $"[grey]· page starting at seq #{fromSeq} · {messages.Count} message(s)[/]\n"));

        if (messages.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No messages on this page.[/]\n");
            return;
        }

        foreach (ServiceBusReceivedMessage msg in messages)
            RenderMessage(msg, fromDeadLetter);
    }

    private static void RenderQueueInfo(QueueProperties props, int activeCount, int deadLetterCount)
    {
        var sb = new StringBuilder();

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Status           :[/] {FormatStatus(props.Status)}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Requires Session :[/] {(props.RequiresSession ? "[green]Yes[/]" : "No")}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Max Delivery     :[/] {props.MaxDeliveryCount}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Lock Duration    :[/] {FormatDuration(props.LockDuration)}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]DL on Expiry     :[/] {(props.DeadLetteringOnMessageExpiration ? "[yellow]Yes[/]" : "No")}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Message TTL      :[/] {FormatDuration(props.DefaultMessageTimeToLive)}"));
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Active Messages  :[/] {FormatCount(activeCount,     "[green]", "[/]")}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Dead Letter      :[/] {FormatCount(deadLetterCount, "[red]",   "[/]")}"));

        AnsiConsole.Write(
            new Panel(sb.ToString().TrimEnd())
                .Header(string.Create(CultureInfo.InvariantCulture, $"[deepskyblue1] {Markup.Escape(props.Name)} [/]"))
                .Border(BoxBorder.Rounded)
                .Padding(1, 0));

        AnsiConsole.WriteLine();
    }

    private static void RenderMessage(ServiceBusReceivedMessage msg, bool isDeadLetter)
    {
        var sb = new StringBuilder();

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Message ID    :[/] {Markup.Escape(msg.MessageId)}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Correlation ID:[/] {Markup.Escape(msg.CorrelationId ?? "—")}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Content Type  :[/] {Markup.Escape(msg.ContentType ?? "—")}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Enqueued      :[/] {msg.EnqueuedTime:yyyy-MM-dd HH:mm:ss zzz}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Sequence #    :[/] {msg.SequenceNumber}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[grey]Delivery Count:[/] {msg.DeliveryCount}"));

        if (isDeadLetter)
        {
            sb.AppendLine();
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[red]DL Reason     :[/] {Markup.Escape(msg.DeadLetterReason ?? "—")}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[red]DL Description:[/] {Markup.Escape(msg.DeadLetterErrorDescription ?? "—")}"));
        }

        if (msg.ApplicationProperties.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[grey]── Application Properties ──[/]");
            foreach (KeyValuePair<string, object> prop in msg.ApplicationProperties)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  [grey]{Markup.Escape(prop.Key)}:[/] {Markup.Escape(prop.Value.ToString() ?? "null")}"));
        }

        sb.AppendLine();
        sb.AppendLine("[bold]── Body ──[/]");
        sb.Append(Markup.Escape(TryFormatJson(msg.Body.ToString())));

        AnsiConsole.Write(
            new Panel(sb.ToString())
                .Header(string.Create(CultureInfo.InvariantCulture, $"[deepskyblue1] #{msg.SequenceNumber}  {Markup.Escape(msg.MessageId)} [/]"))
                .Border(BoxBorder.Rounded)
                .Padding(1, 0));

        AnsiConsole.WriteLine();
    }

    // Returns the next fromSequenceNumber, or null when the user chooses to go back.
    private static long? NavigatePage(
        IReadOnlyList<ServiceBusReceivedMessage> messages,
        Stack<long> history,
        long fromSeq)
    {
        var choices = new List<string>();
        if (messages.Count == PageSize) choices.Add("→  Next Page");
        if (history.Count > 0)          choices.Add("←  Previous Page");
        choices.Add("↩  Back to Queue List");

        string nav = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[grey]Navigate:[/]")
                .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .AddChoices(choices));

        if (nav.StartsWith('→'))
        {
            history.Push(fromSeq);
            return messages[^1].SequenceNumber + 1;
        }

        if (nav.StartsWith('←'))
            return history.Pop();

        return null;
    }

    private static string FormatStatus(EntityStatus status)
    {
        if (status == EntityStatus.Active)          return "[green]Active[/]";
        if (status == EntityStatus.Disabled)        return "[red]Disabled[/]";
        if (status == EntityStatus.ReceiveDisabled) return "[yellow]Receive Disabled[/]";
        if (status == EntityStatus.SendDisabled)    return "[yellow]Send Disabled[/]";
        return status.ToString();
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts == TimeSpan.MaxValue)  return "∞";
        if (ts.TotalDays >= 1)        return string.Create(CultureInfo.InvariantCulture, $"{(int)ts.TotalDays}d {ts.Hours}h");
        if (ts.TotalHours >= 1)       return string.Create(CultureInfo.InvariantCulture, $"{(int)ts.TotalHours}h {ts.Minutes}m");
        if (ts.TotalMinutes >= 1)     return string.Create(CultureInfo.InvariantCulture, $"{(int)ts.TotalMinutes}m {ts.Seconds}s");
        return string.Create(CultureInfo.InvariantCulture, $"{ts.Seconds}s");
    }

    private static string FormatCount(int count, string openTag, string closeTag)
    {
        if (count == 0)   return "0";
        if (count >= 250) return $"{openTag}250+{closeTag}";
        return $"{openTag}{count}{closeTag}";
    }

    private static string TryFormatJson(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
        }
        catch
        {
            return raw;
        }
    }

    private static void Pause()
    {
        AnsiConsole.MarkupLine("\n[grey]Press any key to continue…[/]");
        Console.ReadKey(intercept: true);
    }
}

