namespace BusWorks.Viewer.Console.Models;

public sealed record QueueInfo(
    string Name,
    int ActiveMessageCount,
    int DeadLetterMessageCount);

