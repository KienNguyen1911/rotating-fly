using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Infrastructure.Services;

/// <summary>
/// Manages the lifecycle of <see cref="TaskProfile"/> objects:
/// create, read, update, delete, and persist to a JSON file in AppData.
/// </summary>
public class TaskProfileManager
{
    private readonly string _profilesFilePath;
    private List<TaskProfile> _profiles = new();

    public IReadOnlyList<TaskProfile> Profiles => _profiles.AsReadOnly();

    public event EventHandler? ProfilesChanged;

    public TaskProfileManager()
    {
        string appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AssetAutomator");

        _profilesFilePath = Path.Combine(appDataFolder, "task-profiles.json");

        LoadProfiles();
    }

    /// <summary>
    /// Loads profiles from disk, or creates default templates on first run.
    /// </summary>
    private void LoadProfiles()
    {
        try
        {
            if (File.Exists(_profilesFilePath))
            {
                string json = File.ReadAllText(_profilesFilePath);
                var loaded = JsonSerializer.Deserialize<List<TaskProfile>>(json);
                if (loaded != null && loaded.Count > 0)
                {
                    _profiles = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TaskProfileManager] Load error: {ex.Message}");
        }

        // First-run: seed two useful templates. Deep Research and API Stream are
        // hardcoded to true (those toggles were removed from the UI), but we keep
        // them in the model for backward-compat with older serialized profiles.
        _profiles = new List<TaskProfile>
        {
            new TaskProfile
            {
                Name = "🎬 Psychology Channel",
                ScriptwriterGemId = "",
                ScriptwriterGemName = "",
                ScriptwriterModel = "gemini-3-flash-plus",
                EnableDeepResearch = true,
                SceneCreatorGemId = "",
                SceneCreatorGemName = "",
                SceneCreatorModel = "gemini-3-flash-plus",
                UseApiStreamForSceneCreator = true,
                VoiceId = "",
                SelectedImageProvider = "flow_local",
                CharacterRef = ""
            },
            new TaskProfile
            {
                Name = "📚 Book Review",
                ScriptwriterGemId = "",
                ScriptwriterGemName = "",
                ScriptwriterModel = "gemini-3-flash-plus",
                EnableDeepResearch = true,
                SceneCreatorGemId = "",
                SceneCreatorGemName = "",
                SceneCreatorModel = "gemini-3-flash-plus",
                UseApiStreamForSceneCreator = true,
                VoiceId = "",
                SelectedImageProvider = "flow_local",
                CharacterRef = ""
            }
        };

        SaveProfiles();
    }

    /// <summary>
    /// Persists the current profile list to disk.
    /// </summary>
    public void SaveProfiles()
    {
        try
        {
            string folder = Path.GetDirectoryName(_profilesFilePath) ?? "";
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_profiles, options);
            File.WriteAllText(_profilesFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TaskProfileManager] Save error: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a new profile and persists to disk.
    /// </summary>
    public TaskProfile AddProfile(TaskProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            profile.Name = $"Profile #{_profiles.Count + 1}";

        profile.Id = Guid.NewGuid().ToString("N");
        profile.CreatedAt = DateTime.Now;
        profile.UpdatedAt = DateTime.Now;

        _profiles.Add(profile);
        SaveProfiles();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);

        return profile;
    }

    /// <summary>
    /// Updates an existing profile (matched by Id) and persists to disk.
    /// </summary>
    public bool UpdateProfile(TaskProfile updated)
    {
        int idx = _profiles.FindIndex(p => p.Id == updated.Id);
        if (idx < 0) return false;

        updated.UpdatedAt = DateTime.Now;
        _profiles[idx] = updated;
        SaveProfiles();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// Removes a profile by Id and persists to disk.
    /// </summary>
    public bool RemoveProfile(string profileId)
    {
        int removed = _profiles.RemoveAll(p => p.Id == profileId);
        if (removed == 0) return false;

        SaveProfiles();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Returns a profile by Id, or null if not found.
    /// </summary>
    public TaskProfile? GetProfile(string profileId)
    {
        return _profiles.FirstOrDefault(p => p.Id == profileId);
    }

    /// <summary>
    /// Creates a copy of an existing profile with a new Id and " (Copy)" suffix.
    /// </summary>
    public TaskProfile? DuplicateProfile(string profileId)
    {
        var original = GetProfile(profileId);
        if (original == null) return null;

        var clone = original.Clone();
        _profiles.Add(clone);
        SaveProfiles();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);

        return clone;
    }
}
