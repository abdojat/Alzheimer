namespace Alzheimer.Client.Core
{
    public sealed class GameState
    {
        public PlayerState LocalPlayer { get; } = new PlayerState("You");
        public string CurrentPlayerName { get; set; } = "You";
        public bool IsLocalPlayersTurn { get; set; } = true;
        public string StatusMessage { get; set; } = "Local interface preview";
        public bool AlzheimerCallActive { get; set; }
    }
}
