using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AssetAutomator.Services.Providers;

namespace AssetAutomator.Scripts
{
    /// <summary>
    /// Test runner for validating FlowLocalImageGenProvider integration with local Google Flow API (:8787).
    /// </summary>
    public static class TestFlowApiIntegration
    {
        public static async Task RunTestAsync()
        {
            Console.WriteLine("=== Testing FlowLocalImageGenProvider ===");
            string serverUrl = "http://127.0.0.1:8787/v1";
            string apiKey = "flow-local-key";

            // 1. Test Create Project
            Console.WriteLine("\n1. Testing CreateProjectAsync...");
            var (projectId, projectUrl, error) = await FlowLocalImageGenProvider.CreateProjectAsync(serverUrl, apiKey, "C# Test Project - " + DateTime.Now.ToString("HHmmss"));
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"CreateProject Error: {error}");
            }
            else
            {
                Console.WriteLine($"Project Created Successfully!");
                Console.WriteLine($"Project ID: {projectId}");
                Console.WriteLine($"Project URL: {projectUrl}");
            }

            // 2. Test Single Item Generation
            Console.WriteLine("\n2. Testing ProcessSingleItemAsync...");
            var item = new BatchImageItem
            {
                Index = 1,
                Prompt = "A cozy wooden cabin in a snowy pine forest, warm light glowing from windows",
                Model = "gemini-3.1-flash-image",
                AspectRatio = "16:9",
                FlowProjectId = projectId,
                FlowProjectTitle = "C# Test Project"
            };

            string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "TestFlowApi");
            var provider = new FlowLocalImageGenProvider();
            await provider.ProcessSingleItemAsync(item, serverUrl, apiKey, new List<(string, string)>(), outputDir);

            Console.WriteLine($"Item Status: {item.Status}");
            Console.WriteLine($"Image Path: {item.ImagePath}");
            Console.WriteLine($"Media ID: {item.MediaId}");
            Console.WriteLine($"Flow Project ID: {item.FlowProjectId}");
            Console.WriteLine($"Flow Project URL: {item.FlowProjectUrl}");
            if (!string.IsNullOrEmpty(item.ErrorMessage))
            {
                Console.WriteLine($"Error: {item.ErrorMessage}");
            }

            Console.WriteLine("\n=== Flow Local API Test Completed ===");
        }
    }
}
