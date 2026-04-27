using DialogflowChatApi.Services;
using DialogflowChatApi.WebSockets;

DotNetEnv.Env.Load();
var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// Service registration
// -----------------------------------------------------------------------------

// Controllers expose the Dialogflow fulfillment webhook endpoint.
builder.Services.AddControllers();

// Swagger/OpenAPI — useful when developing / testing the webhook endpoint.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Dialogflow client — singleton because creating a SessionsClient is expensive
// and it is fully thread-safe.
builder.Services.AddSingleton<IDialogflowService, DialogflowService>();

// Tracks active WebSocket connections so we can route Dialogflow responses
// back to the correct client.
builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();

// Handles the per-connection message loop.
builder.Services.AddSingleton<ChatWebSocketHandler>();

// CORS — in development we allow the Vite dev server (localhost:5173) and
// CRA-style (localhost:3000). Tighten this for production deployments.
const string CorsPolicy = "ChatClientPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://localhost:3000",
                "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// -----------------------------------------------------------------------------
// HTTP pipeline
// -----------------------------------------------------------------------------

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(CorsPolicy);

// Enable WebSocket support. KeepAliveInterval keeps idle connections open
// through proxies / load balancers.
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
};
app.UseWebSockets(webSocketOptions);

// Mount the chat WebSocket endpoint at /ws/chat.
// Any client that upgrades to WebSocket on this path is handed off to the
// ChatWebSocketHandler which runs a message loop until the socket closes.
app.Map("/ws/chat", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected a WebSocket upgrade request.");
        return;
    }

    var handler = context.RequestServices.GetRequiredService<ChatWebSocketHandler>();
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    logger.LogInformation("WebSocket client connected from {Remote}", context.Connection.RemoteIpAddress);

    try
    {
        await handler.HandleAsync(socket, context.RequestAborted);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled error in WebSocket handler");
    }
    finally
    {
        logger.LogInformation("WebSocket client disconnected");
    }
});

// Simple health-check so the frontend (and load balancers) can verify the
// backend is alive before attempting a WebSocket upgrade.
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.MapControllers();

app.Run();
