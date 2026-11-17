using Jellynizer.Execution;
using Jellynizer.Helpers;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace Jellynizer.Tests;

public class SubtitleMoverTests
{
    private readonly Mock<ILogger<SubtitleMover>> _loggerMock = new();
    private readonly Mock<IFileSystem> _fsMock = new();
    private readonly SubtitleMover _sut;

    public SubtitleMoverTests()
    {
        _sut = new SubtitleMover(_loggerMock.Object, _fsMock.Object);
    }

    [Fact]
    public void MoveCompanionSubtitles_MovesSubtitleAlongsideVideo()
    {
        var movedVideos = new List<MovedFileInfo>
        {
            new("/source/Show.S01E01.mkv", "/dest/Show/Season 01/Show.S01E01.mkv")
        };

        _fsMock.Setup(f => f.DirectoryExists("/source")).Returns(true);
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { "/source/Show.S01E01.srt" });
        _fsMock.Setup(f => f.FileExists("/source/Show.S01E01.srt")).Returns(true);
        _fsMock.Setup(f => f.FileExists(It.Is<string>(p => p.EndsWith(".srt") && p.StartsWith("/dest"))))
            .Returns(false);

        var result = _sut.MoveCompanionSubtitles(movedVideos, new[] { ".srt" }, "/source");

