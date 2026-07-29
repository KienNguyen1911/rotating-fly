using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    public class VoiceoverGenerationStep
    {
        public async Task ExecuteAsync(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 4] Voiceover placeholder");
            await Task.CompletedTask;
        }
    }
}
