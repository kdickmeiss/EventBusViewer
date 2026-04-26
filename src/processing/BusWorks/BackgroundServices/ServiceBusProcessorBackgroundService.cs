using System.Diagnostics;
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
    private readonly Lock _processorLock = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForApplicationStartup();

        IReadOnlyList<Type> consumerTypes = assemblyRegistry.GetConsumerTypes();
        var configured = new List<ConfiguredProcessor>();

        // ── Phase 1: setup
        // Processors are created and configured under a single startup span, but
        // StartProcessingAsync is NOT called here.  Keeping it out of this block ensures
        // that the worker tasks the SDK spawns internally do NOT capture the startup span's
        // ActivityContext via AsyncLocal — which would silently parent every future message
        // span under this one-time startup trace.
        using (TelemetrySpan startupSpan = tracer.StartActiveSpan("ServiceBus:Startup"))
        {
            startupSpan.SetAttribute("servicebus.consumers.count", consumerTypes.Count);

            int failedCount = 0;
            foreach (Type consumerType in consumerTypes)
            {
                if (stoppingToken.IsCancellationRequested) break;

                ConfiguredProcessor? result = SetupConsumer(consumerType);
                if (result is null) failedCount++;
                else configured.Add(result);
            }

            startupSpan.SetAttribute("servicebus.consumers.failed", failedCount);
            startupSpan.SetStatus(failedCount > 0
                ? Status.Error.WithDescription($"{failedCount} of {consumerTypes.Count} consumer(s) failed to start")
                : Status.Ok);
        }
        // startupSpan is disposed here — Activity.Current is now null again.

        // ── Phase 2: start ────────────────────────────────────────────────────────────
        // Worker tasks the SDK spawns below have no ambient ActivityContext, so message
        // spans will never be accidentally nested under the startup trace.
        foreach (ConfiguredProcessor cp in configured)
        {
            if (stoppingToken.IsCancellationRequested) break;

            if (cp.Processor is not null)
            {
                await cp.Processor.StartProcessingAsync(stoppingToken);
                lock (_processorLock) { _serviceBusProcessors.Add(cp.Processor); }
            }
            else if (cp.SessionProcessor is not null)
            {
                await cp.SessionProcessor.StartProcessingAsync(stoppingToken);
                lock (_processorLock) { _serviceBusSessionProcessors.Add(cp.SessionProcessor); }
            }
        }
    }

    private async Task WaitForApplicationStartup()
    {
        if (hostApplicationLifetime.ApplicationStarted.IsCancellationRequested)
            return;

        var tcs = new TaskCompletionSource();
        await using CancellationTokenRegistration reg =
            hostApplicationLifetime.ApplicationStarted.Register(tcs.SetResult);
        await tcs.Task;
    }

    private ConfiguredProcessor? SetupConsumer(Type consumerType)
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
                ConfigureSessionMessageHandler(sessionProcessor, consumerName, processorFactory, endpoint,
                    endpointDescription);
                sessionProcessor.ProcessErrorAsync +=
                    _telemetry.BuildErrorHandler(consumerType, endpointDescription, "ServiceBus:Session:Error");

                consumerSetupSpan.AddEvent("consumer.configured");
                consumerSetupSpan.SetStatus(Status.Ok);
                return new ConfiguredProcessor(null, sessionProcessor);
            }
            else
            {
                ServiceBusProcessor processor = CreateProcessor(endpoint);
                ConfigureMessageHandler(processor, consumerName, processorFactory, endpoint, endpointDescription);
                processor.ProcessErrorAsync +=
                    _telemetry.BuildErrorHandler(consumerType, endpointDescription, "ServiceBus:Error");

                consumerSetupSpan.AddEvent("consumer.configured");
                consumerSetupSpan.SetStatus(Status.Ok);
                return new ConfiguredProcessor(processor, null);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to setup ServiceBusConsumer: {ConsumerType}", consumerType.Name);

            using TelemetrySpan errorSpan = tracer.StartActiveSpan("ServiceBus:Setup:Error");
            errorSpan.SetAttribute("servicebus.consumer.type", consumerType.Name);
            errorSpan.RecordException(ex);
            errorSpan.SetStatus(Status.Error.WithDescription(ex.Message));
            return null;
        }
    }

    /// <summary>Holds a configured-but-not-yet-started processor ready for Phase 2 of startup.</summary>
    private sealed record ConfiguredProcessor(
        ServiceBusProcessor? Processor,
        ServiceBusSessionProcessor? SessionProcessor);

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
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
            SessionIdleTimeout = _worksOptions.SessionIdleTimeout
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
            Activity.Current = null;

            using IServiceScope scope = serviceScopeFactory.CreateScope();
            using ServiceBusTelemetry.MessageSpanResult result =
                _telemetry.CreateMessageSpan(consumerName, endpoint, args.Message);

            try
            {
                result.Span.AddEvent("message.processing.started");

                Func<ServiceBusReceivedMessage, CancellationToken, Task> process =
                    processorFactory(scope.ServiceProvider);

                await process(args.Message, args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);

                result.Span.AddEvent("message.completed");
                result.Span.SetStatus(Status.Ok);
            }
            catch (Exception ex)
            {
                _telemetry.HandleMessageProcessingError(result.Span, args, ex, endpointDescription);

                if (endpoint.MaxDeliveryCount > 0 && args.Message.DeliveryCount >= endpoint.MaxDeliveryCount)
                {
                    result.Span.AddEvent("message.deadlettered");
                    await args.DeadLetterMessageAsync(
                        args.Message,
                        deadLetterReason: "MaxDeliveryCountExceeded",
                        deadLetterErrorDescription:
                        $"Message exceeded the maximum delivery count of {endpoint.MaxDeliveryCount}. Last error: {ex.Message}",
                        cancellationToken: args.CancellationToken);
                }
                else
                {
                    result.Span.AddEvent("message.abandoned");
                    await args.AbandonMessageAsync(
                        args.Message,
                        propertiesToModify: ServiceBusTelemetry.BuildAbandonProperties(result.EnvelopeTraceparent),
                        cancellationToken: args.CancellationToken);
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
            Activity.Current = null;

            using IServiceScope scope = serviceScopeFactory.CreateScope();
            using ServiceBusTelemetry.MessageSpanResult result =
                _telemetry.CreateMessageSpan(consumerName, endpoint, args.Message);
            result.Span.SetAttribute("messaging.servicebus.session_id", args.SessionId);

            try
            {
                result.Span.AddEvent("message.processing.started");

                Func<ServiceBusReceivedMessage, CancellationToken, Task> process =
                    processorFactory(scope.ServiceProvider);

                await process(args.Message, args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);

                result.Span.AddEvent("message.completed");
                result.Span.SetStatus(Status.Ok);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error processing session message from {Endpoint}, SessionId: {SessionId}, MessageId: {MessageId}",
                    endpointDescription,
                    args.SessionId,
                    args.Message.MessageId);

                result.Span.RecordException(ex);
                result.Span.SetStatus(Status.Error.WithDescription(ex.Message));

                if (endpoint.MaxDeliveryCount > 0 && args.Message.DeliveryCount >= endpoint.MaxDeliveryCount)
                {
                    result.Span.AddEvent("message.deadlettered");
                    await args.DeadLetterMessageAsync(
                        args.Message,
                        deadLetterReason: "MaxDeliveryCountExceeded",
                        deadLetterErrorDescription:
                        $"Message exceeded the maximum delivery count of {endpoint.MaxDeliveryCount}. Last error: {ex.Message}",
                        cancellationToken: args.CancellationToken);
                }
                else
                {
                    result.Span.AddEvent("message.abandoned");
                    await args.AbandonMessageAsync(
                        args.Message,
                        propertiesToModify: ServiceBusTelemetry.BuildAbandonProperties(result.EnvelopeTraceparent),
                        cancellationToken: args.CancellationToken);
                }
            }
        };
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        List<ServiceBusProcessor> processorSnapshot;
        List<ServiceBusSessionProcessor> sessionSnapshot;
        lock (_processorLock)
        {
            processorSnapshot = [.. _serviceBusProcessors];
            _serviceBusProcessors.Clear();

            sessionSnapshot = [.. _serviceBusSessionProcessors];
            _serviceBusSessionProcessors.Clear();
        }

        foreach (ServiceBusProcessor processor in processorSnapshot)
        {
            try
            {
                await processor.StopProcessingAsync(cancellationToken);
                await processor.DisposeAsync();
            }
            catch (Exception ex) { logger.LogError(ex, "Error stopping ServiceBusProcessor"); }
        }

        foreach (ServiceBusSessionProcessor processor in sessionSnapshot)
        {
            try
            {
                await processor.StopProcessingAsync(cancellationToken);
                await processor.DisposeAsync();
            }
            catch (Exception ex) { logger.LogError(ex, "Error stopping ServiceBusSessionProcessor"); }
        }
    }
}
