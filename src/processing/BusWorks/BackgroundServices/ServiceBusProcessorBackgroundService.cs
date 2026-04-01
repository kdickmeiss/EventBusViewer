using System.Reflection;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Consumer;
using BusWorks.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace BusWorks.BackgroundServices;

internal record ServiceBusEndpoint(
    string QueueOrTopicName,
    string? SubscriptionName = null,
    bool RequireSession = false,
    int MaxDeliveryCount = 5)
{
    public bool IsQueue => SubscriptionName is null;
    public bool IsTopic => SubscriptionName is not null;
}

internal sealed class ServiceBusProcessorBackgroundService(
    IHostApplicationLifetime hostApplicationLifetime,
    IServiceScopeFactory serviceScopeFactory,
    ServiceBusClient serviceBusClient,
    ServiceBusAssemblyRegistry assemblyRegistry,
    IOptions<EventBusOptions> eventBusOptions,
    Tracer tracer,
    ILogger<ServiceBusProcessorBackgroundService> logger) : BackgroundService
{
    private readonly EventBusOptions _options = eventBusOptions.Value;
    private readonly List<ServiceBusProcessor> _serviceBusProcessors = [];
    private readonly List<ServiceBusSessionProcessor> _serviceBusSessionProcessors = [];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0063:Use simple 'using' statement", Justification = "<Pending>")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForApplicationStartup();

        IReadOnlyList<Type> consumerTypes = assemblyRegistry.GetConsumerTypes();

        using TelemetrySpan startupSpan = tracer.StartActiveSpan("ServiceBus:Startup");
        startupSpan.SetAttribute("servicebus.consumers.count", consumerTypes.Count);

        int failedCount = 0;
        foreach (Type consumerType in consumerTypes)
        {
            bool success = await SetupConsumerAsync(consumerType, stoppingToken);
            if (!success) failedCount++;
        }

        startupSpan.SetAttribute("servicebus.consumers.failed", failedCount);
        startupSpan.SetStatus(failedCount > 0
            ? Status.Error.WithDescription($"{failedCount} of {consumerTypes.Count} consumer(s) failed to start")
            : Status.Ok);
    }

    private async Task WaitForApplicationStartup()
    {
        if (hostApplicationLifetime.ApplicationStarted.IsCancellationRequested)
            return;

        var tcs = new TaskCompletionSource();
        await using CancellationTokenRegistration reg = hostApplicationLifetime.ApplicationStarted.Register(() => tcs.SetResult());
        await tcs.Task;
    }

    private async Task<bool> SetupConsumerAsync(Type consumerType, CancellationToken stoppingToken)
    {
        try
        {
            using TelemetrySpan consumerSetupSpan = tracer.StartActiveSpan($"ServiceBus:Setup:{consumerType.Name}");
            consumerSetupSpan.SetAttribute("servicebus.consumer.type", consumerType.Name);

            ServiceBusEndpoint endpoint = ResolveEndpoint(consumerType);
            ValidateSessionContract(consumerType, endpoint);
            SetupSpanAttributes(consumerSetupSpan, endpoint);

            string endpointDescription = GetEndpointDescription(endpoint);

            // Pre-build the processor factory once per consumer type — keeps MakeGenericMethod
            // and Invoke out of the per-message hot path.
            string consumerName = consumerType.Name;
            Func<IServiceProvider, Func<ServiceBusReceivedMessage, CancellationToken, Task>> processorFactory =
                BuildProcessorFactory(consumerType);

            if (endpoint.RequireSession)
            {
                ServiceBusSessionProcessor sessionProcessor = CreateSessionProcessor(endpoint);
                ConfigureSessionMessageHandler(sessionProcessor, consumerName, processorFactory, endpoint, endpointDescription);
                ConfigureSessionErrorHandler(sessionProcessor, consumerType, endpointDescription);
                await sessionProcessor.StartProcessingAsync(stoppingToken);
                _serviceBusSessionProcessors.Add(sessionProcessor);
            }
            else
            {
                ServiceBusProcessor processor = CreateProcessor(endpoint);
                ConfigureMessageHandler(processor, consumerName, processorFactory, endpoint, endpointDescription);
                ConfigureErrorHandler(processor, consumerType, endpointDescription);
                await processor.StartProcessingAsync(stoppingToken);
                _serviceBusProcessors.Add(processor);
            }

            consumerSetupSpan.AddEvent("consumer.started");
            consumerSetupSpan.SetStatus(Status.Ok);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start ServiceBusConsumer: {ConsumerType}", consumerType.Name);

            using TelemetrySpan errorSpan = tracer.StartActiveSpan("ServiceBus:Setup:Error");
            errorSpan.SetAttribute("servicebus.consumer.type", consumerType.Name);
            errorSpan.RecordException(ex);
            errorSpan.SetStatus(Status.Error.WithDescription(ex.Message));
            return false;
        }
    }

    private static void SetupSpanAttributes(TelemetrySpan span, ServiceBusEndpoint endpoint)
    {
        span.SetAttribute("servicebus.endpoint.type", endpoint.IsQueue ? "queue" : "topic");
        span.SetAttribute("servicebus.endpoint.name", endpoint.QueueOrTopicName);
        span.SetAttribute("servicebus.session.required", endpoint.RequireSession);
        if (endpoint.IsTopic)
            span.SetAttribute("servicebus.subscription.name", endpoint.SubscriptionName);
    }

    private static string GetEndpointDescription(ServiceBusEndpoint endpoint) =>
        endpoint.IsQueue
            ? $"Queue: {endpoint.QueueOrTopicName}"
            : $"Topic: {endpoint.QueueOrTopicName}, Subscription: {endpoint.SubscriptionName}";

    private ServiceBusProcessor CreateProcessor(ServiceBusEndpoint endpoint)
    {
        var options = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = _options.MaxConcurrentCalls,
            AutoCompleteMessages = false,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
        };

        return endpoint.IsQueue
            ? serviceBusClient.CreateProcessor(endpoint.QueueOrTopicName, options)
            : serviceBusClient.CreateProcessor(endpoint.QueueOrTopicName, endpoint.SubscriptionName!, options);
    }

    private ServiceBusSessionProcessor CreateSessionProcessor(ServiceBusEndpoint endpoint)
    {
        var options = new ServiceBusSessionProcessorOptions
        {
            MaxConcurrentSessions = _options.MaxConcurrentSessions,
            MaxConcurrentCallsPerSession = _options.MaxConcurrentCallsPerSession,
            AutoCompleteMessages = false,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
        };

        return endpoint.IsQueue
            ? serviceBusClient.CreateSessionProcessor(endpoint.QueueOrTopicName, options)
            : serviceBusClient.CreateSessionProcessor(endpoint.QueueOrTopicName, endpoint.SubscriptionName!, options);
    }

    private void ConfigureMessageHandler(
        ServiceBusProcessor processor,
        string consumerName,
        Func<IServiceProvider, Func<ServiceBusReceivedMessage, CancellationToken, Task>> processorFactory,
        ServiceBusEndpoint endpoint,
        string endpointDescription)
    {
        processor.ProcessMessageAsync += async args =>
        {
            using IServiceScope scope = serviceScopeFactory.CreateScope();
            using TelemetrySpan messageSpan = CreateMessageSpan(consumerName, endpoint, args.Message);

            try
            {
                messageSpan.AddEvent("message.processing.started");

                Func<ServiceBusReceivedMessage, CancellationToken, Task> process =
                    processorFactory(scope.ServiceProvider);

                await process(args.Message, args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);

                messageSpan.AddEvent("message.completed");
                messageSpan.SetStatus(Status.Ok);
            }
            catch (Exception ex)
            {
                HandleMessageProcessingError(messageSpan, args, ex, endpointDescription);

                if (endpoint.MaxDeliveryCount > 0 && args.Message.DeliveryCount >= endpoint.MaxDeliveryCount)
                {
                    messageSpan.AddEvent("message.deadlettered");
                    await args.DeadLetterMessageAsync(
                        args.Message,
                        deadLetterReason: "MaxDeliveryCountExceeded",
                        deadLetterErrorDescription: $"Message exceeded the maximum delivery count of {endpoint.MaxDeliveryCount}. Last error: {ex.Message}",
                        cancellationToken: args.CancellationToken);
                }
                else
                {
                    messageSpan.AddEvent("message.abandoned");
                    await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                }
            }
        };
    }

    private void ConfigureSessionMessageHandler(
        ServiceBusSessionProcessor processor,
        string consumerName,
        Func<IServiceProvider, Func<ServiceBusReceivedMessage, CancellationToken, Task>> processorFactory,
        ServiceBusEndpoint endpoint,
        string endpointDescription)
    {
        processor.ProcessMessageAsync += async args =>
        {
            using IServiceScope scope = serviceScopeFactory.CreateScope();
            using TelemetrySpan messageSpan = CreateMessageSpan(consumerName, endpoint, args.Message);
            messageSpan.SetAttribute("messaging.servicebus.session_id", args.SessionId);

            try
            {
                messageSpan.AddEvent("message.processing.started");

                Func<ServiceBusReceivedMessage, CancellationToken, Task> process =
                    processorFactory(scope.ServiceProvider);

                await process(args.Message, args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);

                messageSpan.AddEvent("message.completed");
                messageSpan.SetStatus(Status.Ok);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error processing session message from {Endpoint}, SessionId: {SessionId}, MessageId: {MessageId}",
                    endpointDescription,
                    args.SessionId,
                    args.Message.MessageId);

                messageSpan.RecordException(ex);
                messageSpan.SetStatus(Status.Error.WithDescription(ex.Message));

                if (endpoint.MaxDeliveryCount > 0 && args.Message.DeliveryCount >= endpoint.MaxDeliveryCount)
                {
                    messageSpan.AddEvent("message.deadlettered");
                    await args.DeadLetterMessageAsync(
                        args.Message,
                        deadLetterReason: "MaxDeliveryCountExceeded",
                        deadLetterErrorDescription: $"Message exceeded the maximum delivery count of {endpoint.MaxDeliveryCount}. Last error: {ex.Message}",
                        cancellationToken: args.CancellationToken);
                }
                else
                {
                    messageSpan.AddEvent("message.abandoned");
                    await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                }
            }
        };
    }

    private TelemetrySpan CreateMessageSpan(string consumerName, ServiceBusEndpoint endpoint, ServiceBusReceivedMessage message)
    {
        TelemetrySpan messageSpan = tracer.StartActiveSpan(
            $"ServiceBus:Process:{consumerName}",
            SpanKind.Consumer);

        messageSpan.SetAttribute("messaging.system", "azureservicebus");
        messageSpan.SetAttribute("messaging.operation", "process");
        messageSpan.SetAttribute("messaging.destination.name", endpoint.QueueOrTopicName);
        messageSpan.SetAttribute("messaging.message.id", message.MessageId);
        messageSpan.SetAttribute("messaging.message.body.size", message.Body.ToMemory().Length);
        messageSpan.SetAttribute("messaging.servicebus.delivery_count", message.DeliveryCount);
        messageSpan.SetAttribute("messaging.consumer.name", consumerName);

        AddOptionalMessageAttributes(messageSpan, message, endpoint);

        return messageSpan;
    }

    private static void AddOptionalMessageAttributes(
        TelemetrySpan span,
        ServiceBusReceivedMessage message,
        ServiceBusEndpoint endpoint)
    {
        if (!string.IsNullOrEmpty(message.CorrelationId))
            span.SetAttribute("messaging.message.correlation_id", message.CorrelationId);

        if (endpoint.IsTopic)
            span.SetAttribute("messaging.servicebus.subscription.name", endpoint.SubscriptionName);

        if (message.EnqueuedTime != default)
            span.SetAttribute("messaging.message.enqueued_time", message.EnqueuedTime.ToString("o"));

        if (message.ApplicationProperties.TryGetValue("traceparent", out object? traceParent))
            span.SetAttribute("messaging.trace.parent", traceParent.ToString());
    }

    private void HandleMessageProcessingError(
        TelemetrySpan messageSpan,
        ProcessMessageEventArgs args,
        Exception ex,
        string endpointDescription)
    {
        logger.LogError(
            ex,
            "Error processing message from {Endpoint} with MessageId: {MessageId}",
            endpointDescription,
            args.Message.MessageId);

        messageSpan.RecordException(ex);
        messageSpan.SetStatus(Status.Error.WithDescription(ex.Message));
    }

    private void ConfigureErrorHandler(ServiceBusProcessor processor, Type consumerType, string endpointDescription)
        => processor.ProcessErrorAsync += BuildProcessorErrorHandler(consumerType, endpointDescription, "ServiceBus:Error");

    private void ConfigureSessionErrorHandler(ServiceBusSessionProcessor processor, Type consumerType, string endpointDescription)
        => processor.ProcessErrorAsync += BuildProcessorErrorHandler(consumerType, endpointDescription, "ServiceBus:Session:Error");

    private Func<ProcessErrorEventArgs, Task> BuildProcessorErrorHandler(
        Type consumerType,
        string endpointDescription,
        string spanName)
    {
        return args =>
        {
            using TelemetrySpan errorSpan = tracer.StartActiveSpan(spanName);
            errorSpan.SetAttribute("servicebus.error.source", args.ErrorSource.ToString());
            errorSpan.SetAttribute("servicebus.entity.path", args.EntityPath);
            errorSpan.SetAttribute("servicebus.consumer.type", consumerType.Name);
            errorSpan.RecordException(args.Exception);
            errorSpan.SetStatus(Status.Error.WithDescription(args.Exception.Message));

            logger.LogError(
                args.Exception,
                "ServiceBus processor error for {Endpoint}. Error Source: {ErrorSource}",
                endpointDescription,
                args.ErrorSource);

            return Task.CompletedTask;
        };
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        foreach (ServiceBusProcessor processor in _serviceBusProcessors)
        {
            try { await processor.StopProcessingAsync(cancellationToken); await processor.DisposeAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Error stopping ServiceBusProcessor"); }
        }
        _serviceBusProcessors.Clear();

        foreach (ServiceBusSessionProcessor processor in _serviceBusSessionProcessors)
        {
            try { await processor.StopProcessingAsync(cancellationToken); await processor.DisposeAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Error stopping ServiceBusSessionProcessor"); }
        }
        _serviceBusSessionProcessors.Clear();
    }

    /// <summary>
    /// Validates that the session contract is consistent:
    /// <list type="bullet">
    ///   <item>If <c>RequireSession = true</c> the message type must implement <see cref="ISessionedEvent"/>
    ///   so the publisher can always set a <c>SessionId</c>.</item>
    ///   <item>If <c>RequireSession = false</c> the message type must NOT implement <see cref="ISessionedEvent"/>
    ///   — publishing a session-keyed event to a non-session queue would be silently rejected by the broker.</item>
    /// </list>
    /// Both checks fire at application startup so misconfiguration is caught immediately.
    /// </summary>
    private static void ValidateSessionContract(Type consumerType, ServiceBusEndpoint endpoint)
    {
        Type? messageType = GetConsumerMessageType(consumerType);
        if (messageType is null) return;

        bool isSessioned = typeof(ISessionedEvent).IsAssignableFrom(messageType);

        if (endpoint.RequireSession && !isSessioned)
            throw new InvalidOperationException(
                $"Consumer '{consumerType.Name}' has RequireSession = true, but message type " +
                $"'{messageType.Name}' does not implement ISessionedEvent. " +
                $"Add ': ISessionedEvent' to '{messageType.Name}' and expose a SessionId property " +
                $"that returns a stable domain key (e.g. customerId, orderId).");

        if (!endpoint.RequireSession && isSessioned)
            throw new InvalidOperationException(
                $"Message type '{messageType.Name}' implements ISessionedEvent (declares a SessionId), " +
                $"but consumer '{consumerType.Name}' does not have RequireSession = true. " +
                $"Either add RequireSession = true to the consumer attribute, " +
                $"or remove ISessionedEvent from '{messageType.Name}'.");
    }
    
    private static ServiceBusEndpoint ResolveEndpoint(Type consumerType)
    {
        Type? messageType = GetConsumerMessageType(consumerType);

        ServiceBusQueueAttribute? queueAttr = consumerType.GetCustomAttribute<ServiceBusQueueAttribute>();
        if (queueAttr is not null)
        {
            if (queueAttr.MaxDeliveryCount < 0)
                throw new InvalidOperationException(
                    $"Consumer '{consumerType.Name}' has an invalid MaxDeliveryCount of {queueAttr.MaxDeliveryCount}. " +
                    $"Value must be >= 0.");

            string queueName = queueAttr.QueueName ?? ResolveQueueNameFromMessageType(consumerType, messageType);
            return new ServiceBusEndpoint(queueName, RequireSession: queueAttr.RequireSession, MaxDeliveryCount: queueAttr.MaxDeliveryCount);
        }

        ServiceBusTopicAttribute? topicAttr = consumerType.GetCustomAttribute<ServiceBusTopicAttribute>();
        if (topicAttr is not null)
        {
            if (topicAttr.MaxDeliveryCount < 0)
                throw new InvalidOperationException(
                    $"Consumer '{consumerType.Name}' has an invalid MaxDeliveryCount of {topicAttr.MaxDeliveryCount}. " +
                    $"Value must be >= 0.");

            string topicName = ResolveTopicNameFromMessageType(consumerType, messageType);
            return new ServiceBusEndpoint(topicName, topicAttr.SubscriptionName, topicAttr.RequireSession, topicAttr.MaxDeliveryCount);
        }

        throw new InvalidOperationException(
            $"Consumer '{consumerType.Name}' must have either:\n" +
            $"  - [ServiceBusQueue] or [ServiceBusQueue(\"queue-name\")] for queues\n" +
            $"  - [ServiceBusTopic(\"subscription-name\")] for topic subscriptions\n\n" +
            $"The queue/topic name is resolved from [QueueRoute] / [TopicRoute] on the message type,\n" +
            $"or can be overridden by passing it directly to [ServiceBusQueue(\"explicit-name\")].");
    }

    // Cached once per app lifetime; BuildProcessorFactory calls MakeGenericMethod at consumer-setup time
    // so that neither MakeGenericMethod nor Invoke appear on the per-message hot path.
    private static readonly MethodInfo BuildTypedProcessorMethod =
        typeof(ServiceBusProcessorBackgroundService)
            .GetMethod(nameof(BuildTypedProcessor), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Builds a factory delegate called once per DI scope (i.e. once per message).
    /// <see cref="MethodInfo.MakeGenericMethod"/> is called here at consumer-setup time, not per message.
    /// </summary>
    private static Func<IServiceProvider, Func<ServiceBusReceivedMessage, CancellationToken, Task>>
        BuildProcessorFactory(Type consumerType)
    {
        Type messageType = GetConsumerMessageType(consumerType)!;
        MethodInfo method = BuildTypedProcessorMethod.MakeGenericMethod(messageType);
        return provider =>
        {
            object consumer = provider.GetRequiredService(consumerType);
            return (Func<ServiceBusReceivedMessage, CancellationToken, Task>)method.Invoke(null, [consumer])!;
        };
    }


    private static Func<ServiceBusReceivedMessage, CancellationToken, Task> BuildTypedProcessor<TMessage>(
        IConsumer<TMessage> consumer)
        where TMessage : class, IIntegrationEvent
    {
        return async (message, cancellationToken) =>
        {
            TMessage? deserialized = JsonSerializer.Deserialize<TMessage>(
                message.Body.ToMemory().Span,
                ServiceBusConsumerDefaults.JsonSerializerOptions);

            if (deserialized is null)
                throw new InvalidOperationException(
                    $"Failed to deserialize message {message.MessageId} to type {typeof(TMessage).Name}");

            MessageContext metadata = ToMessageContext(message);
            await consumer.Consume(new ConsumeContext<TMessage>(deserialized, metadata, cancellationToken));
        };
    }

    private static MessageContext ToMessageContext(ServiceBusReceivedMessage m) => new()
    {
        MessageId            = m.MessageId,
        SessionId            = m.SessionId,
        CorrelationId        = m.CorrelationId,
        DeliveryCount        = m.DeliveryCount,
        SequenceNumber       = m.SequenceNumber,
        EnqueuedTime         = m.EnqueuedTime,
        ContentType          = m.ContentType,
        Subject              = m.Subject,
        ApplicationProperties = m.ApplicationProperties
    };

    private static Type? GetConsumerMessageType(Type consumerType)
    {
        return consumerType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault();
    }

    private static string ResolveQueueNameFromMessageType(Type consumerType, Type? messageType)
    {
        if (messageType is null)
            throw new InvalidOperationException(
                $"Consumer '{consumerType.Name}' has [ServiceBusQueue] without an explicit queue name, " +
                $"but does not implement IConsumer<TMessage> so the name cannot be resolved automatically. " +
                $"Pass the name explicitly: [ServiceBusQueue(\"queue-name\")].");

        QueueRouteAttribute? queueRoute = messageType.GetCustomAttribute<QueueRouteAttribute>();
        if (queueRoute is not null)
            return queueRoute.QueueName;

        TopicRouteAttribute? topicRoute = messageType.GetCustomAttribute<TopicRouteAttribute>();
        if (topicRoute is not null)
            throw new InvalidOperationException(
                $"Consumer '{consumerType.Name}' has [ServiceBusQueue] but '{messageType.Name}' " +
                $"is declared as a topic via [TopicRoute(\"{topicRoute.TopicName}\")], not a queue. " +
                $"Did you mean [ServiceBusTopic(\"your-subscription-name\")] on '{consumerType.Name}'?");

        throw new InvalidOperationException(
            $"Consumer '{consumerType.Name}' has [ServiceBusQueue] without an explicit queue name, " +
            $"but message type '{messageType.Name}' does not have a [QueueRoute] attribute. " +
            $"Either add [QueueRoute(\"queue-name\")] to '{messageType.Name}', " +
            $"or pass the name explicitly: [ServiceBusQueue(\"queue-name\")].");
    }

    private static string ResolveTopicNameFromMessageType(Type consumerType, Type? messageType)
    {
        if (messageType is null)
            throw new InvalidOperationException(
                $"Consumer '{consumerType.Name}' has [ServiceBusTopic], but does not implement IConsumer<TMessage>. " +
                $"The topic name cannot be resolved automatically. " +
                $"Use IConsumer<TMessage> with [TopicRoute(\"topic-name\")] on the message type.");

        TopicRouteAttribute? topicRoute = messageType.GetCustomAttribute<TopicRouteAttribute>();
        if (topicRoute is not null)
            return topicRoute.TopicName;

        QueueRouteAttribute? queueRoute = messageType.GetCustomAttribute<QueueRouteAttribute>();
        if (queueRoute is not null)
            throw new InvalidOperationException(
                $"Consumer '{consumerType.Name}' has [ServiceBusTopic] but '{messageType.Name}' " +
                $"is declared as a queue via [QueueRoute(\"{queueRoute.QueueName}\")], not a topic. " +
                $"Did you mean [ServiceBusQueue] on '{consumerType.Name}'?");

        throw new InvalidOperationException(
            $"Consumer '{consumerType.Name}' has [ServiceBusTopic], but message type '{messageType.Name}' " +
            $"does not have a [TopicRoute] attribute. " +
            $"Add [TopicRoute(\"topic-name\")] to '{messageType.Name}'.");
    }
}
