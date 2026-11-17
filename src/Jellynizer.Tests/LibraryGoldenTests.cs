using System.Text.RegularExpressions;

using Jellynizer.Parsing;
using Jellynizer.Planning;

using Xunit;

namespace Jellynizer.Tests;

/// <summary>
/// Loads <c>TestData/library-map.tsv</c>: for every video file of a real, messy downloads tree it
/// stores the destination it must get in the library. Paths are relative to <see cref="Root"/>, so
/// the tests are machine independent and never depend on the folders they were derived from.
/// </summary>
internal static class LibraryFixture
{
    /// <summary>Synthetic root used to turn the relative fixture paths into absolute ones.</summary>
    public const string Root = "/media/library-test";

    private static readonly Lazy<IReadOnlyList<(string Source, string ExpectedTarget)>> CachedMap = new(LoadMap);

    public static IReadOnlyList<(string Source, string ExpectedTarget)> Map => CachedMap.Value;

    private static IReadOnlyList<(string Source, string ExpectedTarget)> LoadMap()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "library-map.tsv");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing golden fixture: {path}", path);
        }

        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Select(line =>
            {
                var parts = line.Split('\t');
                if (parts.Length != 2)
                {
                    throw new InvalidOperationException($"Malformed fixture line: {line}");
                }

                return (parts[0], parts[1]);
            })
            .ToList();
    }
}


/// <summary>
/// Golden test over the real-world library: 908 source files and the exact name each one must get.
/// The expectations are literal data from the fixture, not values computed by the code under test,
/// so any change in grouping or naming fails here.
/// </summary>
public class LibraryGoldenTests
{
    private const int ExpectedFileCount = 908;
    private const int ExpectedShowCount = 38;
    private const int ExpectedMovieCount = 15;
    private const int ExpectedEpisodeCount = 893;

    private static readonly Regex TrailingSeasonMarker =
        new(@"\s(?:S\d{1,2}|Season\s*\d{1,2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void Fixture_ContainsEveryVideoFile()
    {
        Assert.Equal(ExpectedFileCount, LibraryFixture.Map.Count);
    }

    [Fact]
    public void GroupMediaFiles_RealLibrary_SplitsIntoExpectedShowsAndMovies()
    {
        var mediaObjects = GroupRealLibrary();

        Assert.Equal(ExpectedShowCount, mediaObjects.Count(m => m.Type == MediaType.Show));
        Assert.Equal(ExpectedMovieCount, mediaObjects.Count(m => m.Type == MediaType.Movie));

        var episodeCount = mediaObjects
            .Where(m => m.Type == MediaType.Show)
            .Sum(m => m.Seasons.Sum(s => s.Episodes.Count));
        Assert.Equal(ExpectedEpisodeCount, episodeCount);
    }

    [Fact]
    public void GroupMediaFiles_RealLibrary_ResolvesEveryFileToExpectedDestination()
    {
        var actual = BuildDestinationMap(GroupRealLibrary());

        var mismatches = new List<string>();
        foreach (var (source, expected) in LibraryFixture.Map)
        {
            var fullSource = Path.Combine(LibraryFixture.Root, source);
            if (!actual.TryGetValue(fullSource, out var target))
            {
                mismatches.Add($"{source}{Environment.NewLine}   not grouped into any media object");
                continue;
            }

            var fullExpected = Path.Combine(LibraryFixture.Root, expected);
            if (!string.Equals(Normalize(target), Normalize(fullExpected), StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"{source}{Environment.NewLine}" +
                    $"   actual:   {Normalize(target)}{Environment.NewLine}" +
                    $"   expected: {Normalize(fullExpected)}");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} mismatch(es):{Environment.NewLine}{string.Join(Environment.NewLine, mismatches)}");
    }

    [Fact]
    public void GroupMediaFiles_RealLibrary_EpisodeFilesAreStandardized()
    {
        foreach (var show in GroupRealLibrary().Where(m => m.Type == MediaType.Show))
        {
            // The season is a folder, never part of the show name.
            Assert.DoesNotMatch(TrailingSeasonMarker, show.Name);

            foreach (var season in show.Seasons)
            {
                var expectedFolder = Path.Combine(LibraryFixture.Root, show.Name, $"Season {season.SeasonNumber:00}");

                foreach (var episode in season.Episodes)
                {
                    var target = MovePlanBuilder.ResolveDestinationPath(episode.Path, show, LibraryFixture.Root);
                    var expectedStem = $"{show.Name} S{season.SeasonNumber:00}E{episode.EpisodeNumber:00}";

                    Assert.Equal(expectedStem, Path.GetFileNameWithoutExtension(target));
                    Assert.Equal(expectedFolder, Path.GetDirectoryName(target));
                }
            }
        }
    }

    private static List<MediaObject> GroupRealLibrary()
        => new MediaGrouper().GroupMediaFiles(
            LibraryFixture.Map.Select(m => Path.Combine(LibraryFixture.Root, m.Source)).ToList());

    private static Dictionary<string, string> BuildDestinationMap(List<MediaObject> mediaObjects)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var media in mediaObjects)
        {
            if (media.Type == MediaType.Movie)
            {
                var moviePath = media.MoviePath!;
                map[moviePath] = MovePlanBuilder.ResolveDestinationPath(moviePath, media, LibraryFixture.Root);
                continue;
            }

            foreach (var season in media.Seasons)
            {
                foreach (var episode in season.Episodes)
                {
                    map[episode.Path] = MovePlanBuilder.ResolveDestinationPath(episode.Path, media, LibraryFixture.Root);
                }
            }
        }

        return map;
    }

