using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Manages the image generation request pool with worker-based concurrency.
    /// </summary>
    public class ImagePoolService
    {
        private readonly List<Core.Models.ImageGenRequest> _imageRequestPool = new();
        private readonly object _poolLock = new();
        private int _maxImageWorkers = 1;
        private int _activeImageWorkers = 0;

        public Action<Core.Models.AutomationTask, string>? LogTask { get; set; }
        public event Action? OnPoolStateChanged;
        public Func<Core.Models.ImageGenRequest, Task>? EditImageFunc { get; set; }

        public List<Core.Models.ImageGenRequest> GetAllRequests()
        {
            lock (_poolLock)
            {
                return _imageRequestPool.ToList();
            }
        }

        public (int ActiveWorkers, int MaxWorkers, int Waiting, int Processing, int Finished, double AvgSeconds) GetPoolStats()
        {
            lock (_poolLock)
            {
                int waiting = _imageRequestPool.Count(r => r.Status == "Waiting");
                int processing = _imageRequestPool.Count(r => r.Status == "Processing");
                int finished = _imageRequestPool.Count(r => r.Status == "Done" || r.Status == "Failed");

                var processedRequests = _imageRequestPool.Where(r => r.StartedAt != null && r.FinishedAt != null).ToList();
                double avgSeconds = 0;
                if (processedRequests.Count > 0)
                {
                    avgSeconds = processedRequests.Average(r => (r.FinishedAt!.Value - r.StartedAt!.Value).TotalSeconds);
                }

                return (_activeImageWorkers, _maxImageWorkers, waiting, processing, finished, avgSeconds);
            }
        }

        public List<Core.Models.ImageGenRequest> GetOrderedRequests()
        {
            lock (_poolLock)
            {
                return _imageRequestPool.OrderByDescending(r => r.EnqueuedAt).ToList();
            }
        }

        public async Task EnqueueImageRequestAsync(Core.Models.ImageGenRequest request)
        {
            try
            {
                int calculatedWorkers = Math.Max(1, Math.Min(10, Infrastructure.Services.ConfigService.CurrentSettings.MaxConcurrentTasks * 2));
                lock (_poolLock)
                {
                    _maxImageWorkers = calculatedWorkers;
                }
            }
            catch (Exception ex)
            {
                LogTask?.Invoke(request.Task, $"[POOL] Error determining max workers: {ex.Message}. Falling back to default workers.");
            }

            lock (_poolLock)
            {
                _imageRequestPool.Add(request);
                LogTask?.Invoke(request.Task, $"[POOL] Enqueued image request: {Path.GetFileName(request.SavePath)} (Status: {request.Status})");

                while (_activeImageWorkers < _maxImageWorkers)
                {
                    _activeImageWorkers++;
                    _ = Task.Run(async () => await ImageWorkerLoopAsync());
                }
            }
            OnPoolStateChanged?.Invoke();
        }

        private async Task ImageWorkerLoopAsync()
        {
            while (true)
            {
                Core.Models.ImageGenRequest? req = null;
                lock (_poolLock)
                {
                    req = GetNextRequestToProcess();
                    if (req == null)
                    {
                        _activeImageWorkers--;
                        OnPoolStateChanged?.Invoke();
                        break;
                    }
                }

                try
                {
                    req.StartedAt = DateTime.Now;
                    OnPoolStateChanged?.Invoke();

                    LogTask?.Invoke(req.Task, $"[POOL] Starting API generation for: {Path.GetFileName(req.SavePath)}");

                    if (EditImageFunc != null)
                    {
                        await EditImageFunc(req);
                    }

                    lock (_poolLock)
                    {
                        req.Status = "Done";
                        req.FinishedAt = DateTime.Now;
                    }
                    req.Tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    lock (_poolLock)
                    {
                        req.Status = "Failed";
                        req.FinishedAt = DateTime.Now;
                        req.ErrorMessage = ex.Message;
                    }
                    LogTask?.Invoke(req.Task, $"[POOL] [ERROR] Image generation failed for {Path.GetFileName(req.SavePath)}: {ex.Message}");
                    req.Tcs.SetException(ex);
                }
                finally
                {
                    OnPoolStateChanged?.Invoke();
                }

                await Task.Delay(5000);
            }
        }

        private Core.Models.ImageGenRequest? GetNextRequestToProcess()
        {
            var inProgressTaskIds = _imageRequestPool
                .Where(r => r.Status == "Processing")
                .Select(r => r.Task.VideoId)
                .Distinct()
                .ToList();

            foreach (var taskId in inProgressTaskIds)
            {
                var nextInSameTask = _imageRequestPool.FirstOrDefault(r => r.Task.VideoId == taskId && r.Status == "Waiting");
                if (nextInSameTask != null)
                {
                    nextInSameTask.Status = "Processing";
                    return nextInSameTask;
                }
            }

            var nextRequest = _imageRequestPool
                .Where(r => r.Status == "Waiting")
                .OrderBy(r => r.EnqueuedAt)
                .FirstOrDefault();

            if (nextRequest != null)
            {
                nextRequest.Status = "Processing";
            }
            return nextRequest;
        }
    }
}
