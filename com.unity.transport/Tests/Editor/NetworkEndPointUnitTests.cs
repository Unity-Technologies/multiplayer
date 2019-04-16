using NUnit.Framework;

namespace Unity.Networking.Transport.Tests
{
    public class NetworkEndPointUnitTests
    {
        [Test]
        public void NetworkEndPoint_Parse_WorksAsExpected()
        {
            ushort port = 12345;
            NetworkEndPoint nep = NetworkEndPoint.LoopbackIpv4;
            nep.Port = port;

            Assert.That(nep.Family == NetworkFamily.UdpIpv4);
            Assert.That(nep.Port == port);

            NetworkEndPoint iep = NetworkEndPoint.Parse("127.0.0.1", port);

            Assert.That(nep == iep);
        }
    }
}