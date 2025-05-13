using HasteSkipMoreTransitionsMod.Skips;
using UnityEngine;
using UnityEngine.InputSystem;
using Zorro.Core;

namespace HasteSkipMoreTransitionsMod;

public class TransitionSkipper : MonoBehaviour
{
    private static TransitionSkipper _instance;
    public static TransitionSkipper Instance { get => _instance; }

    public static readonly ShardPathSkip shardPathSkip = new();
    public static readonly WinShardEffectSkip winShardEffectSkip = new();
    public static readonly PostGameScreensSkip postGameScreensSkip = new();
    public static readonly PlayerSpawnFromShardAnimSkip playerSpawnFromShardAnimSkip = new();
    public static readonly PlayerSpawnFromShardPlayerAnimSkip playerSpawnFromShardPlayerAnimSkip = new();

    public static readonly ISkippableSkip[] skippableSkips = [
        shardPathSkip,
        winShardEffectSkip,
        postGameScreensSkip,
        playerSpawnFromShardAnimSkip,
        playerSpawnFromShardPlayerAnimSkip
    ];

    [SerializeField]
    public InputAction SkipInputAction;

    public float HeldFor = 0f;

    public void Awake()
    {
        _instance = this;

        foreach (var skippableSkip in skippableSkips)
        {
            skippableSkip.Initialize();
        }
    }

    public void Update()
    {
        if (SkipInputAction == null)
        {
            return;
        }

        UpdateInputEnablement();

        if (!SkipInputAction.IsPressed())
        {
            HeldFor = 0f;
            return;
        }

        HeldFor += Time.unscaledDeltaTime;

        ProcessSkippableSkips();
    }

    private void UpdateInputEnablement()
    {
        bool enableInput = !EscapeMenu.IsOpen;

        if (enableInput)
        {
            SkipInputAction.Enable();
        }
        else
        {
            SkipInputAction.Disable();
        }
    }

    private void ProcessSkippableSkips()
    {
        foreach (var skippableSkip in skippableSkips)
        {
            bool didSkip = false;

            if (skippableSkip is IOneTimeSkippableSkip oneTimeSkippableSkip)
            {
                if (!SkipInputAction.WasPerformedThisFrame())
                {
                    continue;
                }

                didSkip = oneTimeSkippableSkip.TrySkip();
            }
            else if (skippableSkip is IHoldSkippableSkip holdSkippableSkip)
            {
                if (HeldFor < holdSkippableSkip.Threshold)
                {
                    continue;
                }

                didSkip = holdSkippableSkip.TrySkip();
                if (didSkip)
                {
                    HeldFor = 0f;
                }
            }

            if (didSkip)
            {
                Debug.Log($"Triggered skip: {skippableSkip.GetType().Name}");
                break;
            }

        }
    }
}
