using System;
using UnityEngine;

namespace Alzheimer.Client.Networking
{
    public sealed class NetworkManager : MonoBehaviour, INetworkService
    {
        public static NetworkManager Instance { get; private set; }

        public bool IsConnected { get; private set; }
        public event Action<string> StatusChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void Connect(string serverUrl)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                StatusChanged?.Invoke("Server URL is unavailable.");
                Debug.LogWarning("Alzheimer network service requires a server URL.");
                return;
            }

            IsConnected = false;
            StatusChanged?.Invoke("Online lobby transport is not configured yet.");
            Debug.Log("Alzheimer multiplayer is lobby-only on the server; Unity transport is not implemented.");
        }

        public void Disconnect()
        {
            IsConnected = false;
            StatusChanged?.Invoke("Offline preview.");
        }
    }
}
