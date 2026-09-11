using System;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class BackoffTests
    {
        [Fact]
        public void Next_DoublesEachAttempt()
        {
            Assert.Equal(TimeSpan.FromSeconds(3), Backoff.Next(0, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60)));
            Assert.Equal(TimeSpan.FromSeconds(6), Backoff.Next(1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60)));
            Assert.Equal(TimeSpan.FromSeconds(12), Backoff.Next(2, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60)));
        }

        [Fact]
        public void Next_CapsAtMaxDelay()
        {
            Assert.Equal(TimeSpan.FromSeconds(60), Backoff.Next(10, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60)));
        }

        [Fact]
        public void Next_NegativeAttemptUsesZero()
        {
            Assert.Equal(TimeSpan.FromSeconds(3), Backoff.Next(-5, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60)));
        }
    }
}
