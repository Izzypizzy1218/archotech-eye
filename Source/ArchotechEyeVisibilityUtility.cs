using System;
using RimWorld;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

internal static class ArchotechEyeVisibilityUtility
{
    public const float DetectionRadius = 18f;
    public const float RevealedAlpha = 0.3f;

    private static HediffDef cachedArchotechEyeDef;
    private static ThingDef cachedRevenantDef;
    private static bool archotechEyeResolved;
    private static bool revenantResolved;
    private static bool missingArchotechEyeLogged;

    public static HediffDef ArchotechEyeDef
    {
        get
        {
            if (archotechEyeResolved)
            {
                return cachedArchotechEyeDef;
            }

            archotechEyeResolved = true;
            try
            {
                cachedArchotechEyeDef = HediffDef.Named("ArchotechEye");
            }
            catch (Exception ex)
            {
                cachedArchotechEyeDef = null;
                if (!missingArchotechEyeLogged)
                {
                    missingArchotechEyeLogged = true;
                    Log.Warning($"[{ArchotechEyeInvisibilityDetectionMod.HarmonyId}] ArchotechEye hediff was not found. Detection will stay disabled. Exception: {ex.GetType().Name}");
                }
            }

            return cachedArchotechEyeDef;
        }
    }

    public static ThingDef RevenantDef
    {
        get
        {
            if (revenantResolved)
            {
                return cachedRevenantDef;
            }

            revenantResolved = true;
            cachedRevenantDef = DefDatabase<ThingDef>.GetNamedSilentFail("Revenant");
            return cachedRevenantDef;
        }
    }

    public static bool HasArchotechEye(Pawn pawn)
    {
        HediffDef archotechEyeDef = ArchotechEyeDef;
        if (archotechEyeDef == null || pawn?.health?.hediffSet == null)
        {
            return false;
        }

        return pawn.health.hediffSet.HasHediff(archotechEyeDef);
    }

    public static bool IsVanillaInvisible(Pawn pawn)
    {
        if (pawn?.health?.hediffSet == null)
        {
            return false;
        }

        // Read the vanilla predicate without letting our observer override feed back into the cache.
        using (VisibilityContext.EnterOriginal())
        {
            return pawn.IsPsychologicallyInvisible();
        }
    }

    public static bool IsDetectedBy(Pawn observer, Pawn target)
    {
        if (observer?.Spawned != true || target?.Spawned != true || observer == target || observer.Dead || target.Dead)
        {
            return false;
        }

        Map map = observer.Map;
        if (map == null || target.Map != map)
        {
            return false;
        }

        return map.GetComponent<InvisibilityDetectionMapComponent>()?.IsDetectedBy(observer, target) == true;
    }

    public static bool IsDetectedForPlayer(Pawn target)
    {
        if (target?.Spawned != true || target.Dead)
        {
            return false;
        }

        return target.Map?.GetComponent<InvisibilityDetectionMapComponent>()?.IsDetectedForPlayer(target) == true;
    }

    public static bool IsRevenant(Pawn pawn)
    {
        if (pawn == null)
        {
            return false;
        }

        ThingDef revenantDef = RevenantDef;
        if (revenantDef == null)
        {
            return false;
        }

        return pawn.def == revenantDef;
    }
}
