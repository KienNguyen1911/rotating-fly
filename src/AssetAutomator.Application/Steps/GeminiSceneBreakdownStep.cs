using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    [System.Obsolete("Use GeminiPlaywrightSceneBreakdownStep instead")]
    public class GeminiSceneBreakdownStep
    {
        public async Task ExecuteAsync(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[GEMINI] Legacy scene breakdown - use GeminiPlaywrightSceneBreakdownStep");
            await Task.CompletedTask;
        }
    }
}
