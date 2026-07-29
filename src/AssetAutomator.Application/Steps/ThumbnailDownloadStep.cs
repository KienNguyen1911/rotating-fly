using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    public class ThumbnailDownloadStep
    {
        public async Task ExecuteAsync(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 1] Thumbnail download placeholder");
            await Task.CompletedTask;
        }
    }
}
