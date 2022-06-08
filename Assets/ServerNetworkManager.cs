using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PhenomenalViborg.MUCONet;

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

    public class ServerNetworkManager : MUCOSingleton<ServerNetworkManager>
    {
        [HideInInspector] public MUCOServer Server { get; private set; } = null;

        [Header("Debug")]
        [SerializeField] private MUCOLogMessage.MUCOLogLevel m_LogLevel = MUCOLogMessage.MUCOLogLevel.Info;

        // TODO: MOVE
        [SerializeField] private Dictionary<int, GameObject> m_UserObjects = new Dictionary<int, GameObject>();
        [HideInInspector] public Dictionary<int, DeviceInfo> ClientDeviceInfo = new Dictionary<int, DeviceInfo>();
        [SerializeField] private GameObject m_RemoteUserPrefab = null;

        public ServerConfig Config;

        private bool m_Started = false;

        private void Start()
        {
            Config = new ServerConfig();
            Config.AutoLoadExperience = false;
            Config.AutoLoadExperienceName = "NewExperience";

            MUCOLogger.LogEvent += Log;
            MUCOLogger.LogLevel = m_LogLevel;

            Server = new MUCOServer();
            Server.RegisterPacketHandler((System.UInt16)EPacketIdentifier.ClientGenericReplicatedUnicast, HandleGenericReplicatedUnicast);
            Server.RegisterPacketHandler((System.UInt16)EPacketIdentifier.ClientGenericReplicatedMulticast, HandleGenericReplicatedMulticast);
            Server.OnClientConnectedEvent += OnClientConnected;
            Server.OnClientDisconnectedEvent += OnClientDisconnected;
        }

        public void StartServer(int port)
        {
            m_Started = true;
            Server.Start(port);
        }

        public bool IsStarted()
        {
            return m_Started;
        }

        public void StopServer()
        {
            Server.Stop();
            m_Started = false;
        }

        private void OnApplicationQuit()
        {
            if (m_Started)
            {
                StopServer();
            }
        }

        private void OnClientConnected(MUCOServer.MUCORemoteClient newClientInfo)
        {
            MUCOThreadManager.ExecuteOnMainThread(() =>
            {
                // Create a user object on the server.
                Debug.Log($"User Connected: {newClientInfo}");
                m_UserObjects[newClientInfo.UniqueIdentifier] = Instantiate(m_RemoteUserPrefab);
                DontDestroyOnLoad(m_UserObjects[newClientInfo.UniqueIdentifier]);
                User user = m_UserObjects[newClientInfo.UniqueIdentifier].GetComponent<User>();
                user.Initialize(newClientInfo.UniqueIdentifier, false);

                // Update the newly connected user about all the other users in existance.
                foreach (MUCOServer.MUCORemoteClient clientInfo in Server.ClientInfo.Values)
                {
                    if (clientInfo.UniqueIdentifier == newClientInfo.UniqueIdentifier)
                    {
                        continue;
                    }

                    MUCOPacket packet = new MUCOPacket((System.UInt16)EPacketIdentifier.ServerUserConnected);
                    packet.WriteInt(clientInfo.UniqueIdentifier);
                    Server.SendPacket(newClientInfo, packet);
                }

                // Spawn the new user on all clients (includeing the new client).
                foreach (MUCOServer.MUCORemoteClient clientInfo in Server.ClientInfo.Values)
                {
                    MUCOPacket packet = new MUCOPacket((System.UInt16)EPacketIdentifier.ServerUserConnected);
                    packet.WriteInt(newClientInfo.UniqueIdentifier);
                    Server.SendPacket(clientInfo, packet);
                }

                // Send load experience, if auto load experience is enabled
                if (Config.AutoLoadExperience)
                {
                    using (MUCOPacket packet = new MUCOPacket((System.UInt16)EPacketIdentifier.ServerLoadExperience))
                    {
                        packet.WriteString(Config.AutoLoadExperienceName);
                        Server.SendPacket(newClientInfo, packet, true);
                    }
                }
            });
        }

        private void OnClientDisconnected(MUCOServer.MUCORemoteClient disconnectingClientInfo)
        {
            MUCOThreadManager.ExecuteOnMainThread(() =>
            {
                // Destroy disconnected users game object.
                Debug.Log($"User Disconnected: {disconnectingClientInfo}");
                Destroy(m_UserObjects[disconnectingClientInfo.UniqueIdentifier]);
                m_UserObjects[disconnectingClientInfo.UniqueIdentifier] = null;

                // Remove the disconnecting user on all clients (includeing the new client).
                foreach (MUCOServer.MUCORemoteClient clientInfo in Server.ClientInfo.Values)
                {
                    if (clientInfo.UniqueIdentifier == disconnectingClientInfo.UniqueIdentifier)
                    {
                        continue;
                    }

                    MUCOPacket packet = new MUCOPacket((System.UInt16)EPacketIdentifier.ServerUserConnected);
                    packet.WriteInt(disconnectingClientInfo.UniqueIdentifier);
                    Server.SendPacket(clientInfo, packet);
                }
            });
        }

        # region Packet handlers

        /*private void HandleDeviceInfo(MUCOPacket packet, int fromClient)
        {
            DeviceInfo deviceInfo = new DeviceInfo { };

            deviceInfo.BatteryLevel = packet.ReadFloat();
            deviceInfo.BatteryStatus = (BatteryStatus)packet.ReadInt();
            deviceInfo.DeviceModel = packet.ReadString();
            deviceInfo.DeviceUniqueIdentifier = packet.ReadString();
            deviceInfo.OperatingSystem = packet.ReadString();

            ClientDeviceInfo[fromClient] = deviceInfo;
        }*/

        private void HandleGenericReplicatedMulticast(MUCOPacket packet, int fromClient)
        {
            MUCOThreadManager.ExecuteOnMainThread(() =>
            {
                System.UInt16 packetIdentifier = packet.ReadUInt16();
                using (MUCOPacket multicastPacket = new MUCOPacket(packetIdentifier))
                {
                    multicastPacket.WriteBytes(packet.ReadBytes(packet.GetSize() - packet.GetReadOffset()));
                    Server.SendPacketToAll(multicastPacket);
                }
            });
        }

        private void HandleGenericReplicatedUnicast(MUCOPacket packet, int fromClient)
        {
            MUCOThreadManager.ExecuteOnMainThread(() =>
            {
                int receiverIdentifier = packet.ReadInt();
                if (!Server.ClientInfo.ContainsKey(receiverIdentifier))
                {
                    Debug.Log($"Failed to find the designated receiver for unicast packet. The requested identifier was: {receiverIdentifier}.");
                    return;
                }
                MUCOServer.MUCORemoteClient receiver = Server.ClientInfo[receiverIdentifier];

                System.UInt16 packetIdentifier = packet.ReadUInt16();
                using (MUCOPacket unicastPacket = new MUCOPacket(packetIdentifier))
                {
                    unicastPacket.WriteBytes(packet.ReadBytes(packet.GetSize() - packet.GetReadOffset()));
                    Server.SendPacket(receiver, unicastPacket);
                }
            });
        }
        #endregion

        #region Packet senders

        public void SendLoadExperience(string experienceName)
        {
            Debug.Log($"SendLoadExperience({experienceName}");

            using (MUCOPacket packet = new MUCOPacket((System.UInt16)EPacketIdentifier.ServerLoadExperience))
            {
                packet.WriteString(experienceName);
                Server.SendPacketToAll(packet, true);
            }
        }
        #endregion

        private static void Log(MUCOLogMessage message)
        {
            Debug.Log(message.ToString());
        }
    }
}