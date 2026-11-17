using Jellynizer.Parsing;
using Jellynizer.History;
using Jellynizer.Planning;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace Jellynizer.Tests;

public class MovePlanBuilderTests : IDisposable
{
    private readonly Mock<ILogger<MovePlanBuilder>> _loggerMock;
    private readonly Mock<IDbContextFactory<MoveHistoryDbContext>> _contextFactoryMock;
    private readonly DbContextOptions<MoveHistoryDbContext> _dbOptions;
    private MovePlanBuilder _sut;

    public MovePlanBuilderTests()
    {
        _loggerMock = new Mock<ILogger<MovePlanBuilder>>();
        _contextFactoryMock = new Mock<IDbContextFactory<MoveHistoryDbContext>>();

        _dbOptions = new DbContextOptionsBuilder<MoveHistoryDbContext>()
            .UseInMemoryDatabase(databaseName: $"MoveHistoryDb_{Guid.NewGuid()}")
            .Options;

        _contextFactoryMock
            .Setup(f => f.CreateDbContext())
            .Returns(() => new MoveHistoryDbContext(_dbOptions));

        _sut = new MovePlanBuilder(_loggerMock.Object, _contextFactoryMock.Object);
    }

    private MoveHistoryDbContext CreateAssertContext() => new(_dbOptions);

    public void Dispose()
    {
    }

