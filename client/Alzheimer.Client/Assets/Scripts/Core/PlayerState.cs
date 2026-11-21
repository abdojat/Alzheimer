using System;

namespace Alzheimer.Client.Core
{
    public sealed class PlayerState
    {
        public PlayerState(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A player name is required.", nameof(displayName));
            DisplayName = displayName;
        }

        public string DisplayName { get; }
    }
}
