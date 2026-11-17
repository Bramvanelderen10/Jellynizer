using System.Text.RegularExpressions;

using Jellynizer.Helpers;

namespace Jellynizer.Execution;

public class SubtitleMover
{
    private static readonly Regex SeasonEpisodeRegex = new(@"S\d{1,2}E\d{1,4}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly char[] SubtitleNameSeparators = [' ', '.', '_', '-', '[', ']', '(', ')'];

    /// <summary>
    /// Tokens that describe a subtitle's language or variant. Used to build a clean
    /// "{VideoName}.{Language}.ext" name instead of carrying over release junk.
    /// Ambiguous two-letter codes that are also common English words ("no", "it", "de", "hi")
    /// are intentionally excluded.
    /// </summary>
    private static readonly HashSet<string> SubtitleTagTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "eng", "english",
        "nl", "nld", "dut", "dutch",
        "ger", "deu", "german",
        "fr", "fre", "fra", "french",
        "es", "spa", "spanish",
        "ita", "italian",
        "pt", "por", "portuguese", "ptbr",
        "pl", "pol", "polish",
        "ru", "rus", "russian",
        "swe", "swedish",
        "dan", "danish",
        "fin", "finnish",
        "jpn", "japanese",
        "kor", "korean",
        "chi", "zho", "chinese",
        "ara", "arabic",
        "tur", "turkish",
        "cze", "ces", "czech",
        "hun", "hungarian",
        "forced", "sdh", "cc", "commentary",
    };

    private readonly ILogger<SubtitleMover> _logger;
    private readonly IFileSystem _fileSystem;

