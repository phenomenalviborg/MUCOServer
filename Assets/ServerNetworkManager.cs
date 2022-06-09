using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PhenomenalViborg.MUCONet;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace PhenomenalViborg.MUCOSDK
{
    public struct ServerConfig
    {
        public bool AutoLoadExperience;
        public string AutoLoadExperienceName;
    }

    public struct DeviceInfo
    {
        public float BatteryLevel;
        public BatteryStatus BatteryStatus;
        public string DeviceModel;
        public string DeviceUniqueIdentifier;
        public string OperatingSystem;
    }


    public class ServerNetworkManager : PhenomenalViborg.MUCOSDK.IManager<ServerNetworkManager>, INetEventListener, INetLogger
    {
        private NetManager m_Server;
        private System.UInt16 m_IncrementalIdentifier = 0;
        private List<NetPeer> m_Clients = new List<NetPeer>();
        private Dictionary<NetPeer, int> m_ClientIDs = new Dictionary<NetPeer, int>();
        private NetDataWriter m_DataWriter;

        public List<NetPeer> Clients => m_Clients;

        public delegate void PacketHandler(MUCOPacket packet, NetPeer peer);
        public Dictionary<System.UInt16, PacketHandler> m_PacketHandlers = new Dictionary<System.UInt16, PacketHandler>();

        private bool m_Started = false;

        private void Start()
        {
            RegisterPacketHandler((System.UInt16)EPacketIdentifier.ClientGenericReplicatedUnicast, HandleGenericReplicatedUnicast);
            RegisterPacketHandler((System.UInt16)EPacketIdentifier.ClientGenericReplicatedMulticast, HandleGenericReplicatedMulticast);
        }

        public void RegisterPacketHandler(System.UInt16 packetIdentifier, PacketHandler packetHandler)
        {
            if (m_PacketHandlers.ContainsKey(packetIdentifier))
            {
                MUCOLogger.Error($"Failed to register packet handler to packet identifier: {packetIdentifier}. The specified packet identifier has already been assigned a packet handler.");
                return;
            }

            MUCOLogger.Trace($"Successfully assigned a packet handler to packet identifier: {packetIdentifier}");

            m_PacketHandlers.Add(packetIdentifier, packetHandler);
        }

        public void StartServer(int port)
        {
            NetDebug.Logger = this;
            m_DataWriter = new NetDataWriter();
            m_Server = new NetManager(this);
            m_Clients = new List<NetPeer>();
            m_Server.Start(port);
            m_Server.BroadcastReceiveEnabled = true;
            m_Server.UpdateTime = 15;

            m_Started = true;
        }

        public bool IsStarted()
        {
            return m_Started;
        }

        public void StopServer()
        {
            NetDebug.Logger = null;
            if (m_Server != null)
            {
                m_Server.Stop();
            }
            m_Started = false;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Debug.Log("sending packet");
                using (MUCOPacket packet = new MUCOPacket((System.UInt16)EPacketIdentifier.ServerUserConnected))
                {
                    packet.WriteInt(123);
                    SendPacketToAll(packet);
                }
            }

            if (m_Started)
            {
                m_Server.PollEvents();
            }
        }

        private void OnApplicationQuit()
        {
            if (IsStarted())
            {
                StopServer();
            }
        }

        void INetLogger.WriteNet(NetLogLevel level, string str, params object[] args)
        {
            Debug.LogFormat(str, args);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            m_Clients.Add(peer);
           
            OnClientConnected(peer);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            m_Clients.Remove(peer);
            OnClientDisconnected(peer);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Debug.Log("[SERVER] error " + socketError);
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            Debug.Log($"OnNetworkReceive, Raw data size: {reader.RawDataSize}, User data size: {reader.UserDataSize}");


            byte[] payload = new byte[reader.UserDataSize];
            reader.GetBytes(payload, reader.UserDataSize);
            using (MUCOPacket packet = new MUCOPacket(payload))
            {
                System.UInt16 packetID = packet.ReadUInt16();
                Debug.Log($"PacketID: {packetID}");

                if (m_PacketHandlers.ContainsKey(packetID))
                {
                    m_PacketHandlers[packetID](packet, peer);
                }
                else
                {
                    Debug.LogError($"Failed to find package handler for packet with identifier: {packetID}");
                }
            }
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            Debug.Log("OnConnectionRequest");
            request.Accept();
        }

        public void SendPacket(NetPeer receiver, MUCOPacket packet, bool writeSize = true)
        {
            m_DataWriter.Reset();
            m_DataWriter.Put(packet.ToArray(), 0, packet.GetSize());
            receiver.Send(m_DataWriter, DeliveryMethod.Sequenced);

            Debug.Log($"Sending packet, Size: {packet.GetSize()}");
        }

        public void SendPacketToAll(MUCOPacket packet)
        {
            foreach (NetPeer client in m_Clients)
            {
                SendPacket(client, packet, false);
            }
        }

        public void SendPacketToAllExcept(MUCOPacket packet, NetPeer exception)
        {
            foreach (NetPeer client in m_Clients)
            {
                if (client == exception) continue;

                SendPacket(client, packet, false);
            }
        }

        private void OnClientConnected(NetPeer newClient)
        {
            m_ClientIDs[newClient] = m_IncrementalIdentifier;
            m_IncrementalIdentifier++;

            Debug.Log($"User Connected: {m_ClientIDs[newClient]}");

            // Update the newly connected user about all the other users in existance.
            foreach (NetPeer client in m_Clients)
            {
                if (client == newClient) continue;

                using (MUCOPacket packet = new MUCOPacket((System.UInt16)EPacketIdentifier.ServerUserConnected))
                {
                    packet.WriteInt(m_ClientIDs[client]);
                    packet.WriteInt(0);
                    SendPacket(newClient, packet);
                }
            }

            using (MUCOPacket packet = new MUCOPacket((System.UInt16)EPacketIdentifier.ServerUserConnected))
            {
                packet.WriteInt(m_ClientIDs[newClient]);
                packet.WriteInt(0);
                SendPacketToAllExcept(packet, newClient);
            }

            using (MUCOPacket packet = new MUCOPacket((System.UInt16)EPacketIdentifier.ServerUserConnected))
            {
                packet.WriteInt(m_ClientIDs[newClient]);
                packet.WriteInt(1);
                SendPacket(newClient, packet);
            }
        }

        private void OnClientDisconnected(NetPeer disconnectingClient)
        {
            Debug.Log($"User Disconnected: {m_ClientIDs[disconnectingClient]}");

            // Remove the disconnecting user on all clients (includeing the new client).
            using(MUCOPacket packet = new MUCOPacket((System.UInt16)EPacketIdentifier.ServerUserDisconnected))
            {
                packet.WriteInt(m_ClientIDs[disconnectingClient]);
                SendPacketToAll(packet);
            }
        }
 
        private void HandleGenericReplicatedMulticast(MUCOPacket packet, NetPeer sender)
        {
            System.UInt16 packetIdentifier = packet.ReadUInt16();

            using (MUCOPacket multicastPacket = new MUCOPacket(packetIdentifier))
            {
                multicastPacket.WriteBytes(packet.ReadBytes(packet.GetSize() - packet.GetReadOffset()));
                SendPacketToAll(multicastPacket);
            }
        }

        private void HandleGenericReplicatedUnicast(MUCOPacket packet, NetPeer sender)
        {
            int receiverIdentifier = packet.ReadInt();
            foreach (NetPeer client in m_Clients)
            {
                if (receiverIdentifier == m_ClientIDs[client])
                {
                    System.UInt16 packetIdentifier = packet.ReadUInt16();

                    using (MUCOPacket unicastPacket = new MUCOPacket(packetIdentifier))
                    {
                        unicastPacket.WriteBytes(packet.ReadBytes(packet.GetSize() - packet.GetReadOffset()));
                        SendPacket(client, unicastPacket);
                    }

                    return;
                }
            }

        }

    }
}