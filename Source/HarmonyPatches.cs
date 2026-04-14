using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ArchotechEye_InvisibilityDetection;

[HarmonyPatch]
internal static class HarmonyPatches
{
    private static readonly FieldInfo PawnRendererPawnField = AccessTools.Field(typeof(PawnRenderer), "pawn");
    private static readonly MethodInfo GenUiThingsUnderMouseMethod = AccessTools.Method(typeof(GenUI), "ThingsUnderMouse");

    [HarmonyPatch(typeof(Pawn), "Tick")]
    [HarmonyPrefix]
    private static void PawnTickPrefix(Pawn __instance)
    {
        VisibilityContext.Push(__instance);
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    [HarmonyFinalizer]
    private static Exception PawnTickFinalizer(Exception __exception)
    {
        VisibilityContext.Pop();
        return __exception;
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTargetFrom))]
    [HarmonyPrefix]
    private static void VerbCanHitTargetFromPrefix(Verb __instance)
    {
        VisibilityContext.Push(__instance?.CasterPawn);
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTargetFrom))]
    [HarmonyFinalizer]
    private static Exception VerbCanHitTargetFromFinalizer(Exception __exception)
    {
        VisibilityContext.Pop();
        return __exception;
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTarget))]
    [HarmonyPrefix]
    private static void VerbCanHitTargetPrefix(Verb __instance)
    {
        VisibilityContext.Push(__instance?.CasterPawn);
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTarget))]
    [HarmonyFinalizer]
    private static Exception VerbCanHitTargetFinalizer(Exception __exception)
    {
        VisibilityContext.Pop();
        return __exception;
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.ValidateTarget))]
    [HarmonyPrefix]
    private static void VerbValidateTargetPrefix(Verb __instance)
    {
        VisibilityContext.Push(__instance?.CasterPawn);
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.ValidateTarget))]
    [HarmonyFinalizer]
    private static Exception VerbValidateTargetFinalizer(Exception __exception)
    {
        VisibilityContext.Pop();
        return __exception;
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.OrderForceTarget))]
    [HarmonyPrefix]
    private static void VerbOrderForceTargetPrefix(Verb __instance)
    {
        VisibilityContext.Push(__instance?.CasterPawn);
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.OrderForceTarget))]
    [HarmonyFinalizer]
    private static Exception VerbOrderForceTargetFinalizer(Exception __exception)
    {
        VisibilityContext.Pop();
        return __exception;
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    [HarmonyPrefix]
    private static void VerbTryStartCastOnPrefix(Verb __instance)
    {
        VisibilityContext.Push(__instance?.CasterPawn);
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    [HarmonyFinalizer]
    private static Exception VerbTryStartCastOnFinalizer(Exception __exception)
    {
        VisibilityContext.Pop();
        return __exception;
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    [HarmonyPrefix]
    private static void VerbTryStartCastOnWithDestinationPrefix(Verb __instance)
    {
        VisibilityContext.Push(__instance?.CasterPawn);
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    [HarmonyFinalizer]
    private static Exception VerbTryStartCastOnWithDestinationFinalizer(Exception __exception)
    {
        VisibilityContext.Pop();
        return __exception;
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ThreatDisabled))]
    [HarmonyPostfix]
    private static void PawnThreatDisabledPostfix(Pawn __instance, IAttackTargetSearcher disabledFor, ref bool __result)
    {
        if (!__result || __instance == null || !(disabledFor?.Thing is Pawn observer))
        {
            return;
        }

        if (!ArchotechEyeVisibilityUtility.IsVanillaInvisible(__instance))
        {
            return;
        }

        if (ArchotechEyeVisibilityUtility.IsDetectedBy(observer, __instance))
        {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsPsychologicallyInvisible))]
    [HarmonyPostfix]
    private static void IsPsychologicallyInvisiblePostfix(Pawn pawn, ref bool __result)
    {
        if (!__result || pawn == null)
        {
            return;
        }

        Pawn observer = VisibilityContext.CurrentSearcher;
        if (observer == null || observer == pawn)
        {
            return;
        }

        if (ArchotechEyeVisibilityUtility.IsDetectedBy(observer, pawn))
        {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsHiddenFromPlayer))]
    [HarmonyPostfix]
    private static void IsHiddenFromPlayerPostfix(Pawn pawn, ref bool __result)
    {
        RevealDetectedPawnForPlayer(pawn, ref __result);
    }

    [HarmonyPatch(typeof(InvisibilityUtility), nameof(InvisibilityUtility.GetAlpha))]
    [HarmonyPostfix]
    private static void GetAlphaPostfix(Pawn pawn, ref float __result)
    {
        if (pawn == null)
        {
            return;
        }

        if (!ArchotechEyeVisibilityUtility.IsVanillaInvisible(pawn))
        {
            return;
        }

        if (ArchotechEyeVisibilityUtility.IsDetectedForPlayer(pawn))
        {
            __result = ArchotechEyeVisibilityUtility.RevealedAlpha;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "OverrideMaterialIfNeeded")]
    [HarmonyPostfix]
    private static void OverrideMaterialIfNeededPostfix(PawnRenderer __instance, Material original, PawnRenderFlags flags, ref Material __result)
    {
        if (__instance == null || original == null || flags.FlagSet(PawnRenderFlags.Cache) || flags.FlagSet(PawnRenderFlags.Portrait))
        {
            return;
        }

        Pawn pawn = PawnRendererPawnField?.GetValue(__instance) as Pawn;
        if (pawn == null)
        {
            return;
        }

        if (!ArchotechEyeVisibilityUtility.IsVanillaInvisible(pawn))
        {
            return;
        }

        if (ArchotechEyeVisibilityUtility.IsDetectedForPlayer(pawn))
        {
            __result = original;
        }
    }

    [HarmonyPatch(typeof(ThingSelectionUtility), nameof(ThingSelectionUtility.SelectableByMapClick))]
    [HarmonyPostfix]
    private static void SelectableByMapClickPostfix(Thing t, ref bool __result)
    {
        if (__result || t is not Pawn pawn)
        {
            return;
        }

        if (!pawn.def.selectable)
        {
            return;
        }

        if (pawn.SpawnedParentOrMe == null)
        {
            return;
        }

        if (ArchotechEyeVisibilityUtility.IsDetectedForPlayer(pawn))
        {
            __result = true;
        }
    }

    [HarmonyPatch(typeof(GenUI), nameof(GenUI.TargetsAt))]
    [HarmonyPostfix]
    private static void GenUiTargetsAtPostfix(Vector3 clickPos, TargetingParameters clickParams, bool thingsOnly, ITargetingSource source, ref IEnumerable<LocalTargetInfo> __result)
    {
        if (clickParams == null)
        {
            return;
        }

        Thing caster = source?.Caster;
        if (caster is not Pawn observer)
        {
            return;
        }

        if (!ArchotechEyeVisibilityUtility.HasArchotechEye(observer))
        {
            return;
        }

        List<LocalTargetInfo> targets = (__result ?? Enumerable.Empty<LocalTargetInfo>()).ToList();
        List<Thing> thingsUnderMouse = GetThingsUnderMouse(clickPos, clickParams, source);
        if (thingsUnderMouse.Count == 0)
        {
            __result = targets;
            return;
        }

        foreach (Thing thing in thingsUnderMouse)
        {
            if (thing is not Pawn pawn)
            {
                continue;
            }

            if (!ArchotechEyeVisibilityUtility.IsDetectedBy(observer, pawn))
            {
                continue;
            }

            bool alreadyPresent = false;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Thing == pawn)
                {
                    alreadyPresent = true;
                    break;
                }
            }

            if (!alreadyPresent)
            {
                targets.Insert(0, pawn);
            }
        }

        __result = targets;
    }

    private static void RevealDetectedPawnForPlayer(Pawn pawn, ref bool hidden)
    {
        if (!hidden || pawn == null)
        {
            return;
        }

        if (!ArchotechEyeVisibilityUtility.IsVanillaInvisible(pawn))
        {
            return;
        }

        if (ArchotechEyeVisibilityUtility.IsDetectedForPlayer(pawn))
        {
            hidden = false;
        }
    }

    private static List<Thing> GetThingsUnderMouse(Vector3 clickPos, TargetingParameters clickParams, ITargetingSource source)
    {
        if (GenUiThingsUnderMouseMethod == null)
        {
            return new List<Thing>();
        }

        object result = GenUiThingsUnderMouseMethod.Invoke(null, new object[] { clickPos, 0.8f, clickParams, source });
        return result as List<Thing> ?? new List<Thing>();
    }
}
