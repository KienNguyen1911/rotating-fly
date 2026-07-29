using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AssetAutomator.Core;

namespace AssetAutomator
{
    /// <summary>
    /// Simulates human-like browser interactions to avoid bot detection.
    /// Extracted from MainWindow.AutomationSteps.cs for reuse across automation steps.
    /// </summary>
    public static class HumanBehaviourHelper
    {
        private static readonly Random _random = new();

        /// <summary>
        /// Types text character-by-character with random delays to simulate human typing.
        /// </summary>
        public static async Task TypeLikeHumanAsync(IPage page, ILocator locator, string text)
        {
            await locator.FocusAsync();
            foreach (var ch in text)
            {
                await page.Keyboard.TypeAsync(ch.ToString());
                await Task.Delay(_random.Next(Delays.HumanTypingMinMs, Delays.HumanTypingMaxMs));
            }
        }

        /// <summary>
        /// Performs random mouse movements across the page to simulate human behavior.
        /// </summary>
        public static async Task RandomMouseMovementAsync(IPage page)
        {
            int steps = _random.Next(3, 8);
            for (int i = 0; i < steps; i++)
            {
                int x = _random.Next(100, 700);
                int y = _random.Next(100, 500);
                await page.Mouse.MoveAsync(x, y, new MouseMoveOptions { Steps = _random.Next(2, 5) });
                await Task.Delay(_random.Next(Delays.HumanActionMinMs, Delays.HumanActionMaxMs));
            }
        }
    }
}