    [Fact]
    public void BuildMovePlan_NewFile_CreatesHistoryEntry()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var uniqueKey = "unique-key-123";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        var entry = assertContext.MoveHistory.FirstOrDefault(e => e.OriginalFilePath == testFile);
        Assert.NotNull(entry);
        Assert.False(entry.IsMoved);
    }

    [Fact]
    public void BuildMovePlan_FileDoesNotExist_StillCreatesHistoryEntry()
    {
        // Arrange – BuildMovePlan does not check file existence on disk;
        // it creates a history entry regardless.
        var testFile = "/media/nonexistent.mp4";
        var rootFolder = "/organized";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Single(assertContext.MoveHistory);
    }

    [Fact]
    public void BuildMovePlan_AlreadyMovedSuccessfully_IgnoresFile()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var destinationPath = "/organized/Test Movie/Test Movie.mp4";
        var uniqueKey = "Test Movie";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        var existingEntry = new MoveHistoryEntry
        {
            UniqueKey = uniqueKey,
            OriginalFilePath = testFile,
            TargetFilePath = destinationPath,
            IsMoved = true
        };

        using (var seedContext = CreateAssertContext())
        {
            seedContext.MoveHistory.Add(existingEntry);
            seedContext.SaveChanges();
        }

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Single(assertContext.MoveHistory);
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("already moved successfully")),
                It.IsAny<Exception>(),
                It.IsAny<System.Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void BuildMovePlan_UnmovedFileWithSameDestination_KeepsInDatabase()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var destinationPath = "/organized/Test Movie/Test Movie.mp4";
        var uniqueKey = "Test Movie";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        var existingEntry = new MoveHistoryEntry
        {
            UniqueKey = uniqueKey,
            OriginalFilePath = testFile,
            TargetFilePath = destinationPath,
            IsMoved = false
        };

        using (var seedContext = CreateAssertContext())
        {
            seedContext.MoveHistory.Add(existingEntry);
            seedContext.SaveChanges();
        }

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Single(assertContext.MoveHistory);
    }

    [Fact]
    public void BuildMovePlan_DestinationPathChanged_CreatesNewRecord()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var oldDestination = "/old/Test Movie/Test Movie.mp4";
        var newDestination = "/organized/Test Movie/Test Movie.mp4";
        var uniqueKey = "Test Movie";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        var oldEntry = new MoveHistoryEntry
        {
            UniqueKey = uniqueKey,
            OriginalFilePath = testFile,
            TargetFilePath = oldDestination,
            IsMoved = false
        };

        using (var seedContext = CreateAssertContext())
        {
            seedContext.MoveHistory.Add(oldEntry);
            seedContext.SaveChanges();
        }

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        var newEntry = assertContext.MoveHistory.FirstOrDefault(e =>
            e.UniqueKey == uniqueKey &&
            e.TargetFilePath == newDestination &&
            !e.IsMoved);
        Assert.NotNull(newEntry);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Destination path changed")),
                It.IsAny<Exception>(),
                It.IsAny<System.Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void BuildMovePlan_ShowWithMultipleEpisodes_CreatesRecordsForEachEpisode()
    {
        // Arrange
        var testFile1 = "/media/Show.S01E01.mkv";
        var testFile2 = "/media/Show.S01E02.mkv";
        var rootFolder = "/organized";

        var season = new Season(1, new List<Episode>
        {
            new(testFile1, 1),
            new(testFile2, 2)
        });
        var mediaObject = new MediaObject
        {
            Name = "Test Show",
            Type = MediaType.Show,
            Seasons = [season]
        };

        var mediaObjects = new List<MediaObject> { mediaObject };


        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Equal(2, assertContext.MoveHistory.Count());
    }

    [Fact]
    public void BuildMovePlan_MovieWithCorrectDestination_CreatesCorrectPath()
    {
        // Arrange
        var testFile = "/media/Inception.2010.mp4";
        var rootFolder = "/organized";
        var expectedDestination = "/organized/Inception 2010/Inception 2010.mp4";
        var uniqueKey = "unique-key-123";

        var mediaObject = new MediaObject
        {
            Name = "Inception 2010",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        var entry = assertContext.MoveHistory.FirstOrDefault(e =>
            e.TargetFilePath == expectedDestination);
        Assert.NotNull(entry);
    }

    [Fact]
    public void BuildMovePlan_ShowEpisodeWithCorrectDestination_CreatesCorrectPath()
    {
        // Arrange
        var testFile = "/media/Breaking.Bad.S02E05.mkv";
        var rootFolder = "/organized";
        var expectedDestination = "/organized/Breaking Bad/Season 02/Breaking Bad S02E05.mkv";
        var uniqueKey = "unique-key-456";

        var season = new Season(2, new List<Episode> { new(testFile, 5) });
        var mediaObject = new MediaObject
        {
            Name = "Breaking Bad",
            Type = MediaType.Show,
            Seasons = [season]
        };

        var mediaObjects = new List<MediaObject> { mediaObject };


        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        var entry = assertContext.MoveHistory.FirstOrDefault(e =>
            e.TargetFilePath == expectedDestination);
        Assert.NotNull(entry);
    }

    [Fact]
    public void BuildMovePlan_ShowEpisodeWithTrailingCopySuffix_StripsSuffixFromDestination()
    {
        // After a forget/re-organize cycle, the file at the destination has " (1)" baked in.
        // The standardized destination path must not carry that suffix over.
        var testFile = "/organized/Breaking Bad/Season 02/Breaking.Bad.S02E05 (1).mkv";
        var rootFolder = "/organized";
        var expectedDestination = "/organized/Breaking Bad/Season 02/Breaking Bad S02E05.mkv";

        var season = new Season(2, new List<Episode> { new(testFile, 5) });
        var mediaObject = new MediaObject
        {
            Name = "Breaking Bad",
            Type = MediaType.Show,
            Seasons = [season]
        };

        _sut.BuildMovePlan(new List<MediaObject> { mediaObject }, rootFolder);

        using var assertContext = CreateAssertContext();
        var entry = assertContext.MoveHistory.Single();
        Assert.Equal(expectedDestination, entry.TargetFilePath);
    }

    [Fact]
    public void BuildMovePlan_ShowEpisodeWithMultipleStackedSuffixes_StripsSuffixesFromDestination()
    {
        // File has accumulated " (1) (1) (1)" from repeated forget cycles
        var testFile = "/organized/Frieren/Season 01/[SubsPlease] Frieren S2 - 03 (1080p) [7556A22B] (1) (1) (1).mkv";
        var rootFolder = "/organized";
        var expectedDestination = "/organized/Frieren/Season 01/Frieren S01E03.mkv";

        var season = new Season(1, new List<Episode> { new(testFile, 3) });
        var mediaObject = new MediaObject
        {
            Name = "Frieren",
            Type = MediaType.Show,
            Seasons = [season]
        };

        _sut.BuildMovePlan(new List<MediaObject> { mediaObject }, rootFolder);

        using var assertContext = CreateAssertContext();
        var entry = assertContext.MoveHistory.Single();
        Assert.Equal(expectedDestination, entry.TargetFilePath);
    }

    [Fact]
    public void BuildMovePlan_EmptyMediaObjectList_SavesChangesWithoutAddingRecords()
    {
        // Arrange
        var mediaObjects = new List<MediaObject>();

        // Act
        _sut.BuildMovePlan(mediaObjects, "/organized");

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Empty(assertContext.MoveHistory);
    }

    [Fact]
    public void BuildMovePlan_MultipleMediaObjects_ProcessesAll()
    {
        // Arrange
        var movieFile = "/media/Movie.mp4";
        var showFile1 = "/media/Show.S01E01.mkv";
        var showFile2 = "/media/Show.S01E02.mkv";
        var rootFolder = "/organized";

        var movieObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = movieFile
        };

        var season = new Season(1, new List<Episode>
        {
            new(showFile1, 1),
            new(showFile2, 2)
        });
        var showObject = new MediaObject
        {
            Name = "Test Show",
            Type = MediaType.Show,
            Seasons = [season]
        };

        var mediaObjects = new List<MediaObject> { movieObject, showObject };


        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Equal(3, assertContext.MoveHistory.Count());
    }

    // ───────────────────── Show name reconstruction in filename ─────────────────────

    [Fact]
    public void BuildMovePlan_ShowFilenameMissingShowName_ReconstructsWithShowNameAndSxxExx()
    {
        // File doesn't contain the show name — should be reconstructed
        var testFile = "/media/ThatTimeIGotReincarnatedAsASlime/S02E06-The Beauty Makes Her Move [5F8B3E3E].mkv";
        var rootFolder = "/organized";
        var expectedDestination = "/organized/ThatTimeIGotReincarnatedAsASlime/Season 02/ThatTimeIGotReincarnatedAsASlime S02E06.mkv";

        var season = new Season(2, new List<Episode> { new(testFile, 6) });
        var mediaObject = new MediaObject
        {
            Name = "ThatTimeIGotReincarnatedAsASlime",
            Type = MediaType.Show,
            Seasons = [season]
        };

        _sut.BuildMovePlan(new List<MediaObject> { mediaObject }, rootFolder);

        using var assertContext = CreateAssertContext();
        var entry = assertContext.MoveHistory.Single();
        Assert.Equal(expectedDestination, entry.TargetFilePath);
    }

    [Fact]
    public void BuildMovePlan_ShowFilenameContainsReleaseTags_StandardizesFilename()
    {
        // File already contains the show name but is full of release tags — the destination
        // filename is standardized so Jellyfin can parse it.
        var testFile = "/media/Breaking.Bad.S02E05.1080p.WEB-DL.x265-GROUP.mkv";
        var rootFolder = "/organized";
        var expectedDestination = "/organized/Breaking Bad/Season 02/Breaking Bad S02E05.mkv";

        var season = new Season(2, new List<Episode> { new(testFile, 5) });
        var mediaObject = new MediaObject
        {
            Name = "Breaking Bad",
            Type = MediaType.Show,
            Seasons = [season]
        };

        _sut.BuildMovePlan(new List<MediaObject> { mediaObject }, rootFolder);

        using var assertContext = CreateAssertContext();
        var entry = assertContext.MoveHistory.Single();
        Assert.Equal(expectedDestination, entry.TargetFilePath);
    }

    [Fact]
    public void BuildMovePlan_SceneReleaseWithEpisodeTitles_ProducesStandardizedDestinations()
    {
        // Real-world release: episode titles plus quality/source/codec/group tags in every filename
        var folder = "/media/East of Eden 2026 S01 1080p NF WEB-DL DDP5.1 Atmos H264-FLUX";
        var files = new List<string>
        {
            $"{folder}/East of Eden 2026 S01E01 Brothers 1080p NF WEB-DL DDP5 1 Atmos H 264-FLUX.mkv",
            $"{folder}/East of Eden 2026 S01E02 Cathy 1080p NF WEB-DL DDP5 1 Atmos H 264-FLUX.mkv",
            $"{folder}/East of Eden 2026 S01E03 In the Valley 1080p NF WEB-DL DDP5 1 Atmos H 264-FLUX.mkv",
            $"{folder}/East of Eden 2026 S01E04 Kate 1080p NF WEB-DL DDP5 1 Atmos H 264-FLUX.mkv",
            $"{folder}/East of Eden 2026 S01E05 Departure 1080p NF WEB-DL DDP5 1 Atmos H 264-FLUX.mkv",
            $"{folder}/East of Eden 2026 S01E06 The Great Try 1080p NF WEB-DL DDP5 1 Atmos H 264-FLUX.mkv",
            $"{folder}/East of Eden 2026 S01E07 Timshel 1080p NF WEB-DL DDP5 1 Atmos H 264-FLUX.mkv",
        };
        var rootFolder = "/organized";

        var mediaObjects = new MediaGrouper().GroupMediaFiles(files);

        _sut.BuildMovePlan(mediaObjects, rootFolder);

        using var assertContext = CreateAssertContext();
        var targets = assertContext.MoveHistory
            .OrderBy(e => e.TargetFilePath)
            .Select(e => e.TargetFilePath)
            .ToList();

        Assert.Equal(7, targets.Count);
        Assert.Equal("/organized/East of Eden (2026)/Season 01/East of Eden (2026) S01E01.mkv", targets[0]);
        Assert.Equal("/organized/East of Eden (2026)/Season 01/East of Eden (2026) S01E07.mkv", targets[6]);
        Assert.All(targets, t => Assert.DoesNotContain("1080p", t));
        Assert.All(targets, t => Assert.DoesNotContain("DDP5", t));
    }
}
