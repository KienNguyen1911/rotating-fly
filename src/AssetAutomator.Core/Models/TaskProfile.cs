using System;

namespace AssetAutomator.Core.Models;

/// <summary>
/// A reusable named configuration bundle for a group of Gemini tasks.
/// Mirrors the per-task settings in <see cref="GeminiTaskModel"/> so a user
/// can define "Psychology Channel", "Book Review", etc. once and apply them
/// to any task with one click.
/// </summary>
public class TaskProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // ── Scriptwriter ──────────────────────────────────────
    /// <summary>
    /// The Gemini Gem id used as the Scriptwriter (Deep Research + Script).
    /// Stored as a plain string so the profile remains valid even if the Gem
    /// list is not yet loaded.
    /// </summary>
    public string ScriptwriterGemId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the Scriptwriter Gem (for UI only; not used at runtime).
    /// </summary>
    public string ScriptwriterGemName { get; set; } = string.Empty;

    /// <summary>
    /// AI model for the Scriptwriter step (e.g. "gemini-3-pro-plus").
    /// </summary>
    public string ScriptwriterModel { get; set; } = "gemini-3-flash-plus";

    /// <summary>
    /// Whether Deep Research is enabled for tasks using this profile.
    /// </summary>
    public bool EnableDeepResearch { get; set; } = true;

    // ── Scene Creator ─────────────────────────────────────
    /// <summary>
    /// The Gemini Gem id used for Scene Breakdown.
    /// </summary>
    public string SceneCreatorGemId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the Scene Creator Gem (for UI only).
    /// </summary>
    public string SceneCreatorGemName { get; set; } = string.Empty;

    /// <summary>
    /// AI model for the Scene Creator step.
    /// </summary>
    public string SceneCreatorModel { get; set; } = "gemini-3-flash-plus";

    /// <summary>
    /// When true, uses the Python REST API for scene creation (fast, no Chrome).
    /// When false, uses Playwright with a Chrome profile.
    /// </summary>
    public bool UseApiStreamForSceneCreator { get; set; } = true;

    // ── Voiceover ─────────────────────────────────────────
    /// <summary>
    /// Voice ID for the AI84 TTS step (e.g. "en-US-Standard-A").
    /// </summary>
    public string VoiceId { get; set; } = string.Empty;

    // ── Image Generation ────────────────────────────────────
    /// <summary>
    /// Image generation provider (e.g. "flow_local").
    /// </summary>
    public string SelectedImageProvider { get; set; } = "flow_local";

    /// <summary>
    /// Path to the character reference image used for consistent character in videos.
    /// </summary>
    public string CharacterRef { get; set; } = string.Empty;

    /// <summary>
    /// Applies this profile's settings to a <see cref="GeminiTaskModel"/>.
    /// Only applies properties that are non-empty / non-default so the caller
    /// can selectively override specific fields.
    /// </summary>
    public void ApplyTo(GeminiTaskModel task)
    {
        if (task == null) return;

        if (!string.IsNullOrEmpty(ScriptwriterGemId))
            task.SelectedScriptwriterGem = new GemOptionItem { Id = ScriptwriterGemId, Name = ScriptwriterGemName };

        if (!string.IsNullOrEmpty(ScriptwriterModel))
            task.ScriptwriterModel = ScriptwriterModel;

        task.EnableDeepResearch = EnableDeepResearch;

        if (!string.IsNullOrEmpty(SceneCreatorGemId))
            task.SelectedSceneCreatorGem = new GemOptionItem { Id = SceneCreatorGemId, Name = SceneCreatorGemName };

        if (!string.IsNullOrEmpty(SceneCreatorModel))
            task.SceneCreatorModel = SceneCreatorModel;

        task.UseApiStreamForSceneCreator = UseApiStreamForSceneCreator;

        if (!string.IsNullOrEmpty(VoiceId))
            task.VoiceId = VoiceId;

        if (!string.IsNullOrEmpty(SelectedImageProvider))
            task.SelectedImageProvider = SelectedImageProvider;

        if (!string.IsNullOrEmpty(CharacterRef))
            task.CharacterRef = CharacterRef;
    }

    /// <summary>
    /// Creates a clone of this profile with a new Id and a copy of all settings.
    /// </summary>
    public TaskProfile Clone()
    {
        return new TaskProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Name + " (Copy)",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            ScriptwriterGemId = ScriptwriterGemId,
            ScriptwriterGemName = ScriptwriterGemName,
            ScriptwriterModel = ScriptwriterModel,
            EnableDeepResearch = EnableDeepResearch,
            SceneCreatorGemId = SceneCreatorGemId,
            SceneCreatorGemName = SceneCreatorGemName,
            SceneCreatorModel = SceneCreatorModel,
            UseApiStreamForSceneCreator = UseApiStreamForSceneCreator,
            VoiceId = VoiceId,
            SelectedImageProvider = SelectedImageProvider,
            CharacterRef = CharacterRef,
        };
    }
}
