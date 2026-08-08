using AssetAutomator.Core.Models;
using Xunit;

namespace AssetAutomator.Tests;

/// <summary>
/// Tests for the SceneTitle normalizer. The normalizer is the bridge that
/// lets us change the on-screen card title from "Scene #1: scene_001" to a
/// compact "001" without forcing every user to re-run their pipeline.
///
/// Any project.json saved before the rename would still contain the old
/// format, so the load path normalizes on the fly. These tests pin the
/// extraction behavior so a future tweak doesn't break the migration.
/// </summary>
public class BatchImageItemTitleNormalizationTests
{
    [Theory]
    // (rawTitle, index, expected) — pipeline-built current format
    [InlineData(null, 1, "001")]
    [InlineData("", 1, "001")]
    [InlineData("   ", 1, "001")]

    // Legacy pipeline format from before the rename
    [InlineData("Scene #1: scene_001", 1, "001")]
    [InlineData("Scene #42: scene_042", 42, "042")]

    // User-typed "Cảnh N" format from the import-ScriptJson path
    [InlineData("Cảnh 1", 1, "001")]
    [InlineData("Cảnh 99", 99, "099")]

    // Already-normalized — should pass through unchanged
    [InlineData("001", 1, "001")]
    [InlineData("123", 123, "123")]

    // Edge case: number in title > index (e.g. user renamed index in
    // scenes.json but kept the old project.json). Prefer the number
    // from the title so the badge stays consistent with the title bar.
    [InlineData("Scene #7: scene_007", 1, "007")]

    // > 999 scenes still pad to 3 digits (the maximum we'd realistically see)
    [InlineData("Scene #1234: scene_1234", 1234, "1234")]
    public void NormalizeSceneTitle_HandlesAllLegacyAndCurrentFormats(
        string? rawTitle, int index, string expected)
    {
        string actual = BatchImageItem.NormalizeSceneTitle(rawTitle, index);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeSceneTitle_AlwaysReturnsThreeDigitForSmallIndex()
    {
        // Defensive: if the title is empty AND the index is 0, we should
        // still get a valid (zero-padded) string instead of crashing.
        string result = BatchImageItem.NormalizeSceneTitle("", 0);
        Assert.Equal("000", result);
    }
}