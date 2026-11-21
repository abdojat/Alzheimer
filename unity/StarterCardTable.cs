using UnityEngine;

// UI smoke test only. Displays mock cards; it is NOT hooked to the multiplayer server
// and intentionally does not model draws, hand knowledge or turn validation.
public sealed class StarterCardTable : MonoBehaviour
{
    private string _message = "Unity client is running (local demo only)";

    private void OnGUI()
    {
        const float cardW = 70;
        const float cardH = 105;
        const float spacing = 12;
        var total = cardW * 4 + spacing * 3;
        var left = (Screen.width - total) / 2f;
        var top = Screen.height / 2f - 60;

        GUI.Label(new Rect(left, top - 90, total + 30, 32), "ALZHEIMER — UI STARTER");
        GUI.Label(new Rect(left, top - 60, total + 80, 32), _message);

        for (var i = 0; i < 4; i++)
        {
            var cardIndex = i;
            if (GUI.Button(new Rect(left + i * (cardW + spacing), top, cardW, cardH), "?"))
                _message = $"Pressed mock card {cardIndex + 1} (not a gameplay move)";
        }

        if (GUI.Button(new Rect(left, top + cardH + 20, total, 42), "Test button"))
            _message = "Button input works. Next: connect to authoritative server.";
    }
}
