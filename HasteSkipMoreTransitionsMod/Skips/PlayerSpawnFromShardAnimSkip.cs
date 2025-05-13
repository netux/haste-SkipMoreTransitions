using MonoMod.Cil;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HasteSkipMoreTransitionsMod.Skips;

public class PlayerSpawnFromShardAnimSkip : IHoldSkippableSkip
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

    public static readonly State state = new();

    public static GameObject? skipUIGameObject;

    public override float Threshold { get => 1f; }

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
        if (!state.IsAnimationRunning || state.IsSkipping)
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

            cursor.GotoNext(
                i => i.MatchCallOrCallvirt(typeof(Time).GetMethod($"set_{nameof(Time.timeScale)}"))
            );

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
        };

        IEnumerator hook_PlayerSpawnShardAnim(On.GM_Hub.orig_PlayerSpawnShardAnim original, GM_Hub gmHub, WorldShard currentShard)
        {
            state.MarkAnimationRunning();

            var enumerator = original(gmHub, currentShard);
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
        On.GM_Hub.PlayerSpawnShardAnim += hook_PlayerSpawnShardAnim;
    }
}