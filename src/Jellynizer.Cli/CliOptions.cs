/// <summary>
/// Command line options. Anything not supplied falls back to appsettings.json / environment variables.
/// </summary>
internal sealed class CliOptions
{
    public CliOptions(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source" or "-s":
                    Source = ReadValue(args, ref i);
                    break;
                case "--destination" or "--dest" or "-d":
                    Destination = ReadValue(args, ref i);
                    break;
                case "--db" or "--history-db":
                    DatabasePath = ReadValue(args, ref i);
                    break;
                case "--dry-run" or "--plan":
                    DryRun = true;
                    break;
                case "--verbose" or "-v":
                    Verbose = true;
                    break;
                case "--quiet" or "-q":
                    Quiet = true;
                    break;
                case "--help" or "-h":
                    ShowHelp = true;
                    break;
                default:
                    Error ??= $"Unknown argument: {args[i]}";
                    break;
            }
        }
    }

    public string? Source { get; private set; }

    public string? Destination { get; private set; }

    public string? DatabasePath { get; private set; }

    public bool DryRun { get; private set; }

    public bool Verbose { get; private set; }

    public bool Quiet { get; private set; }

    public bool ShowHelp { get; private set; }

    public string? Error { get; private set; }

    private string? ReadValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            Error ??= $"Missing value for {args[index]}";
            return null;
        }

        return args[++index];
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            Jellynizer CLI - organize a messy media folder into a Jellyfin-friendly library.

            Usage:
              Jellynizer.Cli [options]

            Options:
              -s, --source <path>        Source folder to scan (overrides Jellynizer:SourceFolder)
              -d, --destination <path>   Destination root (overrides Jellynizer:DestinationFolder, defaults
                                         to the source folder for an in-place organize)
                  --db <path>            Move history SQLite database path
                  --dry-run, --plan      Print the move plan without touching any files
              -v, --verbose              Also print the source path of every item
              -q, --quiet                Only log warnings and errors
              -h, --help                 Show this help

            Configuration:
              appsettings.json (Jellynizer section) is read from the app directory.
              Environment variables override it, e.g. Jellynizer__SourceFolder=/media/downloads

            Examples:
              dotnet run --project src/Jellynizer.Cli -- --dry-run
              dotnet run --project src/Jellynizer.Cli -- --source ~/Downloads --destination ~/Media
            """);
    }
}
