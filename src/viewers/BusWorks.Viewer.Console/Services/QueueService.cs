using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using EventBusViewer.Viewer.Console.Models;

namespace EventBusViewer.Viewer.Console.Services;

public sealed class QueueService(
    ServiceBusAdministrationClient client,
    ServiceBusClient serviceBusClient)
{
    public async Task<IReadOnlyList<QueueInfo>> GetAllQueuesAsync(CancellationToken cancellationToken = default)
    {
        var queues = new List<QueueInfo>();

        await foreach (QueueProperties queue in client.GetQueuesAsync(cancellationToken))
        {
            int active     = await CountMessagesAsync(queue.Name, fromDeadLetter: false, cancellationToken);
            int deadLetter = await CountMessagesAsync(queue.Name, fromDeadLetter: true,  cancellationToken);

            queues.Add(new QueueInfo(queue.Name, active, deadLetter));
        }

        return queues;
    }

    // Peek-based count — reliable against the emulator unlike GetQueueRuntimePropertiesAsync.
    // Returns up to 250; anything at the cap is displayed as "250+" in the UI.
    private async Task<int> CountMessagesAsync(
        string queueName,
        bool fromDeadLetter,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ServiceBusReceivedMessage> messages =
            await PeekMessagesAsync(queueName, maxMessages: 250, fromDeadLetter: fromDeadLetter, cancellationToken: cancellationToken);

        return messages.Count;
    }

    private Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(
        string queueName,
        int maxMessages = 10,
        bool fromDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        return PeekMessagesPagedAsync(queueName, maxMessages, fromSequenceNumber: 0, fromDeadLetter, cancellationToken);
    }

    // Peeks a page of messages starting from a specific sequence number — used for paginated browsing.
    public async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesPagedAsync(
        string queueName,
        int maxMessages,
        long fromSequenceNumber,
        bool fromDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        ServiceBusReceiver receiver = fromDeadLetter
            ? serviceBusClient.CreateReceiver(queueName,
                new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter })
            : serviceBusClient.CreateReceiver(queueName);

        await using (receiver)
        {
            return await receiver.PeekMessagesAsync(maxMessages, fromSequenceNumber, cancellationToken);
        }
    }

    public async Task SendMessageAsync(
        string queueName,
        string messageBody,
        CancellationToken cancellationToken = default)
    {
        await using ServiceBusSender sender = serviceBusClient.CreateSender(queueName);

        var message = new ServiceBusMessage(messageBody)
        {
            ContentType = "application/json",
            MessageId   = Guid.NewGuid().ToString()
        };

        await sender.SendMessageAsync(message, cancellationToken);
    }

    public async Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        Azure.Response<bool> response = await client.QueueExistsAsync(queueName, cancellationToken);
        return response.Value;
    }

    public async Task<QueueProperties> GetQueuePropertiesAsync(string queueName, CancellationToken cancellationToken = default)
    {
        Azure.Response<QueueProperties> response = await client.GetQueueAsync(queueName, cancellationToken);
        return response.Value;
    }

    public Task UpdateQueueAsync(QueueProperties properties, CancellationToken cancellationToken = default) =>
        client.UpdateQueueAsync(properties, cancellationToken);

    public Task DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default) =>
        client.DeleteQueueAsync(queueName, cancellationToken);

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
            RequiresSession                  = requiresSession,
            MaxDeliveryCount                 = maxDeliveryCount,
            LockDuration                     = TimeSpan.FromSeconds(lockDurationSeconds),
            DeadLetteringOnMessageExpiration = deadLetterOnExpiration
        };

        return client.CreateQueueAsync(options, cancellationToken);
    }
}
