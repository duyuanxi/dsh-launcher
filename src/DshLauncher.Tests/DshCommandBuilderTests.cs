using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class DshCommandBuilderTests
    {
        [Fact]
        public void BuildArguments_ProducesExpectedArgv()
        {
            var args = DshCommandBuilder.BuildArguments(8080, "127.0.0.1");
            Assert.Equal(
                new[] { "web", "--port", "8080", "--host", "127.0.0.1", "--no-open" },
                args);
        }

        [Fact]
        public void BuildUrl_ComposesLoopbackUrl()
        {
            Assert.Equal("http://127.0.0.1:3080", DshCommandBuilder.BuildUrl("127.0.0.1", 3080));
        }
    }
}
