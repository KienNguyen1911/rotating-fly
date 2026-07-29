using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    public class YoutubeTopicSuggestionStep
    {
        public async Task ExecuteAsync(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[TOPIC] Topic suggestion placeholder");
            await Task.CompletedTask;
        }
    }
}
