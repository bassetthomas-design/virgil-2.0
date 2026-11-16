using System.Text.RegularExpressions;

namespace Virgil.App.Speech;

/// <summary>Modèle pur : le propriétaire appelle Tick sans attendre sur son fil.</summary>
public sealed class SpeechReveal(TimeProvider clock)
{
    private long _started;
    private int[] _ends = [];
    public string Text { get; private set; } = string.Empty;
    public string FullText { get; private set; } = string.Empty;
    public string PreviousText { get; private set; } = string.Empty;
    public bool IsComplete => Text.Length == FullText.Length;
    public event Action? Changed;
    public void Start(string text, bool animationsEnabled)
    {
        Complete();
        PreviousText = FullText;
        FullText = text;
        _ends = Regex.Matches(text, @"\S+\s*").Select(m => m.Index + m.Length).ToArray();
        _started = clock.GetTimestamp();
        Text = animationsEnabled && _ends.Length > 0 ? string.Empty : FullText;
        Changed?.Invoke();
    }
    public void Tick()
    {
        if (IsComplete) return;
        var steps = Math.Clamp((int)(clock.GetElapsedTime(_started).TotalMilliseconds / 40), 0, 40);
        var words = Math.Min(_ends.Length, steps * ((_ends.Length + 39) / 40));
        var next = words == 0 ? string.Empty : FullText[.._ends[words - 1]];
        if (next == Text) return;
        Text = next; Changed?.Invoke();
    }
    public void Complete()
    {
        if (IsComplete) return;
        Text = FullText; Changed?.Invoke();
    }
}
