using Alzheimer.Client.Core;
using UnityEngine;

namespace Alzheimer.Client.Game
{
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        public GameState State { get; } = new GameState();
        public TurnManager Turns { get; } = new TurnManager();

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
    }
}
