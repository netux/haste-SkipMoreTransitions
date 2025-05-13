using Mono.Cecil.Cil;
using MonoMod.Cil;
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

    public static readonly State state = new();

    public override void Initialize() => Patch();

    public override bool TrySkip()
    {
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
        IL.RunHandler.CompleteRun += (il) =>
        {
            var cursor = new ILCursor(il);

            var runHandlerTransitionToPostGameMethod = typeof(RunHandler)
                .GetMethod(nameof(RunHandler.TransitionToPostGame), BindingFlags.Public | BindingFlags.Static);

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
        };
    }
}
