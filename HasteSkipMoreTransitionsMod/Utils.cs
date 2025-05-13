using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace HasteSkipMoreTransitionsMod;

internal class Utils
{
    #region Get inline Coroutine Target
    private static readonly MethodInfo monoBehaviourStartCoroutineMethod = typeof(MonoBehaviour).GetMethod(
        nameof(MonoBehaviour.StartCoroutine),
        BindingFlags.Public | BindingFlags.Instance,
        null,
        [typeof(System.Collections.IEnumerator)],
        []
    );

    public static MethodInfo GetInlineCoroutineTarget(MethodInfo fromMethod)
    {
        var cursor = new ILCursor(GetILContextFromMethod(fromMethod));
        return GetInlineCoroutineTarget(cursor);
    }

    public static MethodInfo GetInlineCoroutineTarget(ILCursor ilCursor)
    {
        ilCursor.GotoNext(
            MoveType.Before,
            i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt,
            i => i.MatchCall(monoBehaviourStartCoroutineMethod)
        );

        var iEnumeratorMethod = (MethodInfo)((Mono.Cecil.MethodReference)ilCursor.Next.Operand).ResolveReflection();
        return iEnumeratorMethod;
    }
    #endregion Get inline Coroutine Target

    public static InputAction CloneInputAction(InputAction inputAction, string name, Func<int, Guid> generateGuid, InputActionMap inputActionMap)
    {
        var clonedInputAction = inputActionMap.AddAction(
            name: name,
            type: inputAction.type,
            binding: null,
            interactions: inputAction.interactions,
            processors: inputAction.processors
        );
        clonedInputAction.bindingMask = inputAction.bindingMask;
        clonedInputAction.expectedControlType = inputAction.expectedControlType;

        clonedInputAction.Enable();

        for (int bindingIndex = 0; bindingIndex < inputAction.bindings.Count; bindingIndex++)
        {
            var originalBinding = inputAction.bindings[bindingIndex];
            var clonedBinding = new InputBinding()
            {
                id = generateGuid(bindingIndex),
                action = clonedInputAction.name,
                name = originalBinding.name,
                groups = originalBinding.groups,
                path = originalBinding.path,
                processors = originalBinding.processors,
                interactions = originalBinding.interactions
            };

            clonedInputAction.AddBinding(clonedBinding);
        }

        return clonedInputAction;
    }

    public static void AddLocalizedString(string table, string key, string localized)
    {
        LocalizationSettings.StringDatabase
            .GetTable(table)
            .AddEntry(key, localized);
    }

