using Jellynizer.Helpers;

namespace Jellynizer.Discovery;

public class VideoFileFinder
{
    private readonly IFileSystem _fileSystem;

    public VideoFileFinder(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <param name="sourceFolder">Root folder to scan.</param>
    /// <param name="extensions">Allowed video extensions.</param>
    /// <param name="deleteTrashFiles">
    /// When true (default), leftover qBittorrent trash markers (<c>.*trash*</c>) are deleted while scanning.
    /// Pass false for side-effect free runs such as a dry run.
    /// </param>
    public IReadOnlyList<string> GetVideoFiles(
        string sourceFolder,
        IEnumerable<string> extensions,
        bool deleteTrashFiles = true)
    {
        var allowedExtensions = extensions
            .Select(NormalizeExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var videoFilePaths = new List<string>();

        foreach (var path in _fileSystem.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            if (IsHiddenFile(path))
            {
                if (deleteTrashFiles)
                {
                    _fileSystem.DeleteFile(path);
                }

                continue;
            }

            if (!allowedExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            videoFilePaths.Add(path);
        }

        return videoFilePaths;
    }

    private static bool IsHiddenFile(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        return fileName.StartsWith(".", StringComparison.Ordinal)
            && fileName.Contains("trash", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith('.')
            ? extension.ToLowerInvariant()
            : $".{extension.ToLowerInvariant()}";
    }
}
