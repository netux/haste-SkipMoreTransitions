using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System.Reflection;
using UnityEngine;
using Zorro.ControllerSupport;

namespace HasteSkipMoreTransitionsMod.Skips;

public class ShardPathSkip : IOneTimeSkippableSkip
{
    public class State
    {
        public bool IsPlayingAnimation { get => queuedOnComplete.Count > 0; }

        private readonly List<Action> queuedOnComplete = [];
        private readonly List<Action> ranOnCompletes = [];

        public void EnqueueOnComplete(Action onComplete) =>
            queuedOnComplete.Add(onComplete);

        public void ForceInvokeOnComplete(Action onComplete)
        {
            onComplete.Invoke();
            queuedOnComplete.Remove(onComplete);
        }

        public bool HasAlreadyRanOnComplete(Action onComplete) =>
            ranOnCompletes.Contains(onComplete);

        public void Skip()
        {
            while (queuedOnComplete.Count > 0)
            {
                var onComplete = queuedOnComplete.Last();
                queuedOnComplete.RemoveAt(queuedOnComplete.Count - 1);
                onComplete.Invoke();
                ranOnCompletes.Add(onComplete);
            }
        }

        public void Reset()
        {
            ranOnCompletes.Clear();

            if (queuedOnComplete.Count > 0)
            {
                Debug.LogWarning("Called ShardPathSkip.Reset() with an unconsumed queue of onCompletes!");
            }
            queuedOnComplete.Clear();
        }
    };

    public static bool successfullyPatched = false;
    
    public static readonly State state = new();

    public override bool MultiplayerCompatible { get => false; }

    public override void Initialize() => Patch();

    public override bool TrySkip()
    {
        if (!successfullyPatched)
        {
            return false;
        }

        if (!state.IsPlayingAnimation)
        {
            return false;
        }

        state.Skip();
        return true;
    }

    void Patch()
    {
        var actionInvokeMethod = typeof(Action).GetMethod(nameof(Action.Invoke), BindingFlags.Public | BindingFlags.Instance);

        bool shardPathCreatePathBetween1HookSuccessful = false;
        bool shardPathCreatePathBetween2HookSuccessful = false;

        // Disable "jump to node" behavior when pressing space (default skip keybind) while the animation is running
        IL.LevelSelectCameraMouse.Update += (il) =>
        {
            var cursor = new ILCursor(il);

            /*
             * Original code:
             * if (HasteInputSystem.Interact.GetKeyDown())
             *   GoToPlayedNode();
             *   
             * Modified code:
             * if (HasteInputSystem.Interact.GetKeyDown() && !TransitionSkipper.shardPathSkip.isPlayingAnimation)
             *   GoToPlayedNode();
             */

            ILLabel? elseBranchLabel = null;

            if (!cursor.TryGotoNext(
                MoveType.After,
                i => i.MatchLdsfld(
                    typeof(HasteInputSystem)
                        .GetField(nameof(HasteInputSystem.Interact), BindingFlags.Public | BindingFlags.Static)
                ),
                i => i.MatchCallOrCallvirt(
                    typeof(InputKey)
                        .GetMethod(nameof(InputKey.GetKeyDown), BindingFlags.Public | BindingFlags.Instance)
                ),
                i => i.MatchBrfalse(out elseBranchLabel)
            ))
            {
                Debug.LogError($"{nameof(PlayerSpawnFromShardAnimSkip)}: Unable to find call to HasteInputSystem.Interact.GetKeyDown() in LevelSelectCameraMouse. As a result, when skipping the level select path, you may see a camera jump.");
                return;
            }

            cursor.EmitDelegate(() => !(state.IsPlayingAnimation || UI_TransitionHandler.IsTransitioning));
            cursor.Emit(OpCodes.Brfalse_S, elseBranchLabel);
        };

        // Store up onComplete's
        On.ShardPath.CreatePathBetween_List1_Vector3_Action += (original, shardPath, paths, start, onComplete) =>
        {
            state.EnqueueOnComplete(onComplete);

            original(shardPath, paths, start, onComplete);
        };
        On.ShardPath.CreatePathBetween_Vector3_Vector3_float_Action_float += (original, shardPath, start, end, pathSpeed, onComplete, delayComplete) =>
        {
            state.EnqueueOnComplete(onComplete);

            original(shardPath, start, end, pathSpeed, onComplete, delayComplete);
        };

        // Avoid duplicate onComplete calls (manually triggered via skip)
        IL.ShardPath.CreatePathBetween_List1_Vector3_Action += (il) =>
        {
            new ILHook(
                Utils.GetInlineCoroutineTarget(new ILCursor(il)).GetStateMachineTarget(),
                (il) =>
                {
                    shardPathCreatePathBetween1HookSuccessful = TryPatchCreatePathBetweenIL(il);
                    if (!shardPathCreatePathBetween1HookSuccessful)
                    {
                        Debug.LogError($"{nameof(ShardPathSkip)}: Unable to find call to Action.invoke() in ShardPath.CreatePathBetween(List1, Vector3, Action). This skip will be disabled as a result.");
                    }
                }
            );
        };
        IL.ShardPath.CreatePathBetween_Vector3_Vector3_float_Action_float += (il) =>
        {
            new ILHook(
                Utils.GetInlineCoroutineTarget(new ILCursor(il)).GetStateMachineTarget(),
                (il) =>
                {
                    shardPathCreatePathBetween2HookSuccessful = TryPatchCreatePathBetweenIL(il);
                    if (!shardPathCreatePathBetween2HookSuccessful)
                    {
                        Debug.LogError($"{nameof(ShardPathSkip)}: Unable to find call to Action.invoke() in ShardPath.CreatePathBetween(Vector3, Vector3, float, Action, float). This skip will be disabled as a result.");
                    }
                }
            );
        };

        bool TryPatchCreatePathBetweenIL(ILContext il)
        {
            var cursor = new ILCursor(il);

            if (!cursor.TryGotoNext(i => i.MatchCallOrCallvirt(actionInvokeMethod)))
            {
                return false;
            }

            var actionInvokeCallIncomingLabels = cursor.IncomingLabels.ToArray();

            cursor.Remove();

            var delegateLabel = cursor.MarkLabel();

            cursor.EmitDelegate((Action onComplete) =>
            {
                if (state.HasAlreadyRanOnComplete(onComplete))
                {
                    return;
                }

                state.ForceInvokeOnComplete(onComplete);
            });

            foreach (var originalIncomingLabel in actionInvokeCallIncomingLabels)
            {
                originalIncomingLabel.Target = delegateLabel.Target;
            }

            //Utils.LogInstructions(il.Body.Instructions);

            return true;
        }

        successfullyPatched = shardPathCreatePathBetween1HookSuccessful && shardPathCreatePathBetween2HookSuccessful;
    }
}
