using Google.Apis.Auth.OAuth2;
using Google.Cloud.Dialogflow.V2;
using Grpc.Auth;

namespace DialogflowChatApi.Services;


//  Concrete Dialogflow ES client. Uses the Google-supplied SessionsClient
//  which speaks gRPC under the hood (the SDK wraps the REST + gRPC APIs).

//  Authentication: a service-account JSON key file. The path is configured
//  in appsettings.json under Dialogflow:CredentialsPath, or via the standard
//  GOOGLE_APPLICATION_CREDENTIALS environment variable.

public sealed class DialogflowService : IDialogflowService
{
    private readonly SessionsClient _sessionsClient;
    private readonly string _projectId;
    private readonly string _languageCode;
    private readonly ILogger<DialogflowService> _logger;

    public DialogflowService(IConfiguration configuration, ILogger<DialogflowService> logger)
    {
        _logger = logger;

        _projectId = configuration["Dialogflow:ProjectId"]
            ?? throw new InvalidOperationException(
                "Dialogflow:ProjectId is not configured. Set it in appsettings.json.");

        _languageCode = configuration["Dialogflow:LanguageCode"] ?? "en-US";

        var credentialsPath = configuration["Dialogflow:CredentialsPath"];

        // Two authentication paths are supported:
        //  1. An explicit service-account JSON file (preferred for local dev).
        //  2. Application Default Credentials via GOOGLE_APPLICATION_CREDENTIALS
        //     or workload identity (preferred for cloud deployments).
        if (!string.IsNullOrWhiteSpace(credentialsPath) && File.Exists(credentialsPath))
        {
            _logger.LogInformation("Loading Dialogflow credentials from {Path}", credentialsPath);

            var credential = GoogleCredential
                .FromFile(credentialsPath)
                .CreateScoped("https://www.googleapis.com/auth/cloud-platform");

            _sessionsClient = new SessionsClientBuilder
            {
                ChannelCredentials = credential.ToChannelCredentials()
            }.Build();
        }
        else
        {
            _logger.LogInformation(
                "No explicit credentials file found; falling back to Application Default Credentials.");
            _sessionsClient = SessionsClient.Create();
        }
    }

    public async Task<DialogflowReply> DetectIntentAsync(
        string sessionId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            // Don't waste a round-trip on empty input.
            return new DialogflowReply(string.Empty, "Default Fallback Intent", 0f);
        }

        var session = SessionName.FromProjectSession(_projectId, sessionId);

        var queryInput = new QueryInput
        {
            Text = new TextInput
            {
                Text = text,
                LanguageCode = _languageCode
            }
        };

        _logger.LogDebug("→ Dialogflow [session={Session}]: {Text}", sessionId, text);

        var response = await _sessionsClient.DetectIntentAsync(
            session,
            queryInput,
            cancellationToken).ConfigureAwait(false);

        var result = response.QueryResult;

        _logger.LogDebug(
            "← Dialogflow [intent={Intent} confidence={Confidence:F2}]: {Reply}",
            result.Intent?.DisplayName,
            result.IntentDetectionConfidence,
            result.FulfillmentText);

        return new DialogflowReply(
            FulfillmentText: string.IsNullOrWhiteSpace(result.FulfillmentText)
                ? "I'm not sure how to respond to that yet."
                : result.FulfillmentText,
            IntentDisplayName: result.Intent?.DisplayName ?? "unknown",
            IntentConfidence: result.IntentDetectionConfidence);
    }
}
