using System.Threading.Tasks;
using Microsoft.Playwright;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    public class TranscriptExtractionStep
    {
        private readonly IConfigService _configService;
        public TranscriptExtractionStep(IConfigService configService) { _configService = configService; }

        public Task<string?> ExecuteAsync(AutomationTask task, IBrowserContext context, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 2] Transcript extraction placeholder");
            return Task.FromResult<string?>(null);
        }
    }
}
