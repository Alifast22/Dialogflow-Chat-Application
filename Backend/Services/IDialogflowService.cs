using Google.Cloud.Dialogflow.V2;

namespace DialogflowChatApi.Services;

/// ------------------Summary------------------
/// Abstraction over the Dialogflow ES client. Kept as an interface so that
/// the WebSocket handler can be unit-tested without a live Google project.
/// </summary>
public interface IDialogflowService
{
    /// <summary>
    /// Sends the user's text to Dialogflow ES and returns the fulfillment
    /// response (plus intent metadata for client-side display).
    /// </summary>
    /// <param name="sessionId">Per-conversation id.</param>
    /// <param name="text">Raw user utterance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DialogflowReply> DetectIntentAsync(
        string sessionId,
        string text,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Flattened result from a Dialogflow DetectIntent call.
/// </summary>
public record DialogflowReply(
    string FulfillmentText,
    string IntentDisplayName,
    float IntentConfidence);
