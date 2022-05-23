using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PhenomenalViborg.MUCONet;

namespace PhenomenalViborg.MUCOSDK
{
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

        private bool m_Started = false;

        private void Start()
        {
            MUCOLogger.LogEvent += Log;
            MUCOLogger.LogLevel = m_LogLevel;

            Server = new MUCOServer();
            Server.RegisterPacketHandler((int)MUCOClientPackets.TranslateUser, HandleTranslateUser);
            Server.RegisterPacketHandler((int)MUCOClientPackets.RotateUser, HandleRotateUser);
            Server.RegisterPacketHandler((int)MUCOClientPackets.DeviceInfo, HandleDeviceInfo);
            Server.RegisterPacketHandler((int)MUCOClientPackets.ReplicatedMulticast, HandleReplicatedMulticast);
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

                    MUCOPacket packet = new MUCOPacket((int)MUCOServerPackets.SpawnUser);
                    packet.WriteInt(clientInfo.UniqueIdentifier);
                    Server.SendPacket(newClientInfo, packet);
                }

                // Spawn the new user on all clients (includeing the new client).
                foreach (MUCOServer.MUCORemoteClient clientInfo in Server.ClientInfo.Values)
                {
                    MUCOPacket packet = new MUCOPacket((int)MUCOServerPackets.SpawnUser);
                    packet.WriteInt(newClientInfo.UniqueIdentifier);
                    Server.SendPacket(clientInfo, packet);
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

                    MUCOPacket packet = new MUCOPacket((int)MUCOServerPackets.RemoveUser);
                    packet.WriteInt(disconnectingClientInfo.UniqueIdentifier);
                    Server.SendPacket(clientInfo, packet);
                }
            });
        }

        # region Packet handlers
        private void HandleTranslateUser(MUCOPacket packet, int fromClient)
        {
            MUCOThreadManager.ExecuteOnMainThread(() =>
            {
                // Update the clinet position locally on the server.
                float positionX = packet.ReadFloat();
                float positionY = packet.ReadFloat();
                float positionZ = packet.ReadFloat();
                m_UserObjects[fromClient].transform.position = new Vector3(positionX, positionY, positionZ);

                // Replicate the packet to the other clients.
                MUCOPacket replicatePacket = new MUCOPacket((int)MUCOServerPackets.TranslateUser);
                replicatePacket.WriteInt(fromClient);
                replicatePacket.WriteFloat(positionX);
                replicatePacket.WriteFloat(positionY);
                replicatePacket.WriteFloat(positionZ);
                Server.SendPacketToAllExceptOne(replicatePacket, Server.ClientInfo[fromClient]);
            });
        }

        private void HandleRotateUser(MUCOPacket packet, int fromClient)
        {
            MUCOThreadManager.ExecuteOnMainThread(() =>
            {
                // Update the clinet rotation locally on the server.
                float eulerAnglesX = packet.ReadFloat();
                float eulerAnglesY = packet.ReadFloat();
                float eulerAnglesZ = packet.ReadFloat();

                m_UserObjects[fromClient].transform.rotation = Quaternion.Euler(new Vector3(eulerAnglesX, eulerAnglesY, eulerAnglesZ));

                // Replicate the packet to the other clients.
                MUCOPacket replicatePacket = new MUCOPacket((int)MUCOServerPackets.RotateUser);
                replicatePacket.WriteInt(fromClient);
                replicatePacket.WriteFloat(eulerAnglesX);
                replicatePacket.WriteFloat(eulerAnglesY);
                replicatePacket.WriteFloat(eulerAnglesZ);
                Server.SendPacketToAllExceptOne(replicatePacket, Server.ClientInfo[fromClient]);
            });
        }

        private void HandleDeviceInfo(MUCOPacket packet, int fromClient)
        {
            DeviceInfo deviceInfo = new DeviceInfo { };

            deviceInfo.BatteryLevel = packet.ReadFloat();
            deviceInfo.BatteryStatus = (BatteryStatus)packet.ReadInt();
            deviceInfo.DeviceModel = packet.ReadString();
            deviceInfo.DeviceUniqueIdentifier = packet.ReadString();
            deviceInfo.OperatingSystem = packet.ReadString();

            ClientDeviceInfo[fromClient] = deviceInfo;
        }

        private void HandleReplicatedMulticast(MUCOPacket packet, int fromClient)
        {
            MUCOThreadManager.ExecuteOnMainThread(() =>
            {
                Debug.Log(packet.GetReadOffset());
                using (MUCOPacket multicastPacket = new MUCOPacket(packet.ReadBytes(packet.UnreadLength())))
                {
                    Server.SendPacketToAll(multicastPacket);
                }
            });
        }
        #endregion

        #region Packet senders

        public void SendLoadExperience(string experienceName)
        {
            Debug.Log($"SendLoadExperience({experienceName}");

            using (MUCOPacket packet = new MUCOPacket((int)MUCOServerPackets.LoadExperience))
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