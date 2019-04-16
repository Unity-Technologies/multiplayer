using NUnit.Framework;

namespace Unity.Networking.Transport.Tests
{
    public class NetworkHostUnitTests
    {
        private LocalNetworkDriver Driver;
        private LocalNetworkDriver RemoteDriver;

        [SetUp]
        public void IPC_Setup()
        {
            IPCManager.Instance.Initialize(100);

            Driver = new LocalNetworkDriver(new NetworkDataStreamParameter {size = 64});
            RemoteDriver = new LocalNetworkDriver(new NetworkDataStreamParameter {size = 64});
        }

        [TearDown]
        public void IPC_TearDown()
        {
            Driver.Dispose();
            RemoteDriver.Dispose();
            IPCManager.Instance.Destroy();
        }

        [Test]
        public void Listen()
        {
            Driver.Bind(IPCManager.Instance.CreateEndPoint("network_host"));
            Driver.Listen();
            Assert.That(Driver.Listening);
        }

        [Test]
        public void Accept()
        {
            Driver.Bind(IPCManager.Instance.CreateEndPoint("network_host"));
            Driver.Listen();
            Assert.That(Driver.Listening);

            // create connection to test to connect.
            /*var remote =*/ RemoteDriver.Connect(Driver.LocalEndPoint());

            NetworkConnection id;
            DataStreamReader reader;
            const int maximumIterations = 10;
            int count = 0;
            bool connected = false;
            while (count++ < maximumIterations)
            {
                // Clear pending events
                Driver.PopEvent(out id, out reader);
                RemoteDriver.PopEvent(out id, out reader);

                Driver.ScheduleUpdate().Complete();
                RemoteDriver.ScheduleUpdate().Complete();
                var connection = Driver.Accept();
                if (connection != default(NetworkConnection))
                {
                    connected = true;
                }
            }

            Assert.That(connected);
        }
    }
}