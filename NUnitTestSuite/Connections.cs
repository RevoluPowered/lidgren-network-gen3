﻿
using System;
using System.ComponentModel;
using System.Net;
using System.Threading;
using NUnit.Framework;
using Lidgren;
using Lidgren.Network;
using NUnit.Framework.Constraints;

namespace NUnitTestSuite
{
    [TestFixture]
    public class Connections
    {
        public NetServer StartServer()
        {
            var config = new NetPeerConfiguration("tests")
            {
                Port = 27015,
                MaximumConnections = 10,
                EnableUPnP = true
            };
            

            NetServer server = new NetServer(config);
            server.Start();

            return server;
        }

        public void StopServer( NetServer server )
        {
            server.Shutdown("closing server");
        }

        [Test, Repeat(20)]
        public void NetworkServerInitTest()
        {
            var server = StartServer();
            StopServer(server);

            Assert.AreEqual(NetPeerStatus.ShutdownRequested, server.Status);
            Assert.That(() => server.Status, Is.EqualTo(NetPeerStatus.NotRunning).After(4).Seconds.PollEvery(100));
        }

        public NetClient StartClient()
        {
            // console app sync context
            SynchronizationContext.SetSynchronizationContext( new SynchronizationContext());

            var config = new NetPeerConfiguration("tests");
            
            var client = new NetClient(config);
            client.RegisterReceivedCallback(new SendOrPostCallback(HandleMessageClientCallback));
            client.Start();

            return client;
        }

        public void StopClient(NetClient client)
        {
            client.Shutdown("closing client connection");
        }

        [Test, Repeat(20)]
        public void NetworkClientInitTest()
        {
            var client = StartClient();

            StopClient(client);
        }

        public bool ConnectionStatusHandler( string context, NetIncomingMessage message )
        {
            switch (message.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:
                    NetConnectionStatus status = (NetConnectionStatus) message.ReadByte();
                    TestContext.Out.WriteLine("[" + context + "] Connection status changed: " + status);
                    if (status == NetConnectionStatus.Disconnected)
                    {
                        return true;
                    }
                
                    break;
                default:
                    TestContext.Out.WriteLine("[" + context + "] ConnectionHandler: " + message.ReadString());
                    break;
            }

            return false;
        }

        public void ServerThread()
        {
            var server = StartServer();
            var running = true;
            try
            {
                // enter context for handling messages
                while (running)
                {
                    NetIncomingMessage message;

                    while ((message = server.ReadMessage()) != null)
                    {
                        var status = ConnectionStatusHandler("server", message);
                        if (status)
                        {
                            TestContext.Out.WriteLine("Client has disconnected from the server");

                            running = false;
                            break;
                        }

                        server.Recycle(message);
                    }

                }
            }
            catch (Exception e)
            {
                TestContext.Out.WriteLine(e.ToString());
                throw;
            }
            finally
            {
                TestContext.Out.WriteLine("Stopping server");
                StopServer(server);
            }
        }

        private NetClient client = null;
        private bool _clientShutdown = false;
        private bool _calledDisconnect = false;
        public void HandleMessageClientCallback(object peer)
        {
            TestContext.Out.WriteLine("Client: HandleMessageClientCallback");
            NetIncomingMessage message;

            while ((message = client.ReadMessage()) != null)
            {
                var status = ConnectionStatusHandler("client", message);
                if (status)
                {
                    TestContext.Out.WriteLine("Recieved disconnection flag");
                    _clientShutdown = true;
                    break;
                }

                // disconnect client ONLY when the client has connected
                if (client.ConnectionStatus == NetConnectionStatus.Connected && !_calledDisconnect)
                {
                    TestContext.Out.WriteLine("Informing client socket to disconnect and waiting for proper disconnect flag");
                    client.Disconnect("k thx bye");
                    _calledDisconnect = true;
                }

                client.Recycle(message);
            }
            
        }

        public void ClientThread()
        {
            client = StartClient();
            client.Connect("127.0.0.1", 27015);

            
            while (!_clientShutdown)
            {

                // Do nothing / wait
            }

            TestContext.Out.WriteLine("Stopping client");

            StopClient(client);
            
        }
        
        [Test, MaxTime(5000)]
        public void NetworkConnectDisconnect()
        {
            Thread serverThread = new Thread(ServerThread);
            Thread clientThread = new Thread(ClientThread);

            serverThread.Start();
            clientThread.Start();

            // join for 10 seconds
            serverThread.Join(TimeSpan.FromSeconds(10));
            // server exited, give the client 2-3 seconds to stop.
            clientThread.Join(TimeSpan.FromSeconds(3));
        }
    }
}
