using System;
using RimWorld;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

internal static class ArchotechEyeVisibilityUtility
{
    public const float DetectionRadius = 18f;
    public const float RevealedAlpha = 0.3f;

    private static HediffDef cachedArchotechEyeDef;
    private static ThingDef cachedSightstealerDef;
    private static bool archotechEyeResolved;
    private static bool sightstealerResolved;
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

    public static ThingDef SightstealerDef
    {
        get
        {
            if (sightstealerResolved)
            {
                return cachedSightstealerDef;
            }

            sightstealerResolved = true;
            cachedSightstealerDef = DefDatabase<ThingDef>.GetNamedSilentFail("Sightstealer");
            return cachedSightstealerDef;
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

        HediffComp_Invisibility invisibilityComp = pawn.GetInvisibilityComp();
        if (invisibilityComp == null)
        {
            return false;
        }

        return !invisibilityComp.PsychologicallyVisible;
    }

    public static bool IsDetectedBy(Pawn observer, Pawn target)
    {
        if (observer == null || target == null || observer == target)
        {
            return false;
        }

        Map map = observer.MapHeld;
        if (map == null || target.MapHeld != map)
        {
            return false;
        }

        return map.GetComponent<InvisibilityDetectionMapComponent>()?.IsDetectedBy(observer, target) == true;
    }

    public static bool IsDetectedForPlayer(Pawn target)
    {
        if (target == null)
        {
            return false;
        }

        return target.MapHeld?.GetComponent<InvisibilityDetectionMapComponent>()?.IsDetectedForPlayer(target) == true;
    }

    public static bool IsSightstealer(Pawn pawn)
    {
        if (pawn == null)
        {
            return false;
        }

        ThingDef sightstealerDef = SightstealerDef;
        if (sightstealerDef == null)
        {
            return false;
        }

        return pawn.def == sightstealerDef;
    }
}
