using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace DialogflowChatApi.Controllers;

/// --------------SUMMARY-------------------
/// Dialogflow ES Fulfillment Webhook.
///
/// When an intent in the Dialogflow console has "Enable webhook call for this
/// intent" checked, Dialogflow will POST a WebhookRequest to a public URL we
/// provide in the Fulfillment tab. We respond with a WebhookResponse whose
/// fulfillmentText (or fulfillmentMessages) overrides the intent's default
/// response.
///
/// This controller demonstrates fulfillment for the flight-booking sample
/// conversation in the spec. Extend it with a switch on intent name for
/// additional intents (weather, order status, etc.).
///
/// NOTE: For Dialogflow to reach this endpoint it must be publicly reachable
/// over HTTPS. When developing locally, expose it with ngrok:
///     ngrok http 5000
/// then set the resulting https URL + "/api/webhook" as the Fulfillment URL.
/// 
[ApiController]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(ILogger<WebhookController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    public IActionResult HandleFulfillment([FromBody] JsonElement body)
    {
        // Dialogflow's WebhookRequest is large and mostly optional fields, so
        // we parse defensively with JsonElement rather than a strongly-typed DTO.
        string intent = TryGetString(body, "queryResult", "intent", "displayName") ?? "unknown";
        string queryText = TryGetString(body, "queryResult", "queryText") ?? string.Empty;

        _logger.LogInformation(
            "Fulfillment webhook hit: intent={Intent} queryText={Query}",
            intent,
            queryText);

        // Pull parameters (Dialogflow entities that were matched).
        var parameters = new Dictionary<string, string>();
        if (body.TryGetProperty("queryResult", out var queryResult)
            && queryResult.TryGetProperty("parameters", out var paramsElement)
            && paramsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in paramsElement.EnumerateObject())
            {
                parameters[prop.Name] = prop.Value.ToString();
            }
        }

        string fulfillmentText = BuildReply(intent, parameters);

        var response = new WebhookResponse
        {
            FulfillmentText = fulfillmentText,
            Source = "dialogflow-chat-api"
        };

        return Ok(response);
    }

    /// <summary>
    /// Demonstrates intent-specific fulfillment. Replace or extend as needed.
    /// </summary>
    private static string BuildReply(string intent, IReadOnlyDictionary<string, string> parameters)
    {
        return intent switch
        {
            "book.flight.confirm" when parameters.ContainsKey("from") && parameters.ContainsKey("to")
                => $"Great — I'll search for the best flights from {parameters["from"]} to {parameters["to"]}.",

            "book.flight.collect" when parameters.ContainsKey("from") && parameters.ContainsKey("to")
                => $"Let me confirm: a flight from {parameters["from"]} to {parameters["to"]}. Is that correct?",

            // Default: let Dialogflow's console-configured response stand.
            _ => string.Empty
        };
    }

    private static string? TryGetString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            if (!current.TryGetProperty(segment, out var next)) return null;
            current = next;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }
}

/// <summary>
/// Minimal Dialogflow WebhookResponse. Camel-cased to match the API contract.
/// See: https://cloud.google.com/dialogflow/es/docs/fulfillment-webhook
/// </summary>
public class WebhookResponse
{
    [JsonPropertyName("fulfillmentText")]
    public string FulfillmentText { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}