        Assert.Single(result);
        _fsMock.Verify(f => f.MoveFile("/source/Show.S01E01.srt", It.Is<string>(p => p.Contains("/dest/Show/Season 01/"))), Times.Once);
        _fsMock.Verify(f => f.CreateDirectory("/dest/Show/Season 01"), Times.Once);
    }

    [Fact]
    public void MoveCompanionSubtitles_ReturnsEmpty_WhenNoSubtitlesFound()
    {
        var movedVideos = new List<MovedFileInfo>
        {
            new("/source/movie.mp4", "/dest/Movie/Movie.mp4")
        };

        _fsMock.Setup(f => f.DirectoryExists("/source")).Returns(true);
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.AllDirectories))
            .Returns(Array.Empty<string>());

        var result = _sut.MoveCompanionSubtitles(movedVideos, new[] { ".srt" }, "/source");

        Assert.Empty(result);
    }

    [Fact]
    public void MoveCompanionSubtitles_SkipsWhenSourceDirectoryDoesNotExist()
    {
        var movedVideos = new List<MovedFileInfo>
        {
            new("/gone/movie.mp4", "/dest/Movie/Movie.mp4")
        };

        _fsMock.Setup(f => f.DirectoryExists("/gone")).Returns(false);

        var result = _sut.MoveCompanionSubtitles(movedVideos, new[] { ".srt" }, "/gone");

        Assert.Empty(result);
        _fsMock.Verify(f => f.MoveFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void MoveCompanionSubtitles_SkipsSubtitleWhenFileNoLongerExists()
    {
        var movedVideos = new List<MovedFileInfo>
        {
            new("/source/movie.mp4", "/dest/Movie/Movie.mp4")
        };

        _fsMock.Setup(f => f.DirectoryExists("/source")).Returns(true);
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.AllDirectories))
            .Returns(new[] { "/source/movie.srt" });
        _fsMock.Setup(f => f.FileExists("/source/movie.srt")).Returns(false);

        var result = _sut.MoveCompanionSubtitles(movedVideos, new[] { ".srt" }, "/source");

        Assert.Empty(result);
        _fsMock.Verify(f => f.MoveFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void MoveCompanionSubtitles_PrefixesSubtitleWithVideoName_WhenNamesDiffer()
    {
        var movedVideos = new List<MovedFileInfo>
        {
            new("/source/Show.S01E01.mkv", "/dest/Show/Season 01/Show.S01E01.mkv")
        };

        _fsMock.Setup(f => f.DirectoryExists("/source")).Returns(true);
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { "/source/2_English.srt" });
        _fsMock.Setup(f => f.FileExists("/source/2_English.srt")).Returns(true);
        _fsMock.Setup(f => f.FileExists(It.Is<string>(p => p.StartsWith("/dest"))))
            .Returns(false);

        var result = _sut.MoveCompanionSubtitles(movedVideos, new[] { ".srt" }, "/source");

        Assert.Single(result);
        // The subtitle should be prefixed with the video stem
        Assert.Contains("Show.S01E01", result[0].DestinationPath);
        Assert.Contains("2_English.srt", result[0].DestinationPath);
    }

    [Fact]
    public void MoveCompanionSubtitles_ReturnsEmpty_WhenNoVideosProvided()
    {
        var result = _sut.MoveCompanionSubtitles(new List<MovedFileInfo>(), new[] { ".srt" }, "/source");

        Assert.Empty(result);
    }

    [Fact]
    public void MoveCompanionSubtitles_SubtitleWithMatchingSxxExx_StandardizesToVideoNameAndLanguage()
    {
        // Subtitle carries the same release junk as the video; it should be renamed to
        // "{VideoName}.{Language}.ext" instead of keeping tags and episode titles.
        var videoSource = "/source/East of Eden 2026 S01E01 Brothers 1080p NF WEB-DL DDP5 1 Atmos H 264-FLUX.mkv";
        var subtitleSource = "/source/East of Eden 2026 S01E01 Brothers 1080p NF WEB-DL DDP5 1 Atmos H 264-FLUX.en.srt";
        var expectedDestination = "/dest/East of Eden (2026)/Season 01/East of Eden (2026) S01E01.en.srt";

        var movedVideos = new List<MovedFileInfo>
        {
            new(videoSource, "/dest/East of Eden (2026)/Season 01/East of Eden (2026) S01E01.mkv")
        };

        _fsMock.Setup(f => f.DirectoryExists("/source")).Returns(true);
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { subtitleSource });
        _fsMock.Setup(f => f.FileExists(subtitleSource)).Returns(true);
        _fsMock.Setup(f => f.FileExists(It.Is<string>(p => p.StartsWith("/dest")))).Returns(false);

        var result = _sut.MoveCompanionSubtitles(movedVideos, new[] { ".srt" }, "/source");

        Assert.Single(result);
        Assert.Equal(expectedDestination, result[0].DestinationPath);
        _fsMock.Verify(f => f.MoveFile(subtitleSource, expectedDestination), Times.Once);
    }

    [Fact]
    public void MoveCompanionSubtitles_SubtitleInEpisodeFolderWithoutLanguage_StandardizesToVideoName()
    {
        var videoSource = "/source/Show/Show.S01E01.1080p.mkv";
        var subtitleSource = "/source/Show/Subs/Show.S01E01/2_English.srt";
        var expectedDestination = "/dest/Show/Season 01/Show S01E01.English.srt";

        var movedVideos = new List<MovedFileInfo>
        {
            new(videoSource, "/dest/Show/Season 01/Show S01E01.mkv")
        };

        _fsMock.Setup(f => f.DirectoryExists("/source/Show")).Returns(true);
        _fsMock.Setup(f => f.EnumerateFiles("/source/Show", "*", SearchOption.AllDirectories))
            .Returns(new[] { subtitleSource });
        _fsMock.Setup(f => f.FileExists(subtitleSource)).Returns(true);
        _fsMock.Setup(f => f.FileExists(It.Is<string>(p => p.StartsWith("/dest")))).Returns(false);

        var result = _sut.MoveCompanionSubtitles(movedVideos, new[] { ".srt" }, "/source");

        Assert.Single(result);
        Assert.Equal(expectedDestination, result[0].DestinationPath);
    }

    [Fact]
    public void MoveCompanionSubtitles_MovieSubtitle_KeepsTitleWithoutDuplicating()
    {
        var videoSource = "/source/Interstellar.2014.1080p.mkv";
        var subtitleSource = "/source/Interstellar.2014.en.srt";
        var expectedDestination = "/dest/Interstellar (2014)/Interstellar (2014).en.srt";

        var movedVideos = new List<MovedFileInfo>
        {
            new(videoSource, "/dest/Interstellar (2014)/Interstellar (2014).mkv")
        };

        _fsMock.Setup(f => f.DirectoryExists("/source")).Returns(true);
        _fsMock.Setup(f => f.EnumerateFiles("/source", "*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { subtitleSource });
        _fsMock.Setup(f => f.FileExists(subtitleSource)).Returns(true);
        _fsMock.Setup(f => f.FileExists(It.Is<string>(p => p.StartsWith("/dest")))).Returns(false);

        var result = _sut.MoveCompanionSubtitles(movedVideos, new[] { ".srt" }, "/source");

        Assert.Single(result);
        Assert.Equal(expectedDestination, result[0].DestinationPath);
    }
}
