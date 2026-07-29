using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    public class GeminiTopicResearchStep
    {
        public async Task<string?> ExecuteAsync(string topicOrUrl, string outputDir, string? gemId, bool enableDeepResearch, AutomationTask task, Action<AutomationTask, string> logTask, string? selectedModel = null, string? existingSessionId = null)
        {
            logTask(task, "[GEMINI] Topic research placeholder");
            await Task.CompletedTask;
            return existingSessionId;
        }
    }
}
