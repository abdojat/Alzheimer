using System;

namespace Alzheimer.Client.Core
{
    public sealed class TurnManager
    {
        public bool IsLocalTurn { get; private set; } = true;
        public string Phase { get; private set; } = "Waiting";

        public void SetLocalTurn(bool isLocalTurn)
        {
            IsLocalTurn = isLocalTurn;
            Phase = isLocalTurn ? "Your turn" : "Opponent turn";
        }

        public void SetPhase(string phase)
        {
            if (string.IsNullOrWhiteSpace(phase))
                throw new ArgumentException("A phase is required.", nameof(phase));
            Phase = phase;
        }
    }
}
