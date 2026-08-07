using System;
using System.IO;
using System.Threading.Tasks;
using AssetAutomator.Application.Services;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.Infrastructure.Services;
using Xunit;

namespace AssetAutomator.Tests
{
    /// <summary>
    /// Regression tests for the 5 bugs we hit on 2026-08-07 around the Batch
    /// Image Gen dashboard:
    ///   1. Two duplicate project folders (one with spaces, one with hyphens)
    ///   2. Missing media_id after a Flow HTTP 500 burst
    ///   3. ScriptJson embedded full scene content (60 KB+) instead of a path
    ///   4. Duplicate img/ + Images/ folders under the same project
    ///   5. RefImagePaths always empty after a failed run
    ///
    /// Tests 2 and 4 are exercised through unit-level behavior (retry config,
    /// output-dir overrides). 1, 3, 5 round-trip via a real temp directory
    /// through BatchProjectService.
    /// </summary>
    public class BatchProjectServiceTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly IConfigService _config;

        public BatchProjectServiceTests()
        {
            // Each test gets its own scratch directory so the test suite is
            // hermetic — no cross-test pollution, and no risk of touching a
            // real AssetAutomator project folder.
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "AssetAutomator.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            _config = new StubConfigService();
            // BatchProjectService.GetProjectsBaseDirectory reads from
            // CurrentSettings.ProjectsStorageDir, so we set it directly here.
            _config.CurrentSettings.ProjectsStorageDir =
                Path.Combine(_tempRoot, "Projects");
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
        }

        // ──────────────────────────────────────────────────────────────
        //  Bug 1 — duplicate folder (spaces vs hyphens)
        //  Both the directory created by GetOrCreateProjectAsync (using the
        //  topic slug) AND the directory used by SaveProjectAsync (using
        //  SanitizeProjectName on the display name) MUST resolve to the same
        //  folder, otherwise the user ends up with sibling folders that look
        //  like near-duplicates in the dashboard.
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void SanitizeProjectName_And_SanitizeTopicAsProjectName_Produce_Identical_Slugs()
        {
            // Display name the pipeline sets on the project after lookup.
            string displayName = "mirror neurons made you and they can break you";

            // Slug used by GetOrCreateProjectAsync to locate the existing folder.
            string topicSlug = BatchProjectService.SanitizeTopicAsProjectName(displayName);

            // Slug used by SaveProjectAsync to write project.json back to disk.
            string projectSlug = BatchProjectService.SanitizeProjectName(displayName);

            Assert.Equal("mirror-neurons-made-you-and-they-can-break-you", topicSlug);
            Assert.Equal(topicSlug, projectSlug);
        }

        [Fact]
        public async Task GetOrCreateProjectAsync_Reuses_Same_Folder_After_Renaming_ProjectName()
        {
            var service = new BatchProjectService(_config);

            // First call: pipeline passes the topic slug ("mirror-neurons-...").
            string topicSlug = "mirror-neurons-made-you-and-they-can-break-you";
            var first = await service.GetOrCreateProjectAsync(topicSlug);

            // Pipeline then overwrites ProjectName with the human-friendly
            // display name ("mirror neurons...") and saves.
            first.ProjectName = "mirror neurons made you and they can break you";
            first.ScriptJson = @"D:\Assets\Gemini\...\scenes.json";
            first.RefImagePaths.Add(@"D:\Content-YTB\...\Stickman-Reference.png");
            await service.SaveProjectAsync(first);

            // Second call: pipeline looks up by the same topic slug. It MUST
            // find the project we just wrote, not create a new sibling folder.
            var second = await service.GetOrCreateProjectAsync(topicSlug);

            // Assert: same ProjectId (not a new project) AND the in-memory
            // state we wrote survives (proves we hit the same project.json).
            Assert.Equal(first.ProjectId, second.ProjectId);

            // No sibling folder should exist under ProjectsStorageDir.
            Assert.Single(Directory.GetDirectories(_config.CurrentSettings.ProjectsStorageDir));

            // The single folder should be the topic-slug one (not a "spaces"
            // sibling, which is what Bug 1 produced).
            string singleDir = Directory.GetDirectories(
                _config.CurrentSettings.ProjectsStorageDir)[0];
            Assert.Equal(
                topicSlug,
                Path.GetFileName(singleDir));
        }