    public SubtitleMover(
        ILogger<SubtitleMover> logger,
        IFileSystem fileSystem)
    {
        _logger = logger;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Finds subtitle files in the same directories as moved videos and moves them
    /// to the corresponding destination directory.
    /// </summary>
    public List<MovedFileInfo> MoveCompanionSubtitles(
        List<MovedFileInfo> movedVideos,
        string[] subtitleExtensions,
        string sourceRoot)
    {
        var subtitleExts = new HashSet<string>(subtitleExtensions, StringComparer.OrdinalIgnoreCase);
        var movedSubtitles = new List<MovedFileInfo>();
        var normalizedRoot = Path.GetFullPath(sourceRoot);

        // Group moved videos by their original parent directory
        var bySourceDir = movedVideos
            .GroupBy(m => Path.GetDirectoryName(m.OriginalPath)!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in bySourceDir)
        {
            var sourceDir = group.Key;
            if (!_fileSystem.DirectoryExists(sourceDir))
                continue;

            // Don't scan recursively if video was directly in the source root
            var searchOption = string.Equals(Path.GetFullPath(sourceDir), normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? SearchOption.TopDirectoryOnly
                : SearchOption.AllDirectories;

            var subtitleFiles = _fileSystem.EnumerateFiles(sourceDir, "*", searchOption)
                .Where(f => subtitleExts.Contains(Path.GetExtension(f)))
                .ToList();

            if (subtitleFiles.Count == 0)
                continue;

            var videos = group.ToList();

            foreach (var subFile in subtitleFiles)
            {
                if (!_fileSystem.FileExists(subFile))
                    continue;

                var targetVideo = videos.Count == 1
                    ? videos[0]
                    : FindBestVideoMatch(subFile, videos) ?? videos[0];

                var destDir = Path.GetDirectoryName(targetVideo.DestinationPath)!;
                var videoStem = Path.GetFileNameWithoutExtension(targetVideo.DestinationPath);
                var subFileName = Path.GetFileName(subFile);

                // Strip accumulated copy suffixes like " (1) (1)" from previous cycles
                var subNameWithoutExt = Path.GetFileNameWithoutExtension(subFileName);
                var strippedSubName = PathHelpers.StripTrailingCopySuffixes(subNameWithoutExt);
                if (subNameWithoutExt != strippedSubName)
                {
                    subFileName = strippedSubName + Path.GetExtension(subFileName);
                    subNameWithoutExt = strippedSubName;
                }

                // When the subtitle can be tied to this video, rebuild its name from the
                // standardized video name plus any language/variant tags, so release junk and
                // episode titles from the source filename don't end up next to the video
                // (e.g. "Show S01E01.en.srt" instead of "Show S01E01.Show S01E01 Brother 1080p...en.srt").
                var subEpisodeMarker = FindEpisodeMarker(subNameWithoutExt, subFile);
                var hasMatchingEpisodeMarker = subEpisodeMarker != null
                    && videoStem.Contains(subEpisodeMarker, StringComparison.OrdinalIgnoreCase);
                var sharesVideoTitle = NormalizeForComparison(subNameWithoutExt)
                    .StartsWith(NormalizeForComparison(videoStem), StringComparison.Ordinal);

                if (hasMatchingEpisodeMarker || sharesVideoTitle)
                {
                    var languageSuffix = BuildLanguageSuffix(subNameWithoutExt);
                    subFileName = $"{videoStem}{languageSuffix}{Path.GetExtension(subFile)}";
                }
                // Otherwise prefix the subtitle with the video name so files from different
                // episodes don't collide (e.g. "Health Care S01E03.2_English.srt").
                // Skip the prefix when the subtitle already contains the video's name.
                else if (!subNameWithoutExt.Contains(videoStem, StringComparison.OrdinalIgnoreCase))
                {
                    subFileName = $"{videoStem}.{subFileName}";
                }

                var destPath = Path.Combine(destDir, subFileName);
                destPath = PathHelpers.EnsureUniquePath(destPath, _fileSystem);

                _fileSystem.CreateDirectory(destDir);
                _fileSystem.MoveFile(subFile, destPath);
                movedSubtitles.Add(new MovedFileInfo(subFile, destPath));
                _logger.LogInformation("Moved subtitle '{Source}' -> '{Destination}'", subFile, destPath);
            }
        }

        return movedSubtitles;
    }

    /// <summary>
    /// Matches a subtitle file to the best-fitting video based on SxxExx patterns
    /// or filename similarity.
    /// </summary>
    private static MovedFileInfo? FindBestVideoMatch(string subtitlePath, List<MovedFileInfo> videos)
    {
        var subName = Path.GetFileNameWithoutExtension(subtitlePath);

        // Try matching by SxxExx pattern in the subtitle filename first,
        // then by the parent directory names
        var subPattern = FindEpisodeMarker(subName, subtitlePath);
        if (subPattern != null)
        {
            foreach (var video in videos)
            {
                var videoName = Path.GetFileNameWithoutExtension(video.OriginalPath);
                if (videoName.Contains(subPattern, StringComparison.OrdinalIgnoreCase))
                    return video;
            }
        }

        // Try matching by longest common prefix of normalized names
        MovedFileInfo? bestMatch = null;
        var bestLength = 0;

        foreach (var video in videos)
        {
            var videoBaseName = NormalizeName(Path.GetFileNameWithoutExtension(video.OriginalPath));
            var subBaseName = NormalizeName(subName);

            var commonLength = CommonPrefixLength(videoBaseName, subBaseName);
            if (commonLength > bestLength)
            {
                bestLength = commonLength;
                bestMatch = video;
            }
        }

        return bestMatch;
    }

    private static string NormalizeName(string name)
        => name.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();

    /// <summary>
    /// Keeps only letters and digits (lowercased) so names can be compared regardless of the
    /// separators and brackets used in the release.
    /// </summary>
    private static string NormalizeForComparison(string name)
        => new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>
    /// Finds an SxxExx marker in the subtitle filename, falling back to the names of its
    /// parent directories (handles <c>Subs/Show.S01E01.../2_English.srt</c> layouts).
    /// </summary>
    private static string? FindEpisodeMarker(string fileName, string? filePath = null)
    {
        var match = SeasonEpisodeRegex.Match(fileName);
        if (match.Success)
            return match.Value;

        var dir = filePath is null ? null : Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir))
        {
            match = SeasonEpisodeRegex.Match(Path.GetFileName(dir));
            if (match.Success)
                return match.Value;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>
    /// Builds a ".<Language>" suffix (e.g. <c>.en</c> or <c>.sdh</c>) from the language and
    /// variant tokens found in the subtitle filename. Returns an empty string when the
    /// filename contains no recognizable token.
    /// </summary>
    private static string BuildLanguageSuffix(string subtitleNameWithoutExtension)
    {
        var tags = subtitleNameWithoutExtension
            .Split(SubtitleNameSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(SubtitleTagTokens.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return tags.Count == 0 ? string.Empty : "." + string.Join(".", tags);
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var minLength = Math.Min(a.Length, b.Length);
        for (var i = 0; i < minLength; i++)
        {
            if (a[i] != b[i])
                return i;
        }
        return minLength;
    }
}