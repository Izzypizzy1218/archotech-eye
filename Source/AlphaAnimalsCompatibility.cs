using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

internal static class AlphaAnimalsCompatibility
{
    private static Type stalkingHediffType;

    internal static MethodInfo GraphicForMethod { get; private set; }
    internal static bool Installed => stalkingHediffType != null;
    internal static bool Enabled => Installed && ArchotechEyeInvisibilityDetectionMod.Settings.DetectAlphaStalking;

    internal static void TryInstall(Harmony harmony)
    {
        if (Installed)
        {
            return;
        }

        // These are optional integration endpoints, not race or DefName detection rules.
        Type stateType = AccessTools.TypeByName("AlphaBehavioursAndEvents.Hediff_Stalking");
        Type nodeType = AccessTools.TypeByName("AlphaBehavioursAndEvents.PawnRenderNode_ArcticLion");
        if (stateType == null || nodeType == null)
        {
            return;
        }
        if (!typeof(HediffWithComps).IsAssignableFrom(stateType) ||
            !typeof(PawnRenderNode_AnimalPart).IsAssignableFrom(nodeType) || stateType.Assembly != nodeType.Assembly)
        {
            return;
        }

        MethodInfo target = AccessTools.DeclaredMethod(nodeType, "GraphicFor", new[] { typeof(Pawn) });
        if (target == null || target.ReturnType != typeof(Graphic) || target.IsStatic)
        {
            return;
        }
        MethodInfo transpiler = AccessTools.Method(typeof(AlphaAnimalsCompatibility), nameof(GraphicTranspiler));
        try
        {
            harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
            GraphicForMethod = target;
            stalkingHediffType = stateType;
        }
        catch (Exception exception)
        {
            try
            {
                harmony.Unpatch(target, transpiler);
            }
            catch (Exception cleanupException)
            {
                Log.Warning("[" + ArchotechEyeInvisibilityDetectionMod.HarmonyId +
                    "] Optional Alpha Animals patch cleanup failed: " + cleanupException.GetType().Name);
            }
            Log.Warning("[" + ArchotechEyeInvisibilityDetectionMod.HarmonyId +
                "] Optional Alpha Animals stalking integration was disabled because its graphics API did not match: " +
                exception.GetType().Name);
        }
    }

    internal static bool HasStalkingHediff(Pawn pawn)
    {
        List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
        if (!Installed || hediffs == null)
        {
            return false;
        }
        for (int i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i] != null && stalkingHediffType.IsInstanceOfType(hediffs[i]))
            {
                return true;
            }
        }
        return false;
    }

    internal static IEnumerable<CodeInstruction> GraphicTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
        MethodInfo lookup = AccessTools.Method(typeof(HediffSet), nameof(HediffSet.GetFirstHediffOfDef),
            new[] { typeof(HediffDef), typeof(bool) });
        if (lookup == null)
        {
            throw new InvalidOperationException("The game's hediff lookup API is unavailable.");
        }
        int matches = 0;
        foreach (CodeInstruction code in codes)
        {
            if (code.Calls(lookup))
            {
                matches++;
            }
        }
        if (matches != 1)
        {
            throw new InvalidOperationException("Expected one stalking-state lookup in the Alpha Animals render node.");
        }

        MethodInfo select = AccessTools.Method(typeof(AlphaAnimalsCompatibility), nameof(SelectStalkingGraphic));
        foreach (CodeInstruction code in codes)
        {
            yield return code;
            if (code.Calls(lookup))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(OpCodes.Call, select);
            }
        }
    }

    internal static Hediff SelectStalkingGraphic(Hediff original, Pawn pawn)
    {
        // Change only this graphics branch; the actual hediff and alternate-graphics branches remain intact.
        return ArchotechEyeVisibilityUtility.IsAlphaStalkingDetectedForPlayer(pawn) ? null : original;
    }
}
