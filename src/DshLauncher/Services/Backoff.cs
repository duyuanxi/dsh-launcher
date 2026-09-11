using System;

namespace DshLauncher.Services
{
    /// <summary>
    /// Exponential backoff for the crash-restart watchdog.
    /// </summary>
    public static class Backoff
    {
        public static TimeSpan Next(int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
        {
            if (attempt < 0)
            {
                attempt = 0;
            }

            var ms = baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
            return TimeSpan.FromMilliseconds(Math.Min(ms, maxDelay.TotalMilliseconds));
        }
    }
}
