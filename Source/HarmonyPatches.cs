using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ArchotechEye_InvisibilityDetection;

[HarmonyPatch]
internal static class PawnTickVisibilityPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Pawn), "Tick");
        yield return AccessTools.Method(typeof(Pawn), "TickInterval", new[] { typeof(int) });
    }

    private static void Prefix(Pawn __instance, out VisibilityContext.Scope __state)
    {
        __state = VisibilityContext.Enter(__instance);
    }

    private static void Finalizer(VisibilityContext.Scope __state) => __state.Dispose();
}

[HarmonyPatch]
internal static class VerbVisibilityPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Verb), nameof(Verb.CanHitTargetFrom));
        yield return AccessTools.Method(typeof(Verb), nameof(Verb.CanHitTarget));
        yield return AccessTools.Method(typeof(Verb), nameof(Verb.ValidateTarget));
        yield return AccessTools.Method(typeof(Verb), nameof(Verb.OrderForceTarget));
        yield return AccessTools.Method(typeof(Verb), nameof(Verb.TryStartCastOn),
            new[] { typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
        yield return AccessTools.Method(typeof(Verb), nameof(Verb.TryStartCastOn),
            new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
    }

    private static void Prefix(Verb __instance, out VisibilityContext.Scope __state)
    {
        __state = VisibilityContext.Enter(__instance?.CasterPawn);
    }

    private static void Finalizer(VisibilityContext.Scope __state) => __state.Dispose();
}

[HarmonyPatch(typeof(Pawn), nameof(Pawn.ThreatDisabled))]
internal static class ThreatVisibilityPatch
{
    private static void Prefix(IAttackTargetSearcher disabledFor, out VisibilityContext.Scope __state)
    {
        __state = VisibilityContext.Enter(disabledFor?.Thing as Pawn);
    }

    // Only the invisibility predicate changes; downed, dormant and other exclusions still run.
    private static void Finalizer(VisibilityContext.Scope __state) => __state.Dispose();
}

[HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
internal static class AttackSearchVisibilityPatch
{
    private static void Prefix(IAttackTargetSearcher searcher, out VisibilityContext.Scope __state)
    {
        __state = VisibilityContext.Enter(searcher?.Thing as Pawn);
    }

    private static void Finalizer(VisibilityContext.Scope __state) => __state.Dispose();
}

[HarmonyPatch(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsPsychologicallyInvisible))]
internal static class PsychologicalVisibilityPatch
{
    private static void Postfix(Pawn pawn, ref bool __result)
    {
        if (__result && !VisibilityContext.ReadingOriginal &&
            ArchotechEyeVisibilityUtility.IsDetectedBy(VisibilityContext.CurrentSearcher, pawn))
        {
            __result = false;
        }
    }
}

[HarmonyPatch(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsHiddenFromPlayer))]
internal static class PlayerVisibilityPatch
{
    private static void Postfix(Pawn pawn, ref bool __result)
    {
        if (__result && !VisibilityContext.ReadingOriginal && ArchotechEyeVisibilityUtility.IsDetectedForPlayer(pawn))
        {
            __result = false;
        }
    }
}

[HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
internal static class DetectedPawnRenderingPatch
{
    private static void Postfix(Pawn ___pawn, ref PawnDrawParms __result)
    {
        ApplySilhouette(___pawn, ref __result);
    }

    internal static void ApplySilhouette(Pawn pawn, ref PawnDrawParms parms)
    {
        if (parms.flags.FlagSet(PawnRenderFlags.Cache) || parms.flags.FlagSet(PawnRenderFlags.Portrait) ||
            !ArchotechEyeVisibilityUtility.IsDetectedForPlayer(pawn))
        {
            return;
        }

        // Keep the vanilla invisibility material, which supports fading on the render tree.
        Color tint = parms.tint;
        tint.a = ArchotechEyeVisibilityUtility.RevealedAlpha;
        parms.tint = tint;
        if (ArchotechEyeVisibilityUtility.IsAlphaStalkingDetectedForPlayer(pawn))
        {
            parms.flags |= PawnRenderFlags.Invisible;
        }
    }
}

[HarmonyPatch(typeof(GenUI), nameof(GenUI.TargetsAt))]
internal static class ManualTargetVisibilityPatch
{
    private static void Postfix(ITargetingSource source, ref IEnumerable<LocalTargetInfo> __result)
    {
        Pawn observer = source?.CasterPawn;
        if (__result != null && observer?.Spawned == true &&
            observer.Map?.GetComponent<InvisibilityDetectionMapComponent>()?.HasDetectedTargets(observer) == true)
        {
            __result = WithObserver(__result, observer);
        }
    }

    internal static IEnumerable<LocalTargetInfo> WithObserver(IEnumerable<LocalTargetInfo> targets, Pawn observer)
    {
        using (IEnumerator<LocalTargetInfo> enumerator = targets.GetEnumerator())
        {
            while (true)
            {
                bool hasNext;
                LocalTargetInfo current = default;
                // GenUI.TargetsAt is lazy: the observer must be set during MoveNext, not the factory call.
                using (VisibilityContext.Enter(observer))
                {
                    hasNext = enumerator.MoveNext();
                    if (hasNext)
                    {
                        current = enumerator.Current;
                    }
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return current;
            }
        }
    }
}