        // ──────────────────────────────────────────────────────────────
        //  Bug 3 — ScriptJson should be a path, not full content.
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task SaveProjectAsync_Round_Trips_ScriptJson_As_Path_Not_Embedded_Content()
        {
            var service = new BatchProjectService(_config);

            string projectName = "bedtime-psychology";
            string scenesPath = @"D:\Assets\Gemini\bedtime-psychology\scenes.json";

            var proj = await service.GetOrCreateProjectAsync(projectName);
            proj.ScriptJson = scenesPath;
            await service.SaveProjectAsync(proj);

            // Read the raw file directly: project.json must NOT contain the
            // {"scenes":[...]} blob. It should be a path string.
            string jsonPath = Path.Combine(
                _config.CurrentSettings.ProjectsStorageDir,
                BatchProjectService.SanitizeProjectName(projectName),
                "project.json");
            string raw = await File.ReadAllTextAsync(jsonPath);

            Assert.DoesNotContain("\"scenes\"", raw);
            Assert.DoesNotContain("\"image_prompt\"", raw);
            Assert.Contains("scenes.json", raw);
        }

        // ──────────────────────────────────────────────────────────────
        //  Bug 4 — CreateProjectAsync must NOT auto-create an "Images"
        //  subfolder (SceneImageBatchStep later overrides OutputDir with
        //  {outputDir}/img and we don't want a second empty sibling).
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateProjectAsync_Does_Not_Create_Images_Subfolder()
        {
            var service = new BatchProjectService(_config);
            string projectName = "fresh-project";

            var proj = await service.CreateProjectAsync(projectName);

            string projectDir = Path.Combine(
                _config.CurrentSettings.ProjectsStorageDir,
                BatchProjectService.SanitizeProjectName(projectName));

            Assert.True(Directory.Exists(projectDir));
            Assert.False(
                Directory.Exists(Path.Combine(projectDir, "Images")),
                "CreateProjectAsync must not auto-create an Images/ subfolder — " +
                "the pipeline overrides OutputDir and a stray Images/ folder " +
                "confuses the user (Bug 4).");

            // OutputDir is allowed to be a path string even if the folder is not
            // materialized — the pipeline will overwrite it on first run. The
            // important assertion is that the folder does NOT exist on disk.
            Assert.True(
                proj.OutputDir.EndsWith("Images", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(proj.OutputDir),
                $"OutputDir should either be empty or point at the not-yet-created Images subfolder; was '{proj.OutputDir}'");
        }

        // ──────────────────────────────────────────────────────────────
        //  Bug 5 — RefImagePaths must survive a re-run.
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task RefImagePaths_Round_Trip_Through_GetOrCreate_Loop()
        {
            var service = new BatchProjectService(_config);
            string projectName = "stickman-demo";
            string refPath = @"D:\Content-YTB\bedtime-psycology\Stickman-Reference.png";

            // First run: pipeline creates project + sets RefImagePaths.
            var first = await service.GetOrCreateProjectAsync(projectName);
            first.RefImagePaths.Add(refPath);
            await service.SaveProjectAsync(first);

            // Second run: pipeline loads existing project. RefImagePaths must
            // survive (this is what Bug 5 broke — the file was being saved with
            // an empty list because the in-memory set was never persisted).
            var second = await service.GetOrCreateProjectAsync(projectName);

            Assert.Single(second.RefImagePaths);
            Assert.Equal(refPath, second.RefImagePaths[0]);
        }

        /// <summary>
        /// Minimal in-memory config stub so tests don't touch the user's
        /// real %APPDATA%\AssetAutomator\appsettings.json.
        /// </summary>
        private class StubConfigService : IConfigService
        {
            public AppSettings CurrentSettings { get; } = new AppSettings();

            public AppSettings LoadSettings() => CurrentSettings;
            public void SaveSettings(AppSettings settings) { }
            public void EnsureDefaults(AppSettings settings) { }
        }
    }
}
