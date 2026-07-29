using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    public class SceneImageBatchStep
    {
        public async Task ExecuteAsync(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[SCENE] Scene batch placeholder");
            await Task.CompletedTask;
        }
    }
}
