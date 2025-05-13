using Landfall.Modding;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using UnityEngine.InputSystem;
using Zorro.ControllerSupport;

namespace HasteSkipMoreTransitionsMod;

[LandfallPlugin]
public class SkipMoreTransitions
{
    public static InputAction? SkipInputAction;

    static SkipMoreTransitions()
    {
        RegisterAddComponentHooks();
        RegisterInitializeInputActionHooks();
        RegisterReplaceOriginalSkipButtonHooks();
    }

    private static void RegisterAddComponentHooks()
    {
        On.GameHandler.Initialize += (original, gameHandler) =>
        {
            original(gameHandler);

            var transitionSkipper = gameHandler.gameObject.AddComponent<TransitionSkipper>();
            transitionSkipper.SkipInputAction = SkipInputAction!;
        };
    }

    private static void RegisterInitializeInputActionHooks()
    {
        On.HasteSettingsHandler.ctor += (original, settingsHandler) =>
        {
            original(settingsHandler);

            // "CaNnOt AdD, rEmOvE, oR cHaNgE eLeMeNtS oF iNpUtAcTiOnAsSeT hAsTeInPuTaCtIoNs (UnItYeNgInE.iNpUtSyStEm.InPuTaCtIoNaSsEt) WhIlE oNe Or MoRe Of ItS aCtIoNs ArE eNaBlEd"
            InputSystem.actions.Disable();

            // Make a separate InputActionMap for the mod, since the game sometimes disables
            // the Default InputActionMap during cutscenes (primarily for us, PlayerSpawnFromShardAnim),
            // making our button useless.
            var modInputActionMap = new InputActionMap(nameof(SkipMoreTransitions));
            InputSystem.actions.AddActionMap(modInputActionMap);

            SkipInputAction = Utils.CloneInputAction(
                inputAction: HasteInputSystem.Interact.Action,
                name: $"{nameof(SkipMoreTransitions)}--SkipTransitions",
                generateGuid: (bindingIndex) => new Guid($"56190744-4517-1045-0000-{bindingIndex:X12}"),
                inputActionMap: modInputActionMap
            );

            InputSystem.actions.Enable();

            Utils.AddLocalizedString("Settings", SkipInputAction.name, "Skip Transitions");

            settingsHandler.AddSetting(new InputRebindSetting(SkipInputAction));
        };
    }

    private static void RegisterReplaceOriginalSkipButtonHooks()
    {
        var hasteInputSystemInteractField = typeof(HasteInputSystem)
            .GetField(
                nameof(HasteInputSystem.Interact),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
            );
        var inputKeyGetKeyDown = typeof(InputKey)
            .GetMethod(
                nameof(InputKey.GetKeyDown),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            );
        var inputKeyGetKey = typeof(InputKey)
            .GetMethod(
                nameof(InputKey.GetKey),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            );
        void FindAndReplaceInteractGetKeyState(ILContext il)
        {
            var cursor = new ILCursor(il);

            Func<Mono.Cecil.Cil.Instruction, bool>[] preds = [
                i => i.MatchLdsfld(hasteInputSystemInteractField),
                i => i.MatchCallOrCallvirt(inputKeyGetKeyDown) || i.MatchCallOrCallvirt(inputKeyGetKey)
            ];

            while (cursor.TryGotoNext(preds))
            {
                bool isGetKey = (
                    (Mono.Cecil.MethodReference)cursor
                        .Instrs[cursor.Index + preds.Length - 1]
                        .Operand
                ).Name == inputKeyGetKey.Name;

                var originalIncomingLabels = cursor.IncomingLabels.ToArray();

                cursor.RemoveRange(preds.Length);

                var newLabel = cursor.MarkLabel();

                cursor.EmitDelegate<Func<bool>>(
                    isGetKey
                        ? () => SkipInputAction?.IsPressed() ?? false
                        : () => SkipInputAction?.WasPerformedThisFrame() ?? false
                );

                foreach (var originalLabel in originalIncomingLabels)
                {
                    originalLabel.Target = newLabel.Target;
                }
            }

            //Utils.LogInstructions(cursor.Instrs);
        }

        IL.MainMenu.Update += FindAndReplaceInteractGetKeyState;
        IL.PostLevelScreenHandler.Update += FindAndReplaceInteractGetKeyState;
        IL.EncounterFinalRender.Update += FindAndReplaceInteractGetKeyState;
    }
}
