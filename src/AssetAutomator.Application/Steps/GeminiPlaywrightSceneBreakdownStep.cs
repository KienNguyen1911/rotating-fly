using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    public class GeminiPlaywrightSceneBreakdownStep
    {
        public async Task ExecuteAsync(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[GEMINI] Scene breakdown placeholder");
            await Task.CompletedTask;
        }
    }
}
