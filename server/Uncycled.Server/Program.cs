using Uncycled.Server.Bots;
using Uncycled.Server.Content;
using Uncycled.Server.Net;
using Uncycled.Server.Rooms;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5000");
}

string contentDir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
ContentBundle content = ContentLoader.Load(contentDir);

int maxRooms = 20;
if (int.TryParse(Environment.GetEnvironmentVariable("UNCYCLED_MAX_ROOMS"), out int parsedMax) && parsedMax > 0)
{
    maxRooms = parsedMax;
}

builder.Services.AddSingleton(content);
string botsDir = BotBrains.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_BOTS_DIR"));
string? botsBrain = Environment.GetEnvironmentVariable("UNCYCLED_BOTS_BRAIN");
builder.Services.AddSingleton(sp => BotBrains.Load(botsDir, content, sp.GetRequiredService<ILoggerFactory>().CreateLogger("Bots"), botsBrain));
builder.Services.AddSingleton(sp => new RoomManager(content, maxRooms, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<BotBrains>()));

WebApplication app = builder.Build();
app.Services.GetRequiredService<BotBrains>(); // loads the bot models now, not on the first match
app.Logger.LogInformation("Content loaded from {Dir} (map {Map} {Version})", contentDir, content.MapId, content.MapVersion);
app.Logger.LogInformation("Bots from {Dir}", botsDir);

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

app.MapGet("/health", () => Results.Text("ok " + Codec.ServerBuild));
app.MapGet("/stats", (RoomManager rooms) => Results.Json(rooms.Stats()));
app.Map("/ws", async (HttpContext ctx, RoomManager rooms, ILoggerFactory loggers) =>
{
    await WsHandler.HandleAsync(ctx, rooms, loggers.CreateLogger("Ws"));
});

app.Run();
