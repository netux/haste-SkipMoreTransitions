using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System.Reflection;
using UnityEngine;

namespace HasteSkipMoreTransitionsMod.Skips;

public class WinShardEffectSkip : IOneTimeSkippableSkip
{
    public class State
    {
        public bool alreadyTransitionedToPostGame = false;

        public void MarkSkipped()
        {
            alreadyTransitionedToPostGame = true;
        }

        public void Reset()
        {
            alreadyTransitionedToPostGame = false;
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

        if (!(WinShardEffect.instance && WinShardEffect.instance.playing))
        {
            return false;
        }

        var animator = WinShardEffect.instance.GetComponent<Animator>();
        animator.StopPlayback();

        WinShardEffect.instance.playing = false;
        RunHandler.TransitionToPostGame();

        state.MarkSkipped();

        return true;
    }

    void Patch()
    {
        var monoFunctionsDelayCallMethod = typeof(MonoFunctions).GetMethod(nameof(MonoFunctions.DelayCall), BindingFlags.Public | BindingFlags.Static);
        var runHandlerCompleteRunMethod = typeof(RunHandler).GetMethod(nameof(RunHandler.CompleteRun), BindingFlags.NonPublic | BindingFlags.Static);
        var runHandlerTransitionToPostGameMethod = typeof(RunHandler)
            .GetMethod(nameof(RunHandler.TransitionToPostGame), BindingFlags.Public | BindingFlags.Static);

        var delayedTransitionTargetMethod = FindDelayedTransitionTargetInlineMethod(Utils.GetILContextFromMethod(runHandlerCompleteRunMethod));
        if (delayedTransitionTargetMethod == null)
            {
            Debug.LogError($"{nameof(WinShardEffectSkip)}: Unable to find MonoFunctions.DelayCall() inline target transition method. This skip will be disabled as a result.");
            successfullyPatched = false;
            return;
        }

        new ILHook(delayedTransitionTargetMethod, (il) =>
        {
            var cursor = new ILCursor(il);

            Func<Instruction, bool>[] preds = [
                i => i.MatchLdnull(),
                i => i.MatchLdftn(runHandlerTransitionToPostGameMethod),
                i => i.MatchNewobj(typeof(Action))
            ];
            cursor.GotoNext(preds);
            cursor.RemoveRange(preds.Length);

            cursor.EmitDelegate(() =>
            {
                return () =>
                {
                    if (state.alreadyTransitionedToPostGame)
                    {
                        state.Reset();
                        return;
                    }

                    runHandlerTransitionToPostGameMethod.Invoke(null, []);
                };
            });

            //Utils.LogInstructions(il.Body.Instructions);

            successfullyPatched = true;
        });

        MethodBase? FindDelayedTransitionTargetInlineMethod(ILContext il)
        {
            var cursor = new ILCursor(il);

            Mono.Cecil.MethodReference? transitionInlineClassCecilMethodReference = null;
            if (!cursor.TryGotoNext(
                MoveType.Before,
                i => i.MatchLdftn(out transitionInlineClassCecilMethodReference),
                i => i.MatchNewobj(typeof(Action)),
                i => i.MatchLdarg(out _),
                i => i.MatchCallOrCallvirt(monoFunctionsDelayCallMethod)
            ))
            {
                return null;
            }

            return transitionInlineClassCecilMethodReference.ResolveReflection();
        }
    }
}
