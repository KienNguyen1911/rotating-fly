using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AssetAutomator.Core.Constants;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Service for managing Batch Image Generation Projects.
    /// Stores project configuration and cards history under CurrentSettings.ProjectsStorageDir
    /// </summary>
    public class BatchProjectService
    {
        private readonly IConfigService _configService;
        private static readonly System.Threading.SemaphoreSlim _fileLock = new(1, 1);
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public BatchProjectService(IConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public string GetProjectsBaseDirectory()
        {
            string dir = _configService.CurrentSettings.ProjectsStorageDir;
            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "Projects");
            }
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public void EnsureProjectsDirectory()
        {
            GetProjectsBaseDirectory();
        }

        /// <summary>
        /// Retrieves all saved projects found in ProjectsStorageDir
        /// </summary>
        public async Task<List<BatchProjectModel>> GetAllProjectsAsync()
        {
            string baseDir = GetProjectsBaseDirectory();
            var list = new List<BatchProjectModel>();

            if (Directory.Exists(baseDir))
            {
                var dirs = Directory.GetDirectories(baseDir);
                foreach (var dir in dirs)
                {
                    string jsonPath = Path.Combine(dir, "project.json");
                    if (File.Exists(jsonPath))
                    {
                        try
                        {
                            using var fs = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var reader = new StreamReader(fs);
                            string json = await reader.ReadToEndAsync();
                            var proj = JsonSerializer.Deserialize<BatchProjectModel>(json, _jsonOptions);
                            if (proj != null)
                            {
                                list.Add(proj);
                            }
                        }
                        catch
                        {
                            // Ignore corrupted project files
                        }
                    }
                }
            }

            return list.OrderByDescending(p => p.LastModified).ToList();
        }

        /// <summary>
        /// Creates a new project folder and initial project.json
        ///
        /// Note: We intentionally do NOT auto-create an "Images" subdirectory here.
        /// The Gemini pipeline later sets <c>proj.OutputDir</c> to <c>{outputDir}/img</c>
        /// (where <c>outputDir</c> already contains voiceover.mp3, scenes.json, etc.),
        /// and we don't want a second, empty "Images" folder sibling to that — the
        /// user previously ended up with two confusing duplicate folders.
        /// <c>SaveProjectAsync</c> still ensures <c>OutputDir</c> exists on disk if
        /// it is set to a non-empty value.
        /// </summary>
        public async Task<BatchProjectModel> CreateProjectAsync(string projectName, string initialScriptJson = "")
        {
            string baseDir = GetProjectsBaseDirectory();
            string cleanName = SanitizeProjectName(projectName);
            if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Project_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string projectDir = Path.Combine(baseDir, cleanName);
            Directory.CreateDirectory(projectDir);

            var proj = new BatchProjectModel
            {
                ProjectId = Guid.NewGuid().ToString(),
                ProjectName = projectName,
                CreatedAt = DateTime.Now,
                LastModified = DateTime.Now,
                ScriptJson = initialScriptJson,
                OutputDir = string.Empty
            };

            await SaveProjectAsync(proj);
            return proj;
        }

        /// <summary>
        /// Returns the project with the given <paramref name="projectName"/> if it
        /// already exists on disk; otherwise creates a fresh one and returns it.
        ///
        /// This is the idempotent entry-point used by the Gemini pipeline so a
        /// re-run of the same task does NOT spawn duplicate projects or reset
        /// previously-captured <c>FlowProjectId</c> / <c>FlowProjectUrl</c>
        /// values (which would orphan the existing Flow project on Google Flow).
        /// </summary>
        public async Task<BatchProjectModel> GetOrCreateProjectAsync(string projectName, string initialScriptJson = "")
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = "Project_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }

            string baseDir = GetProjectsBaseDirectory();
            string cleanName = SanitizeProjectName(projectName);
            if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Project_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string projectDir = Path.Combine(baseDir, cleanName);
            string jsonPath = Path.Combine(projectDir, "project.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    using var fs = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    string json = await reader.ReadToEndAsync();
                    var existing = JsonSerializer.Deserialize<BatchProjectModel>(json, _jsonOptions);
                    if (existing != null)
                    {
                        // Make sure OutputDir is populated if it was missing on disk.
                        // We do NOT auto-create an Images/ subdirectory here — the pipeline
                        // will set OutputDir to {outputDir}/img on the next call.
                        if (string.IsNullOrWhiteSpace(existing.OutputDir))
                        {
                            existing.OutputDir = Path.Combine(projectDir, "Images");
                        }
                        // Touch LastModified so the dashboard sorts it to the top.
                        existing.LastModified = DateTime.Now;
                        return existing;
                    }
                }
                catch
                {
                    // Corrupted project.json → fall through to CreateProjectAsync so the
                    // user gets a fresh project rather than a stuck pipeline.
                }
            }

            return await CreateProjectAsync(projectName, initialScriptJson);
        }

        /// <summary>
        /// Sanitizes a human-readable project name into a directory-safe slug.
        ///
        /// IMPORTANT: this MUST stay in sync with <see cref="SanitizeTopicAsProjectName"/>
        /// because the Batch Image Gen dashboard stores its project folder under
        /// <c>ProjectsStorageDir/{slug}</c> and the Gemini pipeline writes its
        /// primary output folder at <c>OutputsDir/{slug}</c>. If the two rules
        /// diverge, the dashboard project and the pipeline output end up in
        /// sibling folders with different names (e.g. "mirror neurons made you
        /// and they can break you" vs "mirror_neurons_made_you_and_they_can_break_you")
        /// and the user ends up with two confusing near-duplicate projects in
        /// the dashboard. Both rules collapse whitespace to single hyphens so
        /// "Bedtime Psychology" and "Bedtime-Psychology" both map to
        /// "bedtime-psychology".
        /// </summary>
        public static string SanitizeProjectName(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName)) return string.Empty;
            return SanitizeTopicAsProjectName(projectName);
        }

        /// <summary>
        /// Sanitizes a free-form pipeline topic (Gemini pipeline input) into a
        /// stable BatchProject directory name. Mirrors the slug rules used by
        /// <see cref="YoutubeHelper.ToSafeTopicSlug"/> so the Batch Image Gen
        /// dashboard groups well with the rest of the app's file naming.
        /// </summary>
        public static string SanitizeTopicAsProjectName(string? topic)
        {
            return YoutubeHelper.ToSafeTopicSlug(topic);
        }

        /// <summary>
        /// Saves project metadata and items state to project.json
        /// </summary>
        public async Task SaveProjectAsync(BatchProjectModel proj)
        {
            if (proj == null || string.IsNullOrWhiteSpace(proj.ProjectName)) return;

            await _fileLock.WaitAsync();
            try
            {
                string baseDir = GetProjectsBaseDirectory();
                string cleanName = SanitizeProjectName(proj.ProjectName);
                string projectDir = Path.Combine(baseDir, cleanName);
                Directory.CreateDirectory(projectDir);

                if (string.IsNullOrWhiteSpace(proj.OutputDir))
                {
                    proj.OutputDir = Path.Combine(projectDir, "Images");
                }

                proj.LastModified = DateTime.Now;
                string jsonPath = Path.Combine(projectDir, "project.json");

                string jsonString = JsonSerializer.Serialize(proj, _jsonOptions);

                // Robust retry loop (up to 5 attempts, 100ms delay) to handle transient
                // file locks e.g. when antivirus or background scanners briefly hold a handle.
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    try
                    {
                        using var fs = new FileStream(jsonPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                        using var writer = new StreamWriter(fs);
                        await writer.WriteAsync(jsonString);
                        break;
                    }
                    catch (IOException) when (attempt < 5)
                    {
                        await Task.Delay(100);
                    }
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <summary>
        /// Deletes a project directory
        /// </summary>
        public void DeleteProject(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName)) return;
            string baseDir = GetProjectsBaseDirectory();
            string cleanName = SanitizeProjectName(projectName);
            string projectDir = Path.Combine(baseDir, cleanName);

            if (Directory.Exists(projectDir))
            {
                Directory.Delete(projectDir, true);
            }
        }
    }
}