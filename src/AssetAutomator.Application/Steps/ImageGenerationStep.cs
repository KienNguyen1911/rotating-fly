using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    public class ImageGenerationStep
    {
        public async Task ExecuteAsync(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 5] Image gen placeholder");
            await Task.CompletedTask;
        }
    }
}
