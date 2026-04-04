using Azure.Messaging.ServiceBus;
using BusWorks.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace BusWorks.BackgroundServices;

internal sealed class ServiceBusProcessorBackgroundService(
    IHostApplicationLifetime hostApplicationLifetime,
    IServiceScopeFactory serviceScopeFactory,
    ServiceBusClient serviceBusClient,
    ServiceBusAssemblyRegistry assemblyRegistry,
    IOptions<BusWorksOptions> eventBusOptions,
    Tracer tracer,
    ILogger<ServiceBusProcessorBackgroundService> logger) : BackgroundService
{
    private readonly BusWorksOptions _worksOptions = eventBusOptions.Value;
    private readonly ServiceBusTelemetry _telemetry = new(tracer, logger);
    private readonly List<ServiceBusProcessor> _serviceBusProcessors = [];
    private readonly List<ServiceBusSessionProcessor> _serviceBusSessionProcessors = [];
    
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

            ServiceBusEndpoint endpoint = ServiceBusEndpointResolver.Resolve(consumerType);
            ServiceBusConsumerValidator.ValidateSessionContract(consumerType, endpoint);
            ServiceBusTelemetry.SetupSpanAttributes(consumerSetupSpan, endpoint);

            string endpointDescription = ServiceBusEndpointResolver.GetEndpointDescription(endpoint);

            // Pre-build the processor factory once per consumer type — keeps MakeGenericMethod
            // and Invoke out of the per-message hot path.
            string consumerName = consumerType.Name;
            Func<IServiceProvider, Func<ServiceBusReceivedMessage, CancellationToken, Task>> processorFactory =
                ServiceBusMessageProcessorBuilder.Build(consumerType);

            if (endpoint.RequireSession)
            {
                ServiceBusSessionProcessor sessionProcessor = CreateSessionProcessor(endpoint);
                ConfigureSessionMessageHandler(sessionProcessor, consumerName, processorFactory, endpoint, endpointDescription);
                sessionProcessor.ProcessErrorAsync += _telemetry.BuildErrorHandler(consumerType, endpointDescription, "ServiceBus:Session:Error");
                await sessionProcessor.StartProcessingAsync(stoppingToken);
                _serviceBusSessionProcessors.Add(sessionProcessor);
            }
            else
            {
                ServiceBusProcessor processor = CreateProcessor(endpoint);
                ConfigureMessageHandler(processor, consumerName, processorFactory, endpoint, endpointDescription);
                processor.ProcessErrorAsync += _telemetry.BuildErrorHandler(consumerType, endpointDescription, "ServiceBus:Error");
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

    private ServiceBusProcessor CreateProcessor(ServiceBusEndpoint endpoint)
    {
        var options = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = _worksOptions.MaxConcurrentCalls,
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
            MaxConcurrentSessions = _worksOptions.MaxConcurrentSessions,
            MaxConcurrentCallsPerSession = _worksOptions.MaxConcurrentCallsPerSession,
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
            using TelemetrySpan messageSpan = _telemetry.CreateMessageSpan(consumerName, endpoint, args.Message);

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
                _telemetry.HandleMessageProcessingError(messageSpan, args, ex, endpointDescription);

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
            using TelemetrySpan messageSpan = _telemetry.CreateMessageSpan(consumerName, endpoint, args.Message);
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
}
