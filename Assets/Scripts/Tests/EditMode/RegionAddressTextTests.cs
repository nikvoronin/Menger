using Menger.Core;
using NUnit.Framework;

namespace Menger.Tests
{
    public class RegionAddressTextTests
    {
        [Test]
        public void ToString_ListsTheChildIndices_ShortensLongPaths()
        {
            Assert.AreEqual("root", RegionAddress.Root.ToString());
            Assert.AreEqual("4", RegionAddress.Root.Child(4).ToString());
            Assert.AreEqual("4/7", RegionAddress.Root.Child(4).Child(7).ToString());

            var deep = RegionAddress.Root;
            for (int i = 0; i < 12; i++) deep = deep.Child(i % 20);
            // 12 levels: the last 10 are shown, the rest counted
            Assert.AreEqual("..(2)/2/3/4/5/6/7/8/9/10/11", deep.ToString());
            StringAssert.DoesNotContain("System", deep.ToString());
        }
    }
}