    public static GameObject InstantiateHoldToSkipUIGameObject(Type skippableSkipType, float holdLength)
    {
        // Hardcoded values here are based off the similar UI in character interactions

        var parentTransform = GameObject.Find("GAME").transform;

        var canvasGameObject = new GameObject($"UI_{skippableSkipType.Name}", [typeof(RectTransform)]);

        var canvasTransform = (RectTransform)canvasGameObject.transform;
        canvasTransform.SetParent(parentTransform, worldPositionStays: false);

        var canvas = canvasGameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var canvasScaler = canvasGameObject.AddComponent<CanvasScaler>();
        canvasScaler.referenceResolution = new Vector2(1920, 1080);
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        var containerGameObject = new GameObject("Skip Visual", [typeof(RectTransform)]);
        containerGameObject.SetActive(false);

        var containerTransform = (RectTransform)containerGameObject.transform;
        containerTransform.SetParent(canvasGameObject.transform, worldPositionStays: false);
        containerTransform.anchorMin = new Vector2(1, 0);
        containerTransform.anchorMax = new Vector2(1, 0);

        var progressGameObject = new GameObject("Progress Image", [typeof(RectTransform)]);

        var progressImageTransform = (RectTransform)progressGameObject.transform;
        progressImageTransform.SetParent(containerGameObject.transform, worldPositionStays: true);
        progressImageTransform.anchorMin = Vector2.zero;
        progressImageTransform.anchorMax = Vector2.one;
        progressImageTransform.localScale = Vector2.one * 0.75f; // x1.25 in character interactions, but that's too big for the layout we have here

        var progressProceduralImage = progressGameObject.AddComponent<UnityEngine.UI.ProceduralImage.ProceduralImage>();
        progressProceduralImage.type = Image.Type.Filled;
        progressProceduralImage.fillOrigin = 2;
        progressProceduralImage.BorderWidth = 4.63f * 1.25f / 0.75f; // value from the reference UI in character interactions, scaled down to the GameObject's scale
        progressProceduralImage.ModifierType = typeof(RoundModifier);

        var useTextGameObject = new GameObject("Use Text", [typeof(RectTransform)]);
        useTextGameObject.transform.SetParent(containerGameObject.transform, worldPositionStays: true);

        var useTextMesh = useTextGameObject.AddComponent<TMPro.TextMeshProUGUI>();
        useTextMesh.font = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>()
            .First(f => f.name == "AkzidenzGroteskPro-Bold SDF");
        useTextMesh.richText = false;
        useTextMesh.text = "Skip";

        var useTextContentSizeFitter = useTextGameObject.AddComponent<ContentSizeFitter>();
        useTextContentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        useTextContentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Set default visibility state.
        // We had to leave it active (Unity GameObject's default state) until now so the layout could be calculated correctly.
        //
        // However, HoldProgressUI has an MonoBehaviour OnEnabled() callback which crashes when its `icon` is null.
        // Since we can't instantiate the HoldProgressUI component ourselves, correctly setting its `icon` before adding it to a GameObject;
        // if the GameObject we enabled, then OnEnabled() is immediately called and the component crashes.
        //
        // This is why we disable the GameObject here, and why we add the HoldProgressUI to the image later.

        var holdProgressUI = progressGameObject.AddComponent<HoldProgressUI>();
        holdProgressUI.icon = progressProceduralImage;
        holdProgressUI.ActionReference = InputActionReference.Create(SkipMoreTransitions.SkipInputAction);
        holdProgressUI.HoldLength = holdLength;
        // Usually we want to stick to the game's defaults.
        // But for ISkippableSkips with very low thresholds, the progress UI might not even be visible
        holdProgressUI.HoldLengthStartShowing = Math.Min(holdLength * 0.2f, holdProgressUI.HoldLengthStartShowing);

        TransitionSkipper.Instance // yoink
            .StartCoroutine(WhyIsThisNeededThough());

        return containerGameObject;

        IEnumerator WhyIsThisNeededThough()
        {
            yield return null;

            containerTransform.position += new Vector3(1760, 86.5f);
        }
    }

    #region IL Helpers
    public static ILContext GetILContextFromMethod(MethodInfo method)
    {
        var fromMethodDefinition = new DynamicMethodDefinition(method).Definition;
        return new ILContext(fromMethodDefinition);
    }

    public static void LogInstructions(IEnumerable<Instruction> instrs)
    {
        var instrsArray = instrs.ToArray();

        for (int instrIndex = 0; instrIndex < instrsArray.Length; instrIndex++)
        {
            var instr = instrsArray[instrIndex];
            Debug.Log($"\t{InstructionToString((instrIndex, instr))}");
        }

        string InstructionToString((int instrIndex, Instruction instr) t) => $"{t.instrIndex} {t.instr.OpCode} {InstructionOperandToString(t.instr.Operand)}";

        string InstructionOperandToString(object operand)
        {
            if (operand is ILLabel label)
            {
                int labelTargetIndex = Array.IndexOf(instrsArray, label.Target);
                return $"(label→ {InstructionToString((labelTargetIndex, label.Target))})";
            }
            else if (operand is VariableDefinition localVariable)
            {
                return $"(local variable: {localVariable.Index} {localVariable.VariableType}{(localVariable.IsPinned ? " (pinned)" : "")})";
            }
            else if (operand is string)
            {
                return $"\"{operand}\"";
            }
            else
            {
                return operand?.ToString() ?? "null";
            }
        }
    }
    #endregion IL Helpers
}
