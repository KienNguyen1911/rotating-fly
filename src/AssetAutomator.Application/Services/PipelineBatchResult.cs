using System;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Kết quả thực thi batch pipeline.
    /// </summary>
    public class PipelineBatchResult
    {
        public int TotalTasks { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public TimeSpan Elapsed { get; set; }

        public override string ToString()
        {
            return $"✅ {SuccessCount}/{TotalTasks} thành công, ❌ {FailedCount} thất bại — {Elapsed.TotalMinutes:F1} phút";
        }
    }
}