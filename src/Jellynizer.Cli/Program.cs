using Jellynizer.Cleanup;
using Jellynizer.Configuration;
using Jellynizer.Discovery;
using Jellynizer.Execution;
using Jellynizer.Helpers;
using Jellynizer.History;
using Jellynizer.Orchestration;
using Jellynizer.Parsing;
using Jellynizer.Planning;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var cli = new CliOptions(args);

if (cli.Error is not null)
{
    Console.Error.WriteLine(cli.Error);
    Console.Error.WriteLine();
    CliOptions.PrintUsage();
    return 1;
}

if (cli.ShowHelp)
{
    CliOptions.PrintUsage();
    return 0;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var options = configuration.GetSection("Jellynizer").Get<JellynizerOptions>() ?? new JellynizerOptions();
options.SourceFolder = cli.Source ?? options.SourceFolder;
options.DestinationFolder = cli.Destination ?? options.DestinationFolder;
if (cli.DatabasePath is not null)
{
    options.MoveHistoryDatabasePath = cli.DatabasePath;
}

// "organize in place": an empty destination must behave like an unset one.
if (string.IsNullOrWhiteSpace(options.DestinationFolder))
{
    options.DestinationFolder = null;
}

if (string.IsNullOrWhiteSpace(options.SourceFolder))
{
    Console.Error.WriteLine("No source folder configured. Set Jellynizer:SourceFolder in appsettings.json or pass --source <path>.");
    return 1;
}

var sourceFolder = Path.GetFullPath(options.SourceFolder);
if (!Directory.Exists(sourceFolder))
{
    Console.Error.WriteLine($"Source folder does not exist: {sourceFolder}");
    return 1;
}

var destinationFolder = string.IsNullOrWhiteSpace(options.DestinationFolder)
    ? sourceFolder
    : Path.GetFullPath(options.DestinationFolder);

var databasePath = MoveHistoryStore.ResolveDatabasePath(options.MoveHistoryDatabasePath);

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddSimpleConsole(console =>
    {
        console.SingleLine = true;
        console.TimestampFormat = "HH:mm:ss ";
    });
    builder.SetMinimumLevel(cli.Quiet ? LogLevel.Warning : LogLevel.Information);

    // EF Core logs every SQL statement at Information; keep the output readable.
    builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
});

// Same services and options as the web app (Program.cs), so the behaviour is identical.
services.AddSingleton(Options.Create(options));
services.AddSingleton<IFileSystem, PhysicalFileSystem>();
services.AddSingleton<VideoFileFinder>();
services.AddSingleton<MediaGrouper>();
services.AddSingleton<MovePlanBuilder>();
services.AddSingleton<VideoMover>();
services.AddSingleton<SubtitleMover>();
services.AddSingleton<DirectoryCleaner>();
services.AddSingleton<MoveHistoryStore>();
services.AddSingleton<MediaFileOrganizer>();
services.AddDbContextFactory<MoveHistoryDbContext>(db => db.UseSqlite($"Data Source={databasePath}"));

using var provider = services.BuildServiceProvider();

Console.WriteLine("Jellynizer CLI");
Console.WriteLine($"  Source:      {sourceFolder}");
Console.WriteLine($"  Destination: {destinationFolder}{(string.Equals(sourceFolder, destinationFolder, StringComparison.Ordinal) ? "  (in place)" : string.Empty)}");
Console.WriteLine($"  History DB:  {databasePath}");
Console.WriteLine($"  Mode:        {(cli.DryRun ? "dry run - no files are moved or renamed" : "move files")}");
Console.WriteLine();

try
{
    if (cli.DryRun)
    {
        PrintPlan(provider, options, sourceFolder, destinationFolder, cli.Verbose);
        return 0;
    }

    var organizer = provider.GetRequiredService<MediaFileOrganizer>();
    var summary = await organizer.OrganizeAsync(sourceFolder);

    Console.WriteLine();
    Console.WriteLine(
        $"Processed {summary.TotalFiles} video file(s): moved {summary.MovedFiles}, skipped {summary.SkippedFiles}. " +
        $"Subtitles moved: {summary.SubtitlesMoved}. Leftover files removed: {summary.LeftoverFilesRemoved}.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Failed: {ex.Message}");
    return 1;
}
static void PrintPlan(
    IServiceProvider provider,
    JellynizerOptions options,
    string sourceFolder,
    string destinationFolder,
    bool verbose)
{
    var fileFinder = provider.GetRequiredService<VideoFileFinder>();
    var mediaGrouper = provider.GetRequiredService<MediaGrouper>();

    // Dry run: don't delete qBittorrent trash markers while scanning.
    var videoFiles = fileFinder.GetVideoFiles(sourceFolder, options.VideoExtensions, deleteTrashFiles: false);
    var mediaObjects = mediaGrouper.GroupMediaFiles(videoFiles);

    var shows = mediaObjects
        .Where(m => m.Type == MediaType.Show)
        .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
    var movies = mediaObjects
        .Where(m => m.Type == MediaType.Movie)
        .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var episodeCount = shows.Sum(s => s.Seasons.Sum(season => season.Episodes.Count));

    Console.WriteLine($"Found {videoFiles.Count} video file(s): {shows.Count} show(s) / {episodeCount} episode(s), {movies.Count} movie(s).");
    Console.WriteLine();

    foreach (var movie in movies)
    {
        var moviePath = movie.MoviePath!;
        var target = MovePlanBuilder.ResolveDestinationPath(moviePath, movie, destinationFolder);

        Console.WriteLine($"MOVIE  {movie.Name}");
        if (verbose)
        {
            Console.WriteLine($"       from: {Relative(sourceFolder, moviePath)}");
        }

        Console.WriteLine($"       to:   {Relative(destinationFolder, target)}");
        Console.WriteLine();
    }

    foreach (var show in shows)
    {
        var showEpisodeCount = show.Seasons.Sum(s => s.Episodes.Count);
        Console.WriteLine($"SHOW   {show.Name}  ({show.Seasons.Count} season(s), {showEpisodeCount} episode(s))");

        foreach (var season in show.Seasons.OrderBy(s => s.SeasonNumber))
        {
            Console.WriteLine($"       Season {season.SeasonNumber:00}");

            foreach (var episode in season.Episodes.OrderBy(e => e.EpisodeNumber))
            {
                var target = MovePlanBuilder.ResolveDestinationPath(episode.Path, show, destinationFolder);
                Console.WriteLine($"         S{season.SeasonNumber:00}E{episode.EpisodeNumber:00} -> {Relative(destinationFolder, target)}");

                if (verbose)
                {
                    Console.WriteLine($"                     from: {Relative(sourceFolder, episode.Path)}");
                }
            }
        }

        Console.WriteLine();
    }
}

static string Relative(string root, string path)
{
    var relative = Path.GetRelativePath(root, path);
    return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
}
