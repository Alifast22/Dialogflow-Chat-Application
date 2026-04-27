using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DialogflowChatApi.Models;
using DialogflowChatApi.Services;

namespace DialogflowChatApi.WebSockets;

/// ----------------------SUMMARY---------------------
/// Runs the per-connection receive loop. For every JSON message the client
/// sends, this:
///   1. Parses the envelope.
///   2. Calls Dialogflow ES via the IDialogflowService.
///   3. Serializes Dialogflow's reply into an OutboundChatMessage and
///      writes it back over the same socket.
///
/// Each handled message is independent — a failure on one message does not
/// tear down the whole socket unless the socket itself errors.
/// </summary>
public sealed class ChatWebSocketHandler
{
    // 4 KB receive buffer is plenty for chat text. Larger messages are
    // reassembled via the EndOfMessage flag.
    private const int ReceiveBufferSize = 4 * 1024;

    // Protect against pathologically large messages (e.g. misuse / attack).
    private const int MaxMessageBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDialogflowService _dialogflow;
    private readonly IConnectionManager _connections;
    private readonly ILogger<ChatWebSocketHandler> _logger;

    public ChatWebSocketHandler(
        IDialogflowService dialogflow,
        IConnectionManager connections,
        ILogger<ChatWebSocketHandler> logger)
    {
        _dialogflow = dialogflow;
        _connections = connections;
        _logger = logger;
    }

    public async Task HandleAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var connectionId = _connections.Register(socket);
        _logger.LogInformation(
            "Registered WebSocket {ConnectionId} (active={Count})",
            connectionId,
            _connections.ActiveCount);

        // Greet the client so they know the socket is alive.
        await SendJsonAsync(socket, new OutboundChatMessage
        {
            Type = "system",
            Text = "Connected. Say hello!"
        }, cancellationToken);

        try
        {
            await ReceiveLoopAsync(socket, cancellationToken);
        }
        finally
        {
            _connections.Remove(connectionId);
            _logger.LogInformation(
                "Unregistered WebSocket {ConnectionId} (active={Count})",
                connectionId,
                _connections.ActiveCount);
        }
    }

    private async Task ReceiveLoopAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var messageStream = new MemoryStream();
            WebSocketReceiveResult? result;

            // Reassemble fragmented messages.
            do
            {
                try
                {
                    result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (WebSocketException ex)
                {
                    _logger.LogWarning(ex, "WebSocket receive failed; closing loop.");
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation(
                        "Client initiated close: {Status} {Description}",
                        result.CloseStatus,
                        result.CloseStatusDescription);

                    if (socket.State == WebSocketState.CloseReceived)
                    {
                        await socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    return;
                }

                messageStream.Write(buffer, 0, result.Count);

                if (messageStream.Length > MaxMessageBytes)
                {
                    _logger.LogWarning(
                        "Client exceeded max message size ({Max} bytes); closing connection.",
                        MaxMessageBytes);

                    await socket.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        "Message too large",
                        CancellationToken.None).ConfigureAwait(false);
                    return;
                }
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                // Binary frames aren't part of this protocol.
                continue;
            }

            var payload = Encoding.UTF8.GetString(messageStream.ToArray());
            await ProcessIncomingAsync(socket, payload, cancellationToken);
        }
    }

    private async Task ProcessIncomingAsync(
        WebSocket socket,
        string payload,
        CancellationToken cancellationToken)
    {
        InboundChatMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<InboundChatMessage>(payload, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Received malformed JSON from client: {Payload}", payload);
            await SendJsonAsync(socket, new OutboundChatMessage
            {
                Type = "error",
                Text = "Malformed message: expected JSON with sessionId + text."
            }, cancellationToken);
            return;
        }

        if (message is null || string.IsNullOrWhiteSpace(message.Text))
        {
            await SendJsonAsync(socket, new OutboundChatMessage
            {
                Type = "error",
                Text = "Empty message."
            }, cancellationToken);
            return;
        }

        // Fall back to a server-generated session id if the client didn't supply
        // one — but warn, because it will break multi-turn context tracking.
        var sessionId = string.IsNullOrWhiteSpace(message.SessionId)
            ? Guid.NewGuid().ToString("N")
            : message.SessionId;

        try
        {
            var reply = await _dialogflow.DetectIntentAsync(
                sessionId,
                message.Text,
                cancellationToken);

            await SendJsonAsync(socket, new OutboundChatMessage
            {
                Type = "bot",
                Text = reply.FulfillmentText,
                Intent = reply.IntentDisplayName,
                Confidence = reply.IntentConfidence
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Catch-all so a Dialogflow hiccup doesn't take down the socket.
            _logger.LogError(ex, "Dialogflow DetectIntent failed");

            await SendJsonAsync(socket, new OutboundChatMessage
            {
                Type = "error",
                Text = "Sorry, the bot is temporarily unavailable. Please try again."
            }, cancellationToken);
        }
    }

    private static async Task SendJsonAsync(
        WebSocket socket,
        OutboundChatMessage message,
        CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }
}
