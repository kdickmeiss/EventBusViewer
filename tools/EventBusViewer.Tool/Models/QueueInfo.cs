namespace EventBusViewer.Tool.Models;

public sealed record QueueInfo(
    string Name,
    int ActiveMessageCount,
    int DeadLetterMessageCount);

