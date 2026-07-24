using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AssetAutomator.Models;

namespace AssetAutomator.Services
{
    /// <summary>
    /// Service for managing Batch Image Generation Projects.
    /// Stores project configuration and cards history under ConfigService.CurrentSettings.ProjectsStorageDir
    /// </summary>
    public class BatchProjectService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public string GetProjectsBaseDirectory()
        {
            string dir = ConfigService.CurrentSettings.ProjectsStorageDir;
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
                            string json = await File.ReadAllTextAsync(jsonPath);
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
        /// </summary>
        public async Task<BatchProjectModel> CreateProjectAsync(string projectName, string initialScriptJson = "")
        {
            string baseDir = GetProjectsBaseDirectory();
            string cleanName = string.Join("_", projectName.Split(Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Project_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string projectDir = Path.Combine(baseDir, cleanName);
            string imagesDir = Path.Combine(projectDir, "Images");
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(imagesDir);

            var proj = new BatchProjectModel
            {
                ProjectId = Guid.NewGuid().ToString(),
                ProjectName = projectName,
                CreatedAt = DateTime.Now,
                LastModified = DateTime.Now,
                ScriptJson = initialScriptJson,
                OutputDir = imagesDir
            };

            await SaveProjectAsync(proj);
            return proj;
        }

        /// <summary>
        /// Saves project metadata and items state to project.json
        /// </summary>
        public async Task SaveProjectAsync(BatchProjectModel proj)
        {
            if (proj == null || string.IsNullOrWhiteSpace(proj.ProjectName)) return;

            string baseDir = GetProjectsBaseDirectory();
            string cleanName = string.Join("_", proj.ProjectName.Split(Path.GetInvalidFileNameChars())).Trim();
            string projectDir = Path.Combine(baseDir, cleanName);
            Directory.CreateDirectory(projectDir);

            if (string.IsNullOrWhiteSpace(proj.OutputDir))
            {
                proj.OutputDir = Path.Combine(projectDir, "Images");
                Directory.CreateDirectory(proj.OutputDir);
            }

            proj.LastModified = DateTime.Now;
            string jsonPath = Path.Combine(projectDir, "project.json");

            string jsonString = JsonSerializer.Serialize(proj, _jsonOptions);
            await File.WriteAllTextAsync(jsonPath, jsonString);
        }

        /// <summary>
        /// Deletes a project directory
        /// </summary>
        public void DeleteProject(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName)) return;
            string baseDir = GetProjectsBaseDirectory();
            string cleanName = string.Join("_", projectName.Split(Path.GetInvalidFileNameChars())).Trim();
            string projectDir = Path.Combine(baseDir, cleanName);

            if (Directory.Exists(projectDir))
            {
                Directory.Delete(projectDir, true);
            }
        }
    }
}
