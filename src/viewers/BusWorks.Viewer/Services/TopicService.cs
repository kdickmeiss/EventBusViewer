using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using BusWorks.Viewer.Models;

namespace BusWorks.Viewer.Services;

public sealed class TopicService(ServiceBusClientProvider clients)
{
    private ServiceBusAdministrationClient AdminClient => clients.AdminClient;
    private ServiceBusClient BusClient => clients.BusClient;

    /// <summary>
    /// Returns every topic with its subscription count and aggregate active/dead-letter message counts (peek-based).
    /// </summary>
    public async Task<IReadOnlyList<TopicInfo>> GetAllTopicsAsync(CancellationToken cancellationToken = default)
    {
        var topics = new List<TopicInfo>();

        await foreach (TopicProperties topic in AdminClient.GetTopicsAsync(cancellationToken))
        {
            var subscriptionNames = new List<string>();
            await foreach (SubscriptionProperties sub in AdminClient.GetSubscriptionsAsync(topic.Name,
                               cancellationToken))
                subscriptionNames.Add(sub.SubscriptionName);

            int active = 0;
            int deadLetter = 0;

            foreach (string sub in subscriptionNames)
            {
                active += await CountSubscriptionMessagesAsync(topic.Name, sub, fromDeadLetter: false,
                    cancellationToken);
                deadLetter +=
                    await CountSubscriptionMessagesAsync(topic.Name, sub, fromDeadLetter: true, cancellationToken);
            }

            topics.Add(new TopicInfo(topic.Name, subscriptionNames.Count, active, deadLetter, topic.Status));
        }

        return topics;
    }

    /// <summary>
    /// Peek-based count per subscription — capped at 250 (displayed as "250+" in the UI).
    /// </summary>
    private async Task<int> CountSubscriptionMessagesAsync(
        string topicName,
        string subscriptionName,
        bool fromDeadLetter,
        CancellationToken cancellationToken)
    {
        ServiceBusReceiver receiver = fromDeadLetter
            ? BusClient.CreateReceiver(topicName, subscriptionName,
                new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter })
            : BusClient.CreateReceiver(topicName, subscriptionName);

        await using (receiver)
        {
            IReadOnlyList<ServiceBusReceivedMessage> messages =
                await receiver.PeekMessagesAsync(maxMessages: 250, cancellationToken: cancellationToken);
            return messages.Count;
        }
    }

    // ── Topic detail ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns full topic detail: admin properties + all subscriptions with peek-based message counts.
    /// </summary>
    public async Task<TopicDetail> GetTopicDetailAsync(string topicName, CancellationToken cancellationToken = default)
    {
        TopicProperties props = await GetTopicPropertiesAsync(topicName, cancellationToken);
        IReadOnlyList<SubscriptionInfo> subscriptions = await GetSubscriptionsDetailAsync(topicName, cancellationToken);
        return TopicDetail.FromProperties(props, subscriptions);
    }

    /// <summary>
    /// Returns all subscriptions for a topic with peek-based message counts.
    /// </summary>
    public async Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsDetailAsync(
        string topicName,
        CancellationToken cancellationToken = default)
    {
        var result = new List<SubscriptionInfo>();

        await foreach (SubscriptionProperties sub in AdminClient.GetSubscriptionsAsync(topicName, cancellationToken))
        {
            int active = await CountSubscriptionMessagesAsync(topicName, sub.SubscriptionName, fromDeadLetter: false,
                cancellationToken);
            int deadLetter = await CountSubscriptionMessagesAsync(topicName, sub.SubscriptionName, fromDeadLetter: true,
                cancellationToken);

            result.Add(new SubscriptionInfo(
                sub.SubscriptionName,
                active,
                deadLetter,
                sub.Status,
                sub.MaxDeliveryCount,
                sub.LockDuration,
                sub.RequiresSession,
                sub.DeadLetteringOnMessageExpiration,
                sub.DefaultMessageTimeToLive,
                sub.EnableBatchedOperations));
        }

        return result;
    }

    /// <summary>
    /// Peeks a page of subscription messages starting from a specific sequence number.
    /// </summary>
    public async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekSubscriptionMessagesPagedAsync(
        string topicName,
        string subscriptionName,
        int maxMessages,
        long fromSequenceNumber,
        bool fromDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        ServiceBusReceiver receiver = fromDeadLetter
            ? BusClient.CreateReceiver(topicName, subscriptionName,
                new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter })
            : BusClient.CreateReceiver(topicName, subscriptionName);

        await using (receiver)
        {
            return await receiver.PeekMessagesAsync(maxMessages, fromSequenceNumber, cancellationToken);
        }
    }

    // ── Subscription CRUD ─────────────────────────────────────────────────────

    public async Task<bool> SubscriptionExistsAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken = default)
    {
        Azure.Response<bool> response =
            await AdminClient.SubscriptionExistsAsync(topicName, subscriptionName, cancellationToken);
        return response.Value;
    }

    public Task CreateSubscriptionAsync(
        string topicName,
        string subscriptionName,
        bool requiresSession,
        int maxDeliveryCount,
        int lockDurationSeconds,
        bool deadLetterOnMessageExpiration,
        CancellationToken cancellationToken = default)
    {
        var options = new CreateSubscriptionOptions(topicName, subscriptionName)
        {
            RequiresSession = requiresSession,
            MaxDeliveryCount = maxDeliveryCount,
            LockDuration = TimeSpan.FromSeconds(lockDurationSeconds),
            DeadLetteringOnMessageExpiration = deadLetterOnMessageExpiration
        };

        return AdminClient.CreateSubscriptionAsync(options, cancellationToken);
    }

    public Task DeleteSubscriptionAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken = default) =>
        AdminClient.DeleteSubscriptionAsync(topicName, subscriptionName, cancellationToken);

    // ── Topic existence / properties ─────────────────────────────────────────

    public async Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken = default)
    {
        Azure.Response<bool> response = await AdminClient.TopicExistsAsync(topicName, cancellationToken);
        return response.Value;
    }

    public async Task<TopicProperties> GetTopicPropertiesAsync(string topicName,
        CancellationToken cancellationToken = default)
    {
        Azure.Response<TopicProperties> response = await AdminClient.GetTopicAsync(topicName, cancellationToken);
        return response.Value;
    }

    public Task UpdateTopicAsync(TopicProperties properties, CancellationToken cancellationToken = default) =>
        AdminClient.UpdateTopicAsync(properties, cancellationToken);

    public Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken = default) =>
        AdminClient.DeleteTopicAsync(topicName, cancellationToken);

    public Task CreateTopicAsync(
        string name,
        int defaultMessageTtlSeconds,
        bool enableBatchedOperations,
        CancellationToken cancellationToken = default)
    {
        var options = new CreateTopicOptions(name)
        {
            DefaultMessageTimeToLive = TimeSpan.FromSeconds(defaultMessageTtlSeconds),
            EnableBatchedOperations = enableBatchedOperations
        };

        return AdminClient.CreateTopicAsync(options, cancellationToken);
    }

    // ── Messaging ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a JSON message to a topic. <paramref name="sessionId"/> is optional — include it
    /// when at least one subscription on the topic requires sessions.
    /// </summary>
    public async Task SendMessageAsync(
        string topicName,
        string messageBody,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        await using ServiceBusSender sender = BusClient.CreateSender(topicName);

        var message = new ServiceBusMessage(messageBody)
        {
            ContentType = "application/json",
            MessageId = Guid.NewGuid().ToString()
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
            message.SessionId = sessionId;

        await sender.SendMessageAsync(message, cancellationToken);
    }

    // ── Subscription properties / update ─────────────────────────────────────

    public async Task<SubscriptionProperties> GetSubscriptionPropertiesAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken = default)
    {
        Azure.Response<SubscriptionProperties> response =
            await AdminClient.GetSubscriptionAsync(topicName, subscriptionName, cancellationToken);
        return response.Value;
    }

    public Task UpdateSubscriptionAsync(
        SubscriptionProperties properties,
        CancellationToken cancellationToken = default) =>
        AdminClient.UpdateSubscriptionAsync(properties, cancellationToken);
}
