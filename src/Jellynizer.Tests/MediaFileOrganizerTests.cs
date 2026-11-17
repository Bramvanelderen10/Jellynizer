using Jellynizer.Orchestration;

using Xunit;

namespace Jellynizer.Tests;

public class MediaFileOrganizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveDestinationRoot_UnsetOrBlank_FallsBackToSourceFolder(string? configuredDestination)
    {
        // Regression: an empty DestinationFolder used to yield relative destinations,
        // so files were moved relative to the process working directory.
        Assert.Equal("/media", MediaFileOrganizer.ResolveDestinationRoot(configuredDestination, "/media"));
    }

    [Fact]
    public void ResolveDestinationRoot_ConfiguredFolder_IsUsed()
    {
        Assert.Equal("/library", MediaFileOrganizer.ResolveDestinationRoot("/library", "/media"));
    }

    [Fact]
    public void ResolveDestinationRoot_RelativeFolder_BecomesAbsolute()
    {
        var root = MediaFileOrganizer.ResolveDestinationRoot("relative/lib", "/media");

        Assert.True(Path.IsPathRooted(root));
        Assert.EndsWith(Path.Combine("relative", "lib"), root);
    }
}
