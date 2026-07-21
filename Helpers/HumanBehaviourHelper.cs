using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace AssetAutomator
{
    /// <summary>
    /// Simulates human-like browser interactions to avoid bot detection.
    /// Extracted from MainWindow.AutomationSteps.cs for reuse across automation steps.
    /// </summary>
    public static class HumanBehaviourHelper
    {
        /// <summary>
        /// Types text character-by-character with random delays to simulate human typing.
        /// </summary>
        public static async Task TypeLikeHumanAsync(IPage page, ILocator locator, string text)
        {
            await locator.FocusAsync();
            var random = new Random();
            foreach (var ch in text)
            {
                await page.Keyboard.TypeAsync(ch.ToString());
                await Task.Delay(random.Next(20, 70));
            }
        }

        /// <summary>
        /// Performs random mouse movements across the page to simulate human behavior.
        /// </summary>
        public static async Task RandomMouseMovementAsync(IPage page)
        {
            var random = new Random();
            int steps = random.Next(3, 8);
            for (int i = 0; i < steps; i++)
            {
                int x = random.Next(100, 700);
                int y = random.Next(100, 500);
                await page.Mouse.MoveAsync(x, y, new MouseMoveOptions { Steps = random.Next(2, 5) });
                await Task.Delay(random.Next(50, 150));
            }
        }
    }
}
