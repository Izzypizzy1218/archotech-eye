using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ArchotechEye_InvisibilityDetection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private static Pawn graphicsTarget;
    private static int graphicsDirtyRequests;

    private static void TestAlphaAnimalsStalking()
    {
        string dll = Path.Combine(alphaAnimalsDirectory, "Assemblies", "AlphaBehavioursAndEvents.dll");
        if (!File.Exists(dll))
        {
            Check(!AlphaAnimalsCompatibility.Installed, "Missing Alpha Animals does not register an optional graphics patch");
            Check(!AlphaAnimalsCompatibility.HasStalkingHediff(null), "Missing optional adapter is null-safe");
            Console.WriteLine("SKIP: Alpha Animals is not available for the optional integration checks.");
            return;
        }

        Assembly alpha = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
        Type stalkingType = alpha.GetType("AlphaBehavioursAndEvents.Hediff_Stalking", throwOnError: true);
        Check(AlphaAnimalsCompatibility.Installed && AlphaAnimalsCompatibility.Enabled,
            "Optional adapter registers against the real Alpha Animals DLL and defaults to enabled");
        Check(Harmony.GetPatchInfo(AlphaAnimalsCompatibility.GraphicForMethod)?.Owners.Contains(ArchotechEyeInvisibilityDetectionMod.HarmonyId) == true,
            "The actual Alpha Animals graphics method has the optional patch");
        Map map = CompatibilityMap();
        InvisibilityDetectionMapComponent cache = map.GetComponent<InvisibilityDetectionMapComponent>();
        Pawn observer = HealthyPawn(100, new IntVec3(5, 0, 5));
        observer.health.hediffSet.hediffs.Add(new Hediff { pawn = observer, def = ArchotechEyeVisibilityUtility.ArchotechEyeDef });
        observer.def.race.intelligence = Intelligence.Humanlike;
        Faction.OfPlayer.def = new FactionDef { defName = "DiagnosticPlayerFaction", isPlayer = true };
        AccessTools.Field(typeof(Thing), "factionInt").SetValue(observer, Faction.OfPlayer);
        Pawn target = HealthyPawn(101, new IntVec3(41, 0, 5));
        HediffWithComps stalking = (HediffWithComps)RuntimeHelpers.GetUninitializedObject(stalkingType);
        stalking.pawn = target;
        stalking.def = new HediffDef { defName = "UnrelatedDiagnosticName" };
        stalking.comps = new List<HediffComp>();
        target.health.hediffSet.hediffs.Add(stalking);
        AccessTools.Field(typeof(MapPawns), "pawnsSpawned").SetValue(map.mapPawns, new List<Pawn> { observer, target });

        Pawn_DrawTracker drawer = (Pawn_DrawTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_DrawTracker));
        drawer.renderer = (PawnRenderer)RuntimeHelpers.GetUninitializedObject(typeof(PawnRenderer));
        AccessTools.Field(typeof(PawnRenderer), "pawn").SetValue(drawer.renderer, target);
        AccessTools.Field(typeof(Pawn), "drawer").SetValue(target, drawer);
        graphicsTarget = target;
        graphicsDirtyRequests = 0;
        Harmony graphicsProbe = new Harmony("archotecheye.tests.graphics-dirty-probe");
        MethodInfo dirtyMethod = AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.SetAllGraphicsDirty));
        graphicsProbe.Patch(dirtyMethod, prefix: new HarmonyMethod(typeof(Program), nameof(TrackGraphicsDirty)));

        try
        {
            RunAlphaAnimalsChecks(cache, observer, target, stalking, map);
        }
        finally
        {
            graphicsProbe.Unpatch(dirtyMethod, HarmonyPatchType.All, graphicsProbe.Id);
            graphicsTarget = null;
            ArchotechEyeInvisibilityDetectionMod.Settings.DetectAlphaStalking = true;
        }
        TestAlphaGraphicTranspiler();
    }

    private static void RunAlphaAnimalsChecks(InvisibilityDetectionMapComponent cache, Pawn observer, Pawn target,
        HediffWithComps stalking, Map map)
    {
        Check(observer.IsColonistPlayerControlled, "Alpha fixture has a player-controlled archotech-eye observer");
        ArchotechEyeInvisibilityDetectionMod.Settings.DetectAlphaStalking = false;

        Check(!ArchotechEyeVisibilityUtility.IsVanillaInvisible(target),
            "REPRO: actual Alpha stalking hediff is not vanilla psychological invisibility");
        RefreshAlphaCache(cache, 1000);
        Check(!cache.IsDetectedBy(observer, target), "REPRO: native-only cache misses Alpha stalking at 36 tiles with clear line of sight");
        Check(InvisibilityDiagnostics.Inspect(target).State == InvisibilityDiagnosticState.CustomStealthDisabled,
            "Diagnostics identify the real stalking state even when its adapter is disabled");

        ArchotechEyeInvisibilityDetectionMod.Settings.DetectAlphaStalking = true;
        RefreshAlphaCache(cache, 1059);
        Check(!cache.IsDetectedBy(observer, target), "Enabling compatibility does not bypass the 60-tick cache interval");
        RefreshAlphaCache(cache, 1060);
        Check(cache.IsDetectedBy(observer, target), "Alpha stalking at exactly 36 tiles is cached for its observer");
        Check(cache.IsDetectedForPlayer(target) && cache.IsAlphaStalkingDetectedForPlayer(target),
            "The detecting player's eye publishes a separate Alpha silhouette cache");
        Check(AlphaAnimalsCompatibility.HasStalkingHediff(target), "Recognition uses the real state type, not a race or DefName");
        Check(AlphaAnimalsCompatibility.SelectStalkingGraphic(stalking, target) == null,
            "Detected stalking chooses the original non-hidden graphics branch");
        Check(ReferenceEquals(target.health.hediffSet.hediffs[0], stalking) && !ArchotechEyeVisibilityUtility.IsVanillaInvisible(target),
            "Revealing graphics does not remove stalking or change native psychological invisibility");
        Check(InvisibilityDiagnostics.Inspect(target).State == InvisibilityDiagnosticState.SupportedCustomStealth,
            "Diagnostics distinguish supported custom stealth from native invisibility");

        PawnDrawParms parms = new PawnDrawParms { tint = Color.white };
        DetectedPawnRenderingPatch.ApplySilhouette(target, ref parms);
        Check(Math.Abs(parms.tint.a - 0.3f) < 0.001f && parms.flags.FlagSet(PawnRenderFlags.Invisible),
            "Alpha silhouette requests the fade-capable material and alpha 0.3");
        parms = new PawnDrawParms { tint = Color.white, flags = PawnRenderFlags.Portrait };
        DetectedPawnRenderingPatch.ApplySilhouette(target, ref parms);
        Check(parms.tint.a == 1f && !parms.flags.FlagSet(PawnRenderFlags.Invisible), "Alpha portraits remain unchanged");
        parms = new PawnDrawParms { tint = Color.white, flags = PawnRenderFlags.Cache };
        DetectedPawnRenderingPatch.ApplySilhouette(target, ref parms);
        Check(parms.tint.a == 1f && !parms.flags.FlagSet(PawnRenderFlags.Invisible), "Alpha atlas cache passes remain unchanged");

        Pawn otherObserver = HealthyPawn(102, new IntVec3(4, 0, 5));
        using (VisibilityContext.Enter(otherObserver))
        {
            Check(!target.IsPsychologicallyInvisible(), "Other pawns retain Alpha Animals' original targeting behavior");
        }
        Check(!cache.IsDetectedBy(otherObserver, target), "An undetecting pawn cannot borrow the eye's cache");

        int refresh = cache.LastRefreshTick;
        Building wall = (Building)RuntimeHelpers.GetUninitializedObject(typeof(Building));
        wall.def = new ThingDef { fillPercent = 1f, category = ThingCategory.Building };
        int wallIndex = map.cellIndices.CellToIndex(new IntVec3(15, 0, 5));
        map.edificeGrid.InnerArray[wallIndex] = wall;
        Check(AlphaAnimalsCompatibility.SelectStalkingGraphic(stalking, target) == null && cache.LastRefreshTick == refresh,
            "Graphics reads the published cache only, without refreshing or testing line of sight");
        RefreshAlphaCache(cache, 1120);
        Check(!cache.IsDetectedBy(observer, target) && !cache.IsAlphaStalkingDetectedForPlayer(target), "A wall blocks the Alpha reveal after cache refresh");
        Check(ReferenceEquals(AlphaAnimalsCompatibility.SelectStalkingGraphic(stalking, target), stalking),
            "Losing line of sight restores the original hidden graphics branch");
        map.edificeGrid.InnerArray[wallIndex] = null;
        target.SetPositionDirect(new IntVec3(42, 0, 5));
        RefreshAlphaCache(cache, 1180);
        Check(!cache.IsDetectedBy(observer, target), "Alpha stalking at 37 tiles is not detected");
        target.SetPositionDirect(new IntVec3(41, 0, 5));
        RefreshAlphaCache(cache, 1240);
        Check(cache.IsAlphaStalkingDetectedForPlayer(target), "Returning to 36 tiles restores the silhouette cache");

        ArchotechEyeInvisibilityDetectionMod.Settings.DetectAlphaStalking = false;
        Check(AlphaAnimalsCompatibility.SelectStalkingGraphic(stalking, target) == null,
            "Changing the option does not mutate the published render snapshot mid-frame");
        RefreshAlphaCache(cache, 1300);
        Check(!cache.IsDetectedBy(observer, target) && !cache.IsAlphaStalkingDetectedForPlayer(target),
            "Disabling the optional adapter restores native-only detection at the next refresh");
        Check(ReferenceEquals(AlphaAnimalsCompatibility.SelectStalkingGraphic(stalking, target), stalking),
            "Disabled compatibility leaves the original graphics branch untouched");

        target.health.hediffSet.hediffs.Add(InvisibilityHediff(target, -99999));
        RefreshAlphaCache(cache, 1360);
        Check(cache.IsDetectedBy(observer, target) && !cache.IsAlphaStalkingDetectedForPlayer(target),
            "Disabling Alpha support does not disable native-system invisibility detection");
        ArchotechEyeInvisibilityDetectionMod.Settings.DetectAlphaStalking = true;
        RefreshAlphaCache(cache, 1420);
        Check(cache.IsAlphaStalkingDetectedForPlayer(target), "Overlapping native invisibility and Alpha stalking retain both states");
        int dirtyBefore = graphicsDirtyRequests;
        target.health.hediffSet.hediffs.Remove(stalking);
        RefreshAlphaCache(cache, 1480);
        Check(cache.IsDetectedForPlayer(target) && !cache.IsAlphaStalkingDetectedForPlayer(target),
            "Removing stalking preserves an independently active native invisibility reveal");
        Check(graphicsDirtyRequests == dirtyBefore + 1,
            "A custom graphics-state change invalidates graphics even when overall detection remains true");

        target.health.hediffSet.hediffs.Clear();
        target.health.hediffSet.hediffs.Add(new HediffWithComps { pawn = target, def = new HediffDef { defName = "AA_InvisibleArcticLion" } });
        RefreshAlphaCache(cache, 1540);
        Check(!cache.IsDetectedBy(observer, target) && !AlphaAnimalsCompatibility.HasStalkingHediff(target),
            "A matching DefName without the actual state type is not treated as stealth");

        target.health.hediffSet.hediffs.Clear();
        target.health.hediffSet.hediffs.Add(stalking);
        AccessTools.Field(typeof(Thing), "factionInt").SetValue(observer, null);
        RefreshAlphaCache(cache, 1600);
        Check(cache.IsDetectedBy(observer, target) && !cache.IsDetectedForPlayer(target),
            "An enemy or non-player observer does not reveal Alpha graphics for the player");
        AccessTools.Field(typeof(Thing), "factionInt").SetValue(observer, Faction.OfPlayer);
        observer.health.hediffSet.hediffs.Clear();
        RefreshAlphaCache(cache, 1660);
        Check(!cache.IsDetectedBy(observer, target), "Removing the archotech eye removes Alpha detection");
        observer.health.hediffSet.hediffs.Add(new Hediff { pawn = observer, def = ArchotechEyeVisibilityUtility.ArchotechEyeDef });
        RefreshAlphaCache(cache, 1720);
        Check(cache.IsAlphaStalkingDetectedForPlayer(target), "Re-equipping the eye restores Alpha detection");

        otherObserver.SetPositionDirect(new IntVec3(50, 0, 5));
        otherObserver.def.race.intelligence = Intelligence.Humanlike;
        AccessTools.Field(typeof(Thing), "factionInt").SetValue(otherObserver, Faction.OfPlayer);
        otherObserver.health.hediffSet.hediffs.Add(new Hediff { pawn = otherObserver, def = ArchotechEyeVisibilityUtility.ArchotechEyeDef });
        ((List<Pawn>)map.mapPawns.AllPawnsSpawned).Add(otherObserver);
        map.edificeGrid.InnerArray[wallIndex] = wall;
        RefreshAlphaCache(cache, 1780);
        Check(!cache.IsDetectedBy(observer, target) && cache.IsDetectedBy(otherObserver, target),
            "Two archotech eyes retain independent Alpha detections on opposite sides of a wall");
        Check(cache.IsAlphaStalkingDetectedForPlayer(target), "Any valid player's eye can maintain the player silhouette");
        otherObserver.health.hediffSet.hediffs.Clear();
        RefreshAlphaCache(cache, 1840);
        Check(!cache.IsAlphaStalkingDetectedForPlayer(target), "A blocked eye cannot borrow a removed eye's reveal");
        map.edificeGrid.InnerArray[wallIndex] = null;
        RefreshAlphaCache(cache, 1900);
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(target, (sbyte)-1);
        Check(!ArchotechEyeVisibilityUtility.IsAlphaStalkingDetectedForPlayer(target) &&
            ReferenceEquals(AlphaAnimalsCompatibility.SelectStalkingGraphic(stalking, target), stalking),
            "A despawned target cannot retain a cached graphics reveal");
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(target, (sbyte)0);
        cache.MapRemoved();
        Check(!cache.IsAlphaStalkingDetectedForPlayer(target) && !cache.IsDetectedBy(observer, target),
            "Map removal clears both observer and Alpha graphics snapshots");
        Check(AlphaAnimalsCompatibility.SelectStalkingGraphic(null, null) == null, "Optional graphics lookup is null-safe");
    }

    private static bool TrackGraphicsDirty(Pawn ___pawn)
    {
        if (___pawn != graphicsTarget)
        {
            return true;
        }
        graphicsDirtyRequests++;
        return false;
    }

    private static void RefreshAlphaCache(InvisibilityDetectionMapComponent cache, int tick)
    {
        AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(Current.Game.tickManager, tick);
        cache.MapComponentTick();
    }

    private static void TestAlphaGraphicTranspiler()
    {
        MethodInfo lookup = AccessTools.Method(typeof(HediffSet), nameof(HediffSet.GetFirstHediffOfDef), new[] { typeof(HediffDef), typeof(bool) });
        List<CodeInstruction> original = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Nop), new CodeInstruction(OpCodes.Callvirt, lookup), new CodeInstruction(OpCodes.Ret)
        };
        List<CodeInstruction> patched = AlphaAnimalsCompatibility.GraphicTranspiler(original).ToList();
        Check(patched.Count == original.Count + 2 && ReferenceEquals(patched[0], original[0]) && ReferenceEquals(patched[4], original[2]),
            "Graphics adapter preserves original instructions except for one state-selection call");
        try
        {
            AlphaAnimalsCompatibility.GraphicTranspiler(new[] { new CodeInstruction(OpCodes.Ret) }).ToList();
            Check(false, "Unsupported Alpha graphics shape must not be patched");
        }
        catch (InvalidOperationException)
        {
            Check(true, "Unsupported graphics API shape is rejected rather than broadly patched");
        }
    }

    private static Map CompatibilityMap()
    {
        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(60, 1, 60) };
        map.components = new List<MapComponent>();
        map.cellIndices = new CellIndices(map);
        map.edificeGrid = new EdificeGrid(map);
        map.mapPawns = (MapPawns)RuntimeHelpers.GetUninitializedObject(typeof(MapPawns));
        AccessTools.Field(typeof(MapPawns), "pawnsSpawned").SetValue(map.mapPawns, new List<Pawn>());
        AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map> { map });
        map.components.Add(new InvisibilityDetectionMapComponent(map));
        return map;
    }
}
