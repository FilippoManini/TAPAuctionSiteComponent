using NUnit.Framework;
using TAP21_22_AuctionSite.Interface;

namespace TAP21_22.AuctionSite.Testing {
    [TestFixture]
    class DbContextTests : AuctionSiteTests {
        [Test]
        public void DbContext_Extends_TAPDbContext() {
            Assert.That(TapDbContext.NumberOfCreatedContexts, Is.Positive);
        }

        [Test]
        public void OnConfiguringIsOk()
        {
            Assert.That(TapDbContext.OnConfiguringOk);
        }
    }
}