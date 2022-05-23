using System;
using UnityEngine;

namespace PhenomenalViborg.MUCOSDK
{
    public class ServerGUI : MonoBehaviour
    {
        private string m_PortString = "4960";
        private string m_LoadExperienceString = "NewExperience";

        private void OnGUI()
        {
            GUILayout.Window(0, new Rect(0, 0, (Screen.width / 2) - 5, Screen.height), RenderServerWindow, "Server");
            GUILayout.Window(1, new Rect((Screen.width / 2) + 5, 0, (Screen.width / 2) - 5, Screen.height), RenderInfoWindow, "Info");
        }

        private void RenderServerWindow(int windowID)
        {
            GUI.enabled = !ServerNetworkManager.GetInstance().IsStarted();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start Server"))
            {

                if (Int32.TryParse(m_PortString, out int port))
                {
                    ServerNetworkManager.GetInstance().StartServer(port);
                }
                else
                {
                    Debug.LogError("Port string could not be parsed.");
                }
            }
            m_PortString = GUILayout.TextField(m_PortString, 8);
            GUILayout.EndHorizontal();

            
            GUI.enabled = ServerNetworkManager.GetInstance().IsStarted();
            if (GUILayout.Button("Stop Server"))
            {
                ServerNetworkManager.GetInstance().StopServer();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load Experience"))
            {
                ServerNetworkManager.GetInstance().SendLoadExperience(m_LoadExperienceString);
            }
            m_LoadExperienceString = GUILayout.TextField(m_LoadExperienceString, 64);
            GUILayout.EndHorizontal();
        }

        private void RenderInfoWindow(int windowID)
        {
            GUILayout.Label("test");

            /*GUILayout.BeginVertical();
            GUILayout.Label(string.Format("<b>Server</b>\n" +
                $"Address: {Server.GetAddress()}\n" +
                $"Port: {Server.GetPort()}\n" +
                $"Active Connections: {Server.GetConnectionCount()}\n" +
                $"Packets Sent: {Server.GetPacketsSendCount()}\n" +
                $"Packets Received: {Server.GetPacketsReceivedCount()}\n"));

            foreach (MUCOServer.MUCORemoteClient clientInfo in Server.ClientInfo.Values)
            {
                GUILayout.Label(string.Format($"<b>Client {clientInfo.UniqueIdentifier}</b>\n" +
                    $"Address: {clientInfo.GetAddress()}\n" +
                    $"Port: {clientInfo.GetPort()}\n" +
                    $"Packets Sent: {Server.ClientStatistics[clientInfo.UniqueIdentifier].PacketsSent}\n" +
                    $"Packets Received: {Server.ClientStatistics[clientInfo.UniqueIdentifier].PacketsReceived}\n"));
            }

            GUILayout.EndVertical();*/
        }
    }
}