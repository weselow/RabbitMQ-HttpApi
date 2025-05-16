using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
// using RabbitMQ.Client.Framing; // Этот using больше не нужен для кодов
using Microsoft.Extensions.Options;
using System.Text;
using RabbitMqApi.Configuration;

namespace RabbitMqApi.Services;

public class RabbitService : IDisposable
{
    private readonly IConnection _connection;
    private IModel? _channel;
    private readonly RabbitMqConfig _config;
    private readonly ILogger<RabbitService> _logger;
    private readonly object _channelLock = new();

    public RabbitService(IOptions<RabbitMqConfig> rabbitMqOptions, ILogger<RabbitService> logger)
    {
        _config = rabbitMqOptions.Value;
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = _config.Host,
            Port = _config.Port,
            UserName = _config.Username,
            Password = _config.Password,
            VirtualHost = _config.VirtualHost,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        try
        {
            _logger.LogInformation("Attempting to connect to RabbitMQ host {Host}:{Port}, VirtualHost: '{VirtualHost}'", _config.Host, _config.Port, _config.VirtualHost);
            _connection = factory.CreateConnection();

            _connection.ConnectionShutdown += OnConnectionShutdown;
            if (_connection is IAutorecoveringConnection autorecoveringConnection)
            {
                autorecoveringConnection.RecoverySucceeded += OnRecoverySucceeded;
                autorecoveringConnection.ConnectionRecoveryError += OnConnectionRecoveryError;
                _logger.LogInformation("Subscribed to IAutorecoveringConnection recovery events.");
            }
            else
            {
                _logger.LogWarning("Connection is not IAutorecoveringConnection, advanced recovery events are not available. AutomaticRecoveryEnabled on factory was {AutoRecoveryEnabled}.", factory.AutomaticRecoveryEnabled);
            }
            _logger.LogInformation("Successfully connected to RabbitMQ host {Host}:{Port}", _config.Host, _config.Port);
        }
        catch (BrokerUnreachableException ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ host {Host}:{Port}. Broker unreachable.", _config.Host, _config.Port);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while connecting to RabbitMQ host {Host}:{Port}", _config.Host, _config.Port);
            throw;
        }
    }

    private void OnConnectionShutdown(object? sender, ShutdownEventArgs args)
    {
        _logger.LogWarning("RabbitMQ connection shut down. Initiator: {Initiator}, ReplyCode: {ReplyCode}, Reason: {ReplyText}", args.Initiator, args.ReplyCode, args.ReplyText);
        lock (_channelLock)
        {
            if (_channel != null)
            {
                _channel.ModelShutdown -= OnChannelShutdown;
                _channel = null;
                _logger.LogDebug("Shared channel instance reset due to connection shutdown.");
            }
        }
    }

    private void OnRecoverySucceeded(object? sender, EventArgs args)
    {
        _logger.LogInformation("RabbitMQ connection recovery succeeded.");
    }

    private void OnConnectionRecoveryError(object? sender, ConnectionRecoveryErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "RabbitMQ connection recovery failed.");
    }

    private void OnChannelShutdown(object? sender, ShutdownEventArgs args)
    {
        _logger.LogWarning("RabbitMQ channel (hashCode: {ChannelHashCode}) shut down. Initiator: {Initiator}, ReplyCode: {ReplyCode}, Reason: {ReplyText}",
            sender?.GetHashCode(), args.Initiator, args.ReplyCode, args.ReplyText);
        lock (_channelLock)
        {
            if (sender == _channel)
            {
                _channel.ModelShutdown -= OnChannelShutdown;
                _channel = null;
                _logger.LogDebug("Shared channel instance was shut down and has been reset.");
            }
        }
    }

    private IModel GetChannel()
    {
        lock (_channelLock)
        {
            if (!_connection.IsOpen)
            {
                _logger.LogWarning("RabbitMQ connection is not open. Current channel (if any) is invalid. Waiting for auto-recovery or next CreateModel attempt.");
                if (_channel != null)
                {
                    _logger.LogDebug("Connection is closed, ensuring shared channel is nullified (if not already). Channel HashCode: {ChannelHashCode}", _channel.GetHashCode());
                    _channel.ModelShutdown -= OnChannelShutdown;
                    _channel = null;
                }
            }

            if (_channel != null && _channel.IsOpen && _connection.IsOpen)
            {
                _logger.LogTrace("Reusing existing open channel (HashCode: {ChannelHashCode}).", _channel.GetHashCode());
                return _channel;
            }

            _logger.LogDebug("Need to create or recreate channel. Current channel is null: {IsChannelNull}, IsOpen: {IsChannelOpen}. Connection IsOpen: {IsConnectionOpen}",
                _channel == null, _channel?.IsOpen, _connection.IsOpen);

            if (_channel != null)
            {
                _logger.LogDebug("Disposing previous channel (HashCode: {ChannelHashCode}, IsClosed: {IsChannelClosed}) before creating a new one.",
                                 _channel.GetHashCode(), _channel.IsClosed);
                _channel.ModelShutdown -= OnChannelShutdown;
                try { if (_channel.IsOpen) _channel.Close(); } catch { /* Игнорируем ошибки */ }
                _channel.Dispose();
                _channel = null;
            }

            try
            {
                _logger.LogDebug("Attempting to create a new channel. ConnectionIsOpen: {IsConnOpen}", _connection.IsOpen);
                if (!_connection.IsOpen)
                {
                    _logger.LogError("Cannot create channel because RabbitMQ connection is not open. Auto-recovery might be in progress or failed.");
                }
                _channel = _connection.CreateModel();
                _logger.LogInformation("New RabbitMQ channel created successfully (HashCode: {ChannelHashCode}).", _channel.GetHashCode());
                _channel.ModelShutdown += OnChannelShutdown;

                if (!_channel.IsOpen)
                {
                    _logger.LogWarning("Channel (HashCode: {ChannelHashCode}) was created but is immediately reported as not open. ConnectionIsOpen: {IsConnOpen}. This is unusual.", _channel.GetHashCode(), _connection.IsOpen);
                }
            }
            catch (AlreadyClosedException ace)
            {
                _logger.LogError(ace, "Failed to create model because the connection or channel is already closed. Connection IsOpen: {IsOpen}", _connection.IsOpen);
                _channel = null;
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while creating RabbitMQ model. Connection IsOpen: {IsOpen}", _connection.IsOpen);
                _channel = null;
                throw;
            }
            return _channel;
        }
    }

    private void EnsureQueueDeclared(IModel channel, string queueName)
    {
        const ushort PRECONDITION_FAILED = 406;
        const ushort RESOURCE_LOCKED = 405;
        const ushort ACCESS_REFUSED = 403; // Добавим на всякий случай

        try
        {
            channel.QueueDeclare(queue: queueName,
                                 durable: true,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);
            _logger.LogInformation("Queue '{QueueName}' declared successfully on channel (HashCode: {ChannelHashCode}) (or already existed with compatible parameters).", queueName, channel.GetHashCode());
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == PRECONDITION_FAILED)
        {
            _logger.LogError(ex, "Failed to declare queue '{QueueName}' on channel (HashCode: {ChannelHashCode}). It already exists with incompatible parameters. API expects a durable, non-exclusive, non-auto-delete queue. Server Reply Code: {ReplyCode}, Text: {ReplyText}",
                queueName, channel.GetHashCode(), ex.ShutdownReason.ReplyCode, ex.ShutdownReason.ReplyText);
            throw new InvalidOperationException($"Queue '{queueName}' exists with incompatible parameters. Expected durable, non-exclusive, non-auto-delete. Server Reply: ({ex.ShutdownReason.ReplyCode}) {ex.ShutdownReason.ReplyText}", ex);
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == RESOURCE_LOCKED)
        {
            _logger.LogError(ex, "Failed to declare queue '{QueueName}' on channel (HashCode: {ChannelHashCode}). It is exclusively used by another connection. Server Reply Code: {ReplyCode}, Text: {ReplyText}",
               queueName, channel.GetHashCode(), ex.ShutdownReason.ReplyCode, ex.ShutdownReason.ReplyText);
            throw new InvalidOperationException($"Queue '{queueName}' is locked by another connection. Server Reply: ({ex.ShutdownReason.ReplyCode}) {ex.ShutdownReason.ReplyText}", ex);
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == ACCESS_REFUSED)
        {
            _logger.LogError(ex, "Failed to declare queue '{QueueName}' on channel (HashCode: {ChannelHashCode}). Access was refused by the server. Server Reply Code: {ReplyCode}, Text: {ReplyText}",
               queueName, channel.GetHashCode(), ex.ShutdownReason.ReplyCode, ex.ShutdownReason.ReplyText);
            throw new InvalidOperationException($"Access to declare queue '{queueName}' was refused. Server Reply: ({ex.ShutdownReason.ReplyCode}) {ex.ShutdownReason.ReplyText}", ex);
        }
        catch (AlreadyClosedException ace)
        {
            _logger.LogError(ace, "Failed to declare queue '{QueueName}' on channel (HashCode: {ChannelHashCode}). The channel or connection was closed.", queueName, channel.GetHashCode());
            throw;
        }
        catch (Exception ex) // Общий перехват для других OperationInterruptedException или других ошибок
        {
            if (ex is OperationInterruptedException oie && oie.ShutdownReason != null)
            {
                _logger.LogError(oie, "OperationInterruptedException while declaring queue '{QueueName}' on channel (HashCode: {ChannelHashCode}). Server Reply Code: {ReplyCode}, Text: {ReplyText}",
                   queueName, channel.GetHashCode(), oie.ShutdownReason.ReplyCode, oie.ShutdownReason.ReplyText);
            }
            else
            {
                _logger.LogError(ex, "Generic error declaring queue '{QueueName}' on channel (HashCode: {ChannelHashCode})", queueName, channel.GetHashCode());
            }
            throw;
        }
    }

    public (byte[]? Body, ulong DeliveryTag, bool MessageFound) GetMessage(string queueName)
    {
        IModel channel;
        try
        {
            channel = GetChannel();
            EnsureQueueDeclared(channel, queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare channel or queue '{QueueName}' for getting a message.", queueName);
            throw;
        }

        try
        {
            BasicGetResult? result = channel.BasicGet(queueName, autoAck: false);

            if (result == null)
            {
                _logger.LogInformation("No message found in queue '{QueueName}' on channel (HashCode: {ChannelHashCode}).", queueName, channel.GetHashCode());
                return (null, 0, false);
            }
            _logger.LogInformation("Message retrieved from queue '{QueueName}' (DeliveryTag: {DeliveryTag}) on channel (HashCode: {ChannelHashCode}).", queueName, result.DeliveryTag, channel.GetHashCode());
            return (result.Body.ToArray(), result.DeliveryTag, true);
        }
        catch (AlreadyClosedException ace)
        {
            _logger.LogError(ace, "Error getting message from queue '{QueueName}' on channel (HashCode: {ChannelHashCode}). Channel or connection was closed.", queueName, channel.GetHashCode());
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "General error getting message from queue '{QueueName}' on channel (HashCode: {ChannelHashCode})", queueName, channel.GetHashCode());
            throw;
        }
    }

    public void AckMessage(ulong deliveryTag)
    {
        IModel? currentChannelInstance;
        lock (_channelLock)
        {
            currentChannelInstance = _channel;
        }

        if (currentChannelInstance == null || currentChannelInstance.IsClosed)
        {
            _logger.LogError("Cannot ACK message {DeliveryTag}. Channel is null or closed. This message might be redelivered.", deliveryTag);
            return;
        }

        try
        {
            currentChannelInstance.BasicAck(deliveryTag, false);
            _logger.LogInformation("Message with DeliveryTag {DeliveryTag} acknowledged on channel (HashCode: {ChannelHashCode}).", deliveryTag, currentChannelInstance.GetHashCode());
        }
        catch (AlreadyClosedException ace)
        {
            _logger.LogError(ace, "Failed to ACK message {DeliveryTag} on channel (HashCode: {ChannelHashCode}). Channel or connection was closed. The message might be redelivered.",
                deliveryTag, currentChannelInstance.GetHashCode());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acknowledging message with DeliveryTag {DeliveryTag} on channel (HashCode: {ChannelHashCode}). The message might be redelivered.",
                deliveryTag, currentChannelInstance.GetHashCode());
        }
    }

    public bool PublishMessage(string queueName, string messageBody, string? contentType)
    {
        IModel channel;
        try
        {
            channel = GetChannel();
            EnsureQueueDeclared(channel, queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare channel or queue '{QueueName}' for publishing a message.", queueName);
            return false;
        }

        try
        {
            var body = Encoding.UTF8.GetBytes(messageBody);
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            if (!string.IsNullOrEmpty(contentType))
            {
                properties.ContentType = contentType;
            }

            channel.BasicPublish(exchange: string.Empty,
                                 routingKey: queueName,
                                 basicProperties: properties,
                                 body: body);
            _logger.LogInformation("Message published to queue '{QueueName}' on channel (HashCode: {ChannelHashCode}).", queueName, channel.GetHashCode());
            return true;
        }
        catch (AlreadyClosedException ace)
        {
            _logger.LogError(ace, "Error publishing message to queue '{QueueName}' on channel (HashCode: {ChannelHashCode}). Channel or connection was closed.", queueName, channel.GetHashCode());
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "General error publishing message to queue '{QueueName}' on channel (HashCode: {ChannelHashCode})", queueName, channel.GetHashCode());
            return false;
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing RabbitService...");

        if (_connection != null)
        {
            _connection.ConnectionShutdown -= OnConnectionShutdown;
            if (_connection is IAutorecoveringConnection autorecoveringConnection)
            {
                autorecoveringConnection.RecoverySucceeded -= OnRecoverySucceeded;
                autorecoveringConnection.ConnectionRecoveryError -= OnConnectionRecoveryError;
            }
        }

        lock (_channelLock)
        {
            if (_channel != null)
            {
                _channel.ModelShutdown -= OnChannelShutdown;
                try
                {
                    if (_channel.IsOpen) _channel.Close();
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Error closing RabbitMQ channel (HashCode: {ChannelHashCode}) during dispose.", _channel.GetHashCode()); }
                finally { _channel.Dispose(); _channel = null; }
            }
        }

        try
        {
            if (_connection != null && _connection.IsOpen)
            {
                _connection.Close();
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Error closing RabbitMQ connection during dispose."); }
        finally
        {
            (_connection as IDisposable)?.Dispose();
        }
        _logger.LogInformation("RabbitService disposed.");
    }
}