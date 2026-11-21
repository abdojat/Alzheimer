using UnityEngine;

namespace Alzheimer.Client.Game
{
    public sealed class ReactionTimerController : MonoBehaviour
    {
        [SerializeField] private float secondsRemaining;

        public bool IsRunning { get; private set; }
        public float SecondsRemaining => secondsRemaining;

        public void StartTimer(float seconds)
        {
            secondsRemaining = Mathf.Max(0f, seconds);
            IsRunning = secondsRemaining > 0f;
        }

        public void StopTimer()
        {
            secondsRemaining = 0f;
            IsRunning = false;
        }

        private void Update()
        {
            if (!IsRunning) return;
            secondsRemaining = Mathf.Max(0f, secondsRemaining - Time.deltaTime);
            if (secondsRemaining <= 0f) IsRunning = false;
        }
    }
}
