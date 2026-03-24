namespace EventBusViewer.Client.Models;

public sealed record QueueInfo(string Name, int ActiveMessages, int DeadLetterMessages)
{
    public int TotalMessages => ActiveMessages + DeadLetterMessages;
}

