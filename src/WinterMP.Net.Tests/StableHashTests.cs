using WinterMP.Net;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class StableHashTests
    {
        [Fact]
        public void Fnv1a32_IsDeterministic()
        {
            uint a = StableHash.Fnv1a32("MAP/Buildings/Home/Door");
            uint b = StableHash.Fnv1a32("MAP/Buildings/Home/Door");
            Assert.Equal(a, b);
        }

        [Fact]
        public void Fnv1a32_DistinguishesSimilarPaths()
        {
            Assert.NotEqual(
                StableHash.Fnv1a32("MAP/Buildings/Home/Door1"),
                StableHash.Fnv1a32("MAP/Buildings/Home/Door2"));
        }

        [Fact]
        public void Fnv1a32_HandlesNonAsciiPaths()
        {
            Assert.NotEqual(
                StableHash.Fnv1a32("Sauna/Kiuas"),
                StableHash.Fnv1a32("Sauna/Kiuäs"));
        }

        [Fact]
        public void Combine_OrderMatters()
        {
            uint x = StableHash.Fnv1a32("a");
            uint y = StableHash.Fnv1a32("b");
            Assert.NotEqual(StableHash.Combine(x, y), StableHash.Combine(y, x));
        }
    }
}
