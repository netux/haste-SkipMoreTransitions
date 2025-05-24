using MonoMod.Cil;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HasteSkipMoreTransitionsMod.Skips;

public class PlayerSpawnFromShardPlayerAnimSkip : IHoldSkippableSkip
{
    public class State
    {
        public bool IsAnimationRunning = false;
        public bool IsSkipping = false;

        public void MarkAnimationRunning() =>
            IsAnimationRunning = true;

        public void Skip() =>
            IsSkipping = true;

        public void Reset()
        {
            IsAnimationRunning = false;
            IsSkipping = false;
        }
    }

    public static bool successfullyPatched = false;
    
    public static readonly State state = new();

    public static GameObject? skipUIGameObject;

    public override float Threshold { get => 0.25f; }

    public override void Initialize()
    {
        Patch();

        SceneManager.activeSceneChanged += (_oldScene, newScene) =>
        {
            if (!newScene.name.ToLower().Contains("hub")) // "FullHub", but also "FullHub Cutscene" and "DemoHub"
            {
                return;
            }

            state.Reset();

            TransitionSkipper.Instance.StartCoroutine(CreateSkipUIGameObjectNextFrame());
            IEnumerator CreateSkipUIGameObjectNextFrame()
            {
                yield return null;

                skipUIGameObject = Utils.InstantiateHoldToSkipUIGameObject(this.GetType(), Threshold);
            }
        };
    }

    public override bool TrySkip()
    {
        if (!successfullyPatched)
        {
            return false;
        }
        
        if (!state.IsAnimationRunning)
        {
            return false;
        }

        state.Skip();
        return true;
    }

    void Patch()
    {
        IL.TimeHandler.Update += (il) =>
        {
            var cursor = new ILCursor(il);

            if (!cursor.TryGotoNext(
                i => i.MatchCallOrCallvirt(typeof(Time).GetMethod($"set_{nameof(Time.timeScale)}"))
            ))
            {
                Debug.LogError($"{nameof(PlayerSpawnFromShardAnimSkip)}: Unable to find set call to Time.timeScale in TimeHandler. This skip will be disabled as a result.");
                return;
            }

            cursor.EmitDelegate((float timeScale) =>
            {
                if (
                    timeScale != 0f && // i.e. escape menu opened
                    state.IsAnimationRunning && state.IsSkipping
                )
                {
                    timeScale = 50f;
                }

                return timeScale;
            });

            successfullyPatched = true;
        };

        if (!successfullyPatched)
        {
            return;
        }

        static IEnumerator hook_PlayerSpawnFromShardPlayerAnim(On.GM_Hub.orig_PlayerSpawnFromShardPlayerAnim original, GM_Hub gmHub, PlayerCharacter player, float animTime, Transform spawnPoint)
        {
            state.MarkAnimationRunning();

            var enumerator = original(gmHub, player, animTime, spawnPoint);
            while (enumerator.MoveNext())
            {
#pragma warning disable IDE0031 // Use null propagation. Unity makes it hard to do this
                if (skipUIGameObject != null)
#pragma warning restore IDE0031 // Use null propagation. Unity makes it hard to do this
                {
                    skipUIGameObject.SetActive(true);
                }
                yield return enumerator.Current;
            }

#pragma warning disable IDE0031 // Use null propagation. Unity makes it hard to do this
            if (skipUIGameObject != null)
#pragma warning restore IDE0031 // Use null propagation. Unity makes it hard to do this
            {
                skipUIGameObject.SetActive(false);
            }
            state.Reset();
        }
        On.GM_Hub.PlayerSpawnFromShardPlayerAnim += hook_PlayerSpawnFromShardPlayerAnim;
    }
}