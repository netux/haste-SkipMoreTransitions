using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System.Reflection;
using UnityEngine;

namespace HasteSkipMoreTransitionsMod.Skips;

public class StartRun_UISkip : IOneTimeSkippableSkip
{
    public class State
    {
        public WorldShard? worldShardRunningEnterAnimation = null;
        public bool wasSkipped = false;

        public void MarkWorldShardRunningEnterAnimation(WorldShard worldShard)
        {
            worldShardRunningEnterAnimation = worldShard;
        }

        public void MarkSkipped()
        {
            wasSkipped = true;
        }

        public void Reset()
        {
            worldShardRunningEnterAnimation = null;
            wasSkipped = false;
        }
    };

    public static bool successfullyPatched = false;

    public static readonly State state = new();

    public override void Initialize() => Patch();

    public override bool TrySkip()
    {
        if (!successfullyPatched)
        {
            return false;
        }

        if (state.worldShardRunningEnterAnimation == null)
        {
            return false;
        }

        if (!(StartRun_UI.Instance && StartRun_UI.Instance.isShowing))
        {
            return false;
        }

        state.MarkSkipped();

        StartRun_UI.Instance.Close();

        if (state.worldShardRunningEnterAnimation.enterCor != null)
        {
            state.worldShardRunningEnterAnimation.StopCoroutine(state.worldShardRunningEnterAnimation.enterCor);
        }

        state.worldShardRunningEnterAnimation.PlayLevel();

        return true;
    }

    void Patch()
    {
        /*
         * Original code goes more or less like this:
         * 
         * class WorldShard {
         *   // ...
         *   
         *   internal Enter(...) {
         *     // ...
         *     
         *     StartCoroutine(HandleEnterAnimation);
         *     
         *     IEnumerator HandleEnterAnimation() {
         *       // ...
         *       
         *       Singleton<StartRun_UI>.Instance.Show(...);
         *       
         *       yield return ...;
         *       
         *       if (Singleton<StartRun_UI>.Instance.isShowing)
         *       {
         *          this.PlayLevel(...);
         *       }
         *       
         *       // ...
         *     }
         *   }
         * }
         */

        var worldShardPlayLevelMethod = typeof(WorldShard).GetMethod(nameof(WorldShard.PlayLevel), BindingFlags.NonPublic | BindingFlags.Instance, null, [], []);
        var worldShardEnterMethod = typeof(WorldShard).GetMethod(nameof(WorldShard.Enter), BindingFlags.NonPublic | BindingFlags.Instance);
        var handleEnterAnimationStateMachineTargetMethod = Utils.GetInlineCoroutineTarget(new ILCursor(Utils.GetILContextFromMethod(worldShardEnterMethod))).GetStateMachineTarget();

        if (handleEnterAnimationStateMachineTargetMethod == null)
        {
            Debug.LogError($"{nameof(StartRun_UISkip)}: Unable to find WorldShard.PlayLevel() call inside inline coroutine method of WorldShard.Enter(). This skip will be disabled as a result.");
            return;
        }

        On.WorldShard.Enter += (original, worldShard, player, point, normal, hubOrbPiece) =>
        {
            state.MarkWorldShardRunningEnterAnimation(worldShard);

            original(worldShard, player, point, normal, hubOrbPiece);
        };

        new ILHook(
            handleEnterAnimationStateMachineTargetMethod,
            (il) =>
            {
                var cursor = new ILCursor(il);

                Func<Instruction, bool>[] preds = [
                    i => i.MatchCallOrCallvirt(worldShardPlayLevelMethod)
                ];
                if (!cursor.TryGotoNext(preds))
                {
                    Debug.LogError($"{nameof(StartRun_UISkip)}: Unable to find WorldShard.PlayLevel() call inside inline coroutine method of WorldShard.Enter(). This skip will be disabled as a result.");
                    return;
                }
                cursor.RemoveRange(preds.Length);

                cursor.EmitDelegate((WorldShard worldShard) =>
                {
                    if (state.wasSkipped)
                    {
                        state.Reset();
                        return;
                    }

                    worldShard.PlayLevel();
                });

                Utils.LogInstructions(il.Body.Instructions);
                
                successfullyPatched = true;
            }
        );
    }
}
