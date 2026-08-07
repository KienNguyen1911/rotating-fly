using System;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Simple in-process pub/sub used by the Gemini pipeline to notify the
    /// Batch Image Gen dashboard whenever a new / updated BatchProject is
    /// persisted to disk. Without this hook the dashboard only refreshes when
    /// the user opens the tab, so pipeline runs that finish while another tab
    /// is in focus appear silently — and the user can't retry failed scenes
    /// from the dashboard until they manually switch tabs.
    ///
    /// Kept deliberately tiny: a single event, no payload, no async. Subscribers
    /// should be cheap and non-blocking (just call <c>LoadProjectsListAsync</c>).
    /// For richer cross-process messaging we'd need a real bus, but the WinUI
    /// app is single-process so this is enough.
    /// </summary>
    public static class PipelineEvents
    {
        /// <summary>
        /// Raised after a BatchProject has been created or updated on disk by
        /// the Gemini pipeline (SceneImageBatchStep). Subscribers should
        /// refresh their Batch Image Gen dashboard view.
        /// </summary>
        public static event EventHandler<BatchProjectUpdatedEventArgs>? BatchProjectUpdated;

        public static void RaiseBatchProjectUpdated(string projectName)
        {
            try
            {
                BatchProjectUpdated?.Invoke(
                    null,
                    new BatchProjectUpdatedEventArgs(projectName ?? string.Empty));
            }
            catch
            {
                // Never let a misbehaving subscriber crash the pipeline.
            }
        }

        /// <summary>
        /// Test hook: drop all subscribers. Only used by unit tests.
        /// </summary>
        public static void ResetForTesting()
        {
            BatchProjectUpdated = null;
        }
    }

    public class BatchProjectUpdatedEventArgs : EventArgs
    {
        public string ProjectName { get; }
        public DateTime UpdatedAt { get; } = DateTime.Now;

        public BatchProjectUpdatedEventArgs(string projectName)
        {
            ProjectName = projectName;
        }
    }
}
