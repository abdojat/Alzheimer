using System;
#nullable enable

namespace Alzheimer.Client.Networking
{
    public interface INetworkService
    {
        bool IsConnected { get; }
        void Connect(string serverUrl);
        void Disconnect();
        event Action<string>? StatusChanged;
    }
}
