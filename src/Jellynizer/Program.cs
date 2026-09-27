using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Jellynizer.Cleanup;
using Jellynizer.Configuration;
using Jellynizer.Discovery;
using Jellynizer.Endpoints;
using Jellynizer.Execution;
using Jellynizer.Helpers;
using Jellynizer.History;
using Jellynizer.Logging;
using Jellynizer.Orchestration;
using Jellynizer.Parsing;
using Jellynizer.Planning;
using Jellynizer.Torrents;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on port 45263
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(45263);
});

// Add background service
builder.Services.AddSingleton<JobExecutor>();
builder.Services.Configure<JellynizerOptions>(builder.Configuration.GetSection("Jellynizer"));
builder.Services.AddSingleton<IFileSystem, PhysicalFileSystem>();
builder.Services.AddSingleton<VideoFileFinder>();
builder.Services.AddSingleton<MediaGrouper>();
builder.Services.AddSingleton<MovePlanBuilder>();
builder.Services.AddSingleton<VideoMover>();
builder.Services.AddSingleton<SubtitleMover>();
builder.Services.AddSingleton<DirectoryCleaner>();

var dbPath = MoveHistoryStore.ResolveDatabasePath(
    builder.Configuration.GetValue<string>("Jellynizer:MoveHistoryDatabasePath") ?? "");
builder.Services.AddDbContextFactory<MoveHistoryDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton<MoveHistoryStore>();
builder.Services.AddSingleton<MediaFileOrganizer>();

// Torrents: accepts .torrent uploads and hands them to qBittorrent to download.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ITorrentClient, QbittorrentClient>();
builder.Services.AddSingleton<TorrentService>();

// Live log streaming (SSE)
builder.Services.AddSingleton<LogBroadcaster>();
builder.Services.AddSingleton<ILoggerProvider, BroadcastLoggerProvider>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.EndpointPathPrefix = "/scalar/{documentName}";
});
app.MapGet("/scalar", () => Results.Redirect("/scalar/v1", permanent: false));

app.MapJobEndpoints();
app.MapHistoryEndpoints();
app.MapFileManagementEndpoints();
app.MapSystemEndpoints();
app.MapTorrentEndpoints();

app.Run();
