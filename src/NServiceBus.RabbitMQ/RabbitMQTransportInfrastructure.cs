﻿namespace NServiceBus.Transport.RabbitMQ
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;
    using Janitor;
    using Performance.TimeToBeReceived;
    using Routing;
    using Settings;
    using RabbitMqNext;
    using Logging;

    [SkipWeaving]
    class RabbitMQTransportInfrastructure : TransportInfrastructure, IDisposable
    {
        readonly static ILog Logger = LogManager.GetLogger("RabbitMqTransport");
        readonly SettingsHolder settings;
        IRoutingTopology routingTopology;
        ConnectionFactory connectionFactory;
        ChannelProvider channelProvider;
        IConnection receiveConnection;

        public RabbitMQTransportInfrastructure(SettingsHolder settings, string connectionString)
        {
            LogAdapter.LogDebugFn = (@class, message, ex) => Logger.Debug($"{@class} {message}");
            LogAdapter.LogWarnFn = (@class, message, ex) => Logger.Warn($"{@class} {message}", ex);
            LogAdapter.LogErrorFn = (@class, message, ex) => Logger.Error($"{@class} {message}", ex);
            LogAdapter.IsDebugEnabled = Logger.IsDebugEnabled;
            LogAdapter.IsErrorEnabled = Logger.IsErrorEnabled;
            LogAdapter.IsWarningEnabled = Logger.IsWarnEnabled;

            this.settings = settings;
            var connectionConfiguration = new ConnectionStringParser(settings.EndpointName()).Parse(connectionString);

            routingTopology = CreateRoutingTopology();

            bool usePublisherConfirms;
            if (!settings.TryGet(SettingsKeys.UsePublisherConfirms, out usePublisherConfirms))
            {
                usePublisherConfirms = true;
            }

            connectionFactory = new ConnectionFactory(settings.InstanceSpecificQueue, connectionConfiguration);
            channelProvider = new ChannelProvider(connectionFactory, routingTopology, usePublisherConfirms);

            RequireOutboxConsent = false;
        }


        public override async Task Start()
        {
            Logger.Info("Starting RabbitMqNext transport");
            receiveConnection = await connectionFactory.CreateConnection("Receive").ConfigureAwait(false);
        }

        public override Task Stop()
        {
            Logger.Info("Stopping RabbitMqNext transport");
            receiveConnection.Dispose();
            return Task.CompletedTask;
        }


        public override IEnumerable<Type> DeliveryConstraints => new[] { typeof(DiscardIfNotReceivedBefore), typeof(NonDurableDelivery) };

        public override OutboundRoutingPolicy OutboundRoutingPolicy => new OutboundRoutingPolicy(OutboundRoutingType.Unicast, OutboundRoutingType.Multicast, OutboundRoutingType.Unicast);

        public override TransportTransactionMode TransactionMode => TransportTransactionMode.ReceiveOnly;

        public override EndpointInstance BindToLocalEndpoint(EndpointInstance instance) => instance;

        public override TransportReceiveInfrastructure ConfigureReceiveInfrastructure()
        {
            return new TransportReceiveInfrastructure(
                    () => CreateMessagePump(),
                    () => new QueueCreator(connectionFactory, routingTopology, settings.DurableMessagesEnabled()),
                    () => Task.FromResult(ObsoleteAppSettings.Check()));
        }

        public override TransportSendInfrastructure ConfigureSendInfrastructure()
        {
            return new TransportSendInfrastructure(
                () => new MessageDispatcher(channelProvider),
                () => Task.FromResult(StartupCheckResult.Success));
        }

        public override TransportSubscriptionInfrastructure ConfigureSubscriptionInfrastructure()
        {
            return new TransportSubscriptionInfrastructure(() => new SubscriptionManager(connectionFactory, routingTopology, settings.LocalAddress()));
        }

        public override string ToTransportAddress(LogicalAddress logicalAddress)
        {
            var queue = new StringBuilder(logicalAddress.EndpointInstance.Endpoint);

            if (logicalAddress.EndpointInstance.Discriminator != null)
            {
                queue.Append("-" + logicalAddress.EndpointInstance.Discriminator);
            }

            if (logicalAddress.Qualifier != null)
            {
                queue.Append("." + logicalAddress.Qualifier);
            }

            return queue.ToString();
        }

        public void Dispose()
        {
            channelProvider.Dispose();
        }
        IRoutingTopology CreateRoutingTopology()
        {
            var durable = settings.DurableMessagesEnabled();
            Func<bool, IRoutingTopology> topologyFactory;

            if (!settings.TryGet(out topologyFactory))
            {
                topologyFactory = d => new ConventionalRoutingTopology(d);
            }

            return topologyFactory(durable);
        }


        IPushMessages CreateMessagePump()
        {
            Logger.Debug("Creating message pump");
            MessageConverter messageConverter;

            if (settings.HasSetting(SettingsKeys.CustomMessageIdStrategy))
            {
                messageConverter = new MessageConverter(settings.Get<Func<MessageDelivery, string>>(SettingsKeys.CustomMessageIdStrategy));
            }
            else
            {
                messageConverter = new MessageConverter();
            }

            string hostDisplayName;
            if (!settings.TryGet("NServiceBus.HostInformation.DisplayName", out hostDisplayName))
            {
                hostDisplayName = Support.RuntimeEnvironment.MachineName;
            }

            var consumerTag = $"{hostDisplayName} - {settings.EndpointName()}";

            var queuePurger = new QueuePurger(connectionFactory);

            TimeSpan timeToWaitBeforeTriggeringCircuitBreaker;
            if (!settings.TryGet(SettingsKeys.TimeToWaitBeforeTriggeringCircuitBreaker, out timeToWaitBeforeTriggeringCircuitBreaker))
            {
                timeToWaitBeforeTriggeringCircuitBreaker = TimeSpan.FromMinutes(2);
            }

            int prefetchMultiplier;
            if (!settings.TryGet(SettingsKeys.PrefetchMultiplier, out prefetchMultiplier))
            {
                prefetchMultiplier = 3;
            }

            ushort prefetchCount;
            if (!settings.TryGet(SettingsKeys.PrefetchCount, out prefetchCount))
            {
                prefetchCount = 0;
            }

            return new MessagePump(receiveConnection, messageConverter, consumerTag, channelProvider, queuePurger, timeToWaitBeforeTriggeringCircuitBreaker, prefetchMultiplier, prefetchCount);
        }
    }
}
