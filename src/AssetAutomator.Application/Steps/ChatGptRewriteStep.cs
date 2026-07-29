using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    public class ChatGptRewriteStep
    {
        public async Task<string?> ExecuteAsync(string targetLanguage, string outputDir, string transcriptText, string videoId, AutomationTask task, Microsoft.Playwright.IBrowserContext context, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 3] ChatGPT rewrite placeholder");
            await Task.CompletedTask;
            return null;
        }
    }
}
