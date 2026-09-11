using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class SemVerTests
    {
        [Fact]
        public void Compare_PrereleaseOrdersBySuffix()
        {
            Assert.True(SemVer.Compare("0.1.1-rc.2", "0.1.1-rc.1") > 0);
            Assert.True(SemVer.Compare("0.1.1-rc.1", "0.1.1-rc.2") < 0);
        }

        [Fact]
        public void Compare_ReleaseRanksAbovePrerelease()
        {
            Assert.True(SemVer.Compare("0.1.1", "0.1.1-rc.2") > 0);
            Assert.True(SemVer.Compare("0.1.1-rc.2", "0.1.1") < 0);
        }

        [Fact]
        public void Compare_PatchVersionWins()
        {
            Assert.True(SemVer.Compare("0.1.2", "0.1.1") > 0);
            Assert.True(SemVer.Compare("0.1.1", "0.1.2") < 0);
        }

        [Fact]
        public void Compare_EqualVersionsReturnZero()
        {
            Assert.Equal(0, SemVer.Compare("0.1.1", "0.1.1"));
        }

        [Fact]
        public void IsNewer_DetectsNewerCandidate()
        {
            Assert.True(SemVer.IsNewer("0.1.1-rc.2", "0.1.1-rc.1"));
            Assert.False(SemVer.IsNewer("0.1.1-rc.1", "0.1.1-rc.2"));
        }
    }
}
