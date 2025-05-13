namespace HasteSkipMoreTransitionsMod.Skips;

public abstract class ISkippableSkip
{
    public abstract void Initialize();
}

public abstract class IOneTimeSkippableSkip : ISkippableSkip
{
    public abstract bool TrySkip();
}

public abstract class IHoldSkippableSkip : ISkippableSkip
{
    public abstract float Threshold { get; }
    public abstract bool TrySkip();
}