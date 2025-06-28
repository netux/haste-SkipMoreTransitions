namespace HasteSkipMoreTransitionsMod.Skips;

public class PostGameScreensSkip : IOneTimeSkippableSkip
{
    public class State
    {
        static readonly float COOLDOWN_SECONDS = 0.3f;

        private float lastSkipTime = 0;

        public void MarkSkipped() =>
            lastSkipTime = UnityEngine.Time.realtimeSinceStartup;

        public bool CanSkip() =>
            UnityEngine.Time.realtimeSinceStartup - state.lastSkipTime > COOLDOWN_SECONDS;
    }

    public static State state = new();

    public override bool MultiplayerCompatible { get => true; }

    public override void Initialize() { /* no-op */ }

    public override bool TrySkip()
    {
        var postGameScreen = UnityEngine.Object.FindObjectOfType<PostGameScreen>();
        if (postGameScreen == null)
        {
            return false;
        }

        var postGameFadeIn = UnityEngine.Object.FindObjectOfType<PostGame_FadeIn>();
        if (postGameFadeIn != null && postGameFadeIn.counter < postGameFadeIn.duration)
        {
            return false;
        }

        if (!state.CanSkip())
        {
            return false;
        }

        postGameScreen.Continue();
        state.MarkSkipped();

        return true;
    }
}