    internal static string Normalize(string path) => path.Replace('\\', '/');

/// <summary>
/// Optional integration check that keeps the fixture honest. When the real source and target
/// folders exist on this machine, it verifies that:
/// - the fixture covers exactly the files of the source tree,
/// - every expected destination is a real file in the target library,
/// - every file in the target library is explained by the fixture, apart from the mover's
///   " (N)" de-duplication suffixes.
/// Skipped on machines that do not have those folders (e.g. CI).
/// </summary>
public class LibraryGoldenFolderTests
{
    private const string SourceFolder = "/home/bramve/Documents/test2";
    private const string TargetFolder = "/home/bramve/Documents/test";

    private static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".ts", ".mpg", ".mpeg"];

    private static readonly Regex DuplicateSuffix = new(@" \(\d+\)$", RegexOptions.Compiled);

    [Fact]
    public void Fixture_DescribesTheRealLibraryFolders()
    {
        if (!Directory.Exists(SourceFolder) || !Directory.Exists(TargetFolder))
        {
            Assert.Skip($"Skipped: {SourceFolder} and/or {TargetFolder} are not available on this machine.");
        }

        var sourceFiles = ListVideos(SourceFolder);
        var targetFiles = ListVideos(TargetFolder);

        // 1) The fixture covers exactly the real source tree.
        var fixtureSources = LibraryFixture.Map
            .Select(m => m.Source)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(sourceFiles, fixtureSources);

        // 2) Every expected destination exists in the real target library.
        var missing = LibraryFixture.Map
            .Select(m => m.ExpectedTarget)
            .Distinct(StringComparer.Ordinal)
            .Where(expected => !targetFiles.Contains(expected, StringComparer.Ordinal))
            .ToList();
        Assert.True(
            missing.Count == 0,
            $"Expected destinations missing from {TargetFolder}:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");

        // 3) Every file in the target library is produced by the fixture.
        var explained = LibraryFixture.Map
            .Select(m => m.ExpectedTarget)
            .ToHashSet(StringComparer.Ordinal);
        var unexplained = targetFiles
            .Where(file => !explained.Contains(file) && !DuplicateSuffix.IsMatch(Path.GetFileNameWithoutExtension(file)))
            .ToList();
        Assert.True(
            unexplained.Count == 0,
            $"Target files not produced by the fixture:{Environment.NewLine}{string.Join(Environment.NewLine, unexplained)}");
    }

    private static List<string> ListVideos(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
}

}
