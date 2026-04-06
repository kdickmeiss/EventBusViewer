using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using BusWorks.Viewer.Models;

namespace BusWorks.Viewer.Services;

public sealed class QueueService
{
    private readonly ServiceBusClientProvider _clients;

    public QueueService(ServiceBusClientProvider clients) => _clients = clients;

    private ServiceBusAdministrationClient AdminClient => _clients.AdminClient;
    private ServiceBusClient               BusClient   => _clients.BusClient;


    /// <summary>
    /// Returns every queue with its active and dead-letter message counts (peek-based).
    /// </summary>
    public async Task<IReadOnlyList<QueueInfo>> GetAllQueuesAsync(CancellationToken cancellationToken = default)
    {
        var queues = new List<QueueInfo>();

        await foreach (QueueProperties queue in AdminClient.GetQueuesAsync(cancellationToken))
        {
            int active = await CountMessagesAsync(queue.Name, fromDeadLetter: false, cancellationToken);
            int deadLetter = await CountMessagesAsync(queue.Name, fromDeadLetter: true, cancellationToken);

            queues.Add(new QueueInfo(queue.Name, active, deadLetter, queue.Status, queue.RequiresSession));
        }

        return queues;
    }

    /// <summary>
    /// Peek-based count — reliable against the emulator unlike <c>GetQueueRuntimePropertiesAsync</c>.
    /// Returns up to 250; anything at the cap is displayed as "250+" in the UI.
    /// </summary>
    private async Task<int> CountMessagesAsync(
        string queueName,
        bool fromDeadLetter,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ServiceBusReceivedMessage> messages =
            await PeekMessagesAsync(queueName, maxMessages: 250, fromDeadLetter: fromDeadLetter,
                cancellationToken: cancellationToken);

        return messages.Count;
    }

    private Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(
        string queueName,
        int maxMessages = 10,
        bool fromDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        return PeekMessagesPagedAsync(queueName, maxMessages, fromSequenceNumber: 0, fromDeadLetter,
            cancellationToken);
    }

    /// <summary>
    /// Peeks a page of messages starting from a specific sequence number — used for paginated browsing.
    /// </summary>
    public async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesPagedAsync(
        string queueName,
        int maxMessages,
        long fromSequenceNumber,
        bool fromDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        ServiceBusReceiver receiver = fromDeadLetter
            ? BusClient.CreateReceiver(queueName,
                new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter })
            : BusClient.CreateReceiver(queueName);

        await using (receiver)
        {
            return await receiver.PeekMessagesAsync(maxMessages, fromSequenceNumber, cancellationToken);
        }
    }

    public async Task SendMessageAsync(
        string queueName,
        string messageBody,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        await using ServiceBusSender sender = BusClient.CreateSender(queueName);

        var message = new ServiceBusMessage(messageBody)
        {
            ContentType = "application/json",
            MessageId = Guid.NewGuid().ToString()
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
            message.SessionId = sessionId;

        await sender.SendMessageAsync(message, cancellationToken);
    }

    /// <summary>
    /// Returns full queue detail: admin properties + peek-based message counts.
    /// </summary>
    public async Task<QueueDetail> GetQueueDetailAsync(string queueName, CancellationToken cancellationToken = default)
    {
        QueueProperties props = await GetQueuePropertiesAsync(queueName, cancellationToken);
        int active = await CountMessagesAsync(queueName, fromDeadLetter: false, cancellationToken);
        int deadLetter = await CountMessagesAsync(queueName, fromDeadLetter: true, cancellationToken);

        return QueueDetail.FromProperties(props, active, deadLetter);
    }

    public async Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        Azure.Response<bool> response = await AdminClient.QueueExistsAsync(queueName, cancellationToken);
        return response.Value;
    }

    public async Task<QueueProperties> GetQueuePropertiesAsync(string queueName,
        CancellationToken cancellationToken = default)
    {
        Azure.Response<QueueProperties> response = await AdminClient.GetQueueAsync(queueName, cancellationToken);
        return response.Value;
    }

    public Task UpdateQueueAsync(QueueProperties properties, CancellationToken cancellationToken = default) =>
        AdminClient.UpdateQueueAsync(properties, cancellationToken);

    public Task DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default) =>
        AdminClient.DeleteQueueAsync(queueName, cancellationToken);

    public Task CreateQueueAsync(
        string name,
        bool requiresSession,
        int maxDeliveryCount,
        int lockDurationSeconds,
        bool deadLetterOnExpiration,
        CancellationToken cancellationToken = default)
    {
        var options = new CreateQueueOptions(name)
        {
            RequiresSession = requiresSession,
            MaxDeliveryCount = maxDeliveryCount,
            LockDuration = TimeSpan.FromSeconds(lockDurationSeconds),
            DeadLetteringOnMessageExpiration = deadLetterOnExpiration
        };

        return AdminClient.CreateQueueAsync(options, cancellationToken);
    }
}
