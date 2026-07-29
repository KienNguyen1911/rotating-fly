using System.Collections.Generic;
using System.Threading.Tasks;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Services
{
    public class HistoryService
    {
        public HistoryService(ILogService logService) { }
        public Task SaveTaskToHistoryAsync(AutomationTask task) => Task.CompletedTask;
        public List<string> LoadHistoryDates() => new List<string>();
        public List<HistoryTaskModel> LoadHistoryForDate(string date) => new List<HistoryTaskModel>();
        public Task DeleteHistoryForDateAsync(string date) => Task.CompletedTask;
    }

    public class LicenseService
    {
        public LicenseService(IConfigService configService, ILogService logService) { }
        public Task<LicenseResponse> ActivateAsync(string licenseKey, string? serverUrl = null)
            => Task.FromResult(new LicenseResponse { Success = false, Message = "Placeholder" });
        public Task<LicenseResponse> VerifyAsync(string? serverUrl = null)
            => Task.FromResult(new LicenseResponse { Success = false, Message = "Placeholder" });
    }
}
