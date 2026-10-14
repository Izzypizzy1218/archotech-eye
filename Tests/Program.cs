using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ArchotechEye_InvisibilityDetection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

internal static class Program
{
    private static int assertions;

    private static int Main(string[] args)
    {
        string gameDir = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Steam\steamapps\common\RimWorld";
        string harmonyDir = args.Length > 1 ? args[1] : @"C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { gameDir, harmonyDir })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path))
                {
                    return context.LoadFromAssemblyPath(path);
                }
            }
            return null;
        };

        try
        {
            string modPath = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "ArchotechEye_InvisibilityDetection.dll");
            AssemblyLoadContext.Default.LoadFromAssemblyPath(modPath);
            Run();
            Console.WriteLine($"PASS: {assertions} assertions against installed RimWorld 1.7 assemblies.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        new ArchotechEyeInvisibilityDetectionMod(null);
        MethodBase[] expected =
        {
            AccessTools.Method(typeof(Pawn), "Tick"),
            AccessTools.Method(typeof(Pawn), "TickInterval", new[] { typeof(int) }),
            AccessTools.Method(typeof(Pawn), nameof(Pawn.ThreatDisabled)),
            AccessTools.Method(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget)),
            AccessTools.Method(typeof(Verb), nameof(Verb.CanHitTarget)),
            AccessTools.Method(typeof(Verb), nameof(Verb.CanHitTargetFrom)),
            AccessTools.Method(typeof(Verb), nameof(Verb.ValidateTarget)),
            AccessTools.Method(typeof(Verb), nameof(Verb.OrderForceTarget)),
            AccessTools.Method(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }),
            AccessTools.Method(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }),
            AccessTools.Method(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsPsychologicallyInvisible)),
            AccessTools.Method(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsHiddenFromPlayer)),
            AccessTools.Method(typeof(PawnRenderer), "GetDrawParms"),
            AccessTools.Method(typeof(GenUI), nameof(GenUI.TargetsAt))
        };
        foreach (MethodBase method in expected)
        {
            Check(method != null && Harmony.GetPatchInfo(method)?.Owners.Contains(ArchotechEyeInvisibilityDetectionMod.HarmonyId) == true,
                "Harmony registered " + method?.DeclaringType?.Name + "." + method?.Name);
        }
        Check(Harmony.GetAllPatchedMethods().Count(method => Harmony.GetPatchInfo(method).Owners.Contains(ArchotechEyeInvisibilityDetectionMod.HarmonyId)) == expected.Length,
            "Only intended methods are patched");

        Pawn outer = BarePawn(1);
        Pawn inner = BarePawn(2);
        using (VisibilityContext.Enter(outer))
        {
            try
            {
                using (VisibilityContext.Enter(inner))
                {
                    Check(VisibilityContext.CurrentSearcher == inner, "Nested observer");
                    throw new InvalidOperationException("intentional");
                }
            }
            catch (InvalidOperationException)
            {
                Check(VisibilityContext.CurrentSearcher == outer, "Exception restores observer");
            }

            using (IEnumerator<LocalTargetInfo> iterator = ManualTargetVisibilityPatch.WithObserver(Candidates(inner), inner).GetEnumerator())
            {
                Check(iterator.MoveNext(), "Lazy target iterator produces a target");
                Check(VisibilityContext.CurrentSearcher == outer, "Iterator does not leak observer across yield");
            }
            Check(VisibilityContext.CurrentSearcher == outer, "Early disposal preserves caller observer");

            try
            {
                ManualTargetVisibilityPatch.WithObserver(FailingCandidates(inner), inner).ToList();
            }
            catch (InvalidOperationException)
            {
                Check(VisibilityContext.CurrentSearcher == outer, "Iterator exception restores observer");
            }
        }
        Check(VisibilityContext.CurrentSearcher == null, "Observer fully restored");

        using (VisibilityContext.EnterOriginal())
        {
            using (VisibilityContext.EnterOriginal())
            {
                Check(VisibilityContext.ReadingOriginal, "Nested original checks suppress detection");
            }
            Check(VisibilityContext.ReadingOriginal, "Nested original scope preserves outer suppression");
        }
        Check(!VisibilityContext.ReadingOriginal, "Original predicate suppression is restored");
        Check(!ArchotechEyeVisibilityUtility.IsVanillaInvisible(null), "Null pawn is safe");
        Check(!ArchotechEyeVisibilityUtility.IsDetectedBy(null, null), "Null observer is safe");
        Check(!ArchotechEyeVisibilityUtility.IsDetectedForPlayer(null), "Null render target is safe");

        PawnDrawParms parms = new PawnDrawParms { flags = PawnRenderFlags.Portrait, tint = Color.white };
        DetectedPawnRenderingPatch.ApplySilhouette(null, ref parms);
        Check(parms.tint.a == 1f, "Portraits keep their alpha");
        TestMultipleInvisibilityHediffs();
        TestDetectionAndCombat();
    }

    private static IEnumerable<LocalTargetInfo> Candidates(Pawn observer)
    {
        Check(VisibilityContext.CurrentSearcher == observer, "Lazy iteration sees the shooting pawn");
        yield return new IntVec3(5, 0, 5);
    }

    private static IEnumerable<LocalTargetInfo> FailingCandidates(Pawn observer)
    {
        Check(VisibilityContext.CurrentSearcher == observer, "Failing iterator sees observer");
        throw new InvalidOperationException("intentional");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static Pawn BarePawn(int id)
    {
        Pawn pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.thingIDNumber = id;
        return pawn;
    }

    private static void TestMultipleInvisibilityHediffs()
    {
        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        game.tickManager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
        AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(game.tickManager, 1000);
        game.InitData = (GameInitData)RuntimeHelpers.GetUninitializedObject(typeof(GameInitData));
        game.InitData.playerFaction = (Faction)RuntimeHelpers.GetUninitializedObject(typeof(Faction));
        Current.Game = game;

        Pawn pawn = BarePawn(3);
        pawn.def = new ThingDef { defName = "TestPawn", race = new RaceProperties() };
        pawn.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        pawn.health.hediffSet = new HediffSet(pawn);
        pawn.mindState = (Pawn_MindState)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_MindState));
        pawn.mindState.lastBecameVisibleTick = 0;
        pawn.mindState.lastBecameInvisibleTick = 500;
        HediffWithComps visible = InvisibilityHediff(pawn, 1000);
        HediffWithComps invisible = InvisibilityHediff(pawn, -99999);
        pawn.health.hediffSet.hediffs.Add(visible);
        pawn.health.hediffSet.hediffs.Add(invisible);
        Check(visible.TryGetComp<HediffComp_Invisibility>().PsychologicallyVisible, "First invisibility comp is disrupted");
        Check(ArchotechEyeVisibilityUtility.IsVanillaInvisible(pawn), "Later invisible comp is not ignored");
        Check(!VisibilityContext.ReadingOriginal, "Raw invisibility check restores suppression");
    }

    private static HediffWithComps InvisibilityHediff(Pawn pawn, int disruptedTick)
    {
        HediffWithComps hediff = new HediffWithComps { pawn = pawn, def = new HediffDef { defName = "TestInvisibility" } };
        HediffComp_Invisibility comp = new HediffComp_Invisibility
        {
            parent = hediff,
            props = new HediffCompProperties_Invisibility { fadeDurationTicks = 60, recoverFromDisruptedTicks = 60 }
        };
        hediff.comps = new List<HediffComp> { comp };
        AccessTools.Field(typeof(HediffComp_Invisibility), "lastDisrupted").SetValue(comp, disruptedTick);
        return hediff;
    }

    private static void TestDetectionAndCombat()
    {
        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(60, 1, 60) };
        map.components = new List<MapComponent>();
        map.cellIndices = new CellIndices(map);
        map.edificeGrid = new EdificeGrid(map);
        AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map> { map });
        InvisibilityDetectionMapComponent component = new InvisibilityDetectionMapComponent(map);
        map.components.Add(component);

        Pawn watcher = HealthyPawn(4, new IntVec3(5, 0, 5));
        Pawn otherWatcher = HealthyPawn(5, new IntVec3(6, 0, 5));
        Pawn target = HealthyPawn(6, new IntVec3(23, 0, 5));
        target.health.hediffSet.hediffs.Add(InvisibilityHediff(target, -99999));
        MethodInfo canDetect = AccessTools.Method(typeof(InvisibilityDetectionMapComponent), "CanWatcherDetectTarget");
        Check((bool)canDetect.Invoke(component, new object[] { watcher, target }), "Exactly 18 tiles is detected");
        target.SetPositionDirect(new IntVec3(24, 0, 5));
        Check(!(bool)canDetect.Invoke(component, new object[] { watcher, target }), "Beyond 18 tiles is rejected");
        target.SetPositionDirect(new IntVec3(23, 0, 5));
        Building wall = (Building)RuntimeHelpers.GetUninitializedObject(typeof(Building));
        wall.def = new ThingDef { fillPercent = 1f, category = ThingCategory.Building };
        int wallIndex = map.cellIndices.CellToIndex(new IntVec3(15, 0, 5));
        map.edificeGrid.InnerArray[wallIndex] = wall;
        Check(!(bool)canDetect.Invoke(component, new object[] { watcher, target }), "Wall blocks detection");
        map.edificeGrid.InnerArray[wallIndex] = null;
        Check((bool)canDetect.Invoke(component, new object[] { watcher, target }), "Removing wall restores detection");

        object snapshot = AccessTools.Field(typeof(InvisibilityDetectionMapComponent), "snapshot").GetValue(component);
        ((Dictionary<Pawn, HashSet<Pawn>>)AccessTools.Field(snapshot.GetType(), "ByWatcher").GetValue(snapshot))[watcher] = new HashSet<Pawn> { target };
        ((HashSet<Pawn>)AccessTools.Field(snapshot.GetType(), "ForPlayer").GetValue(snapshot)).Add(target);
        Check(ArchotechEyeVisibilityUtility.IsDetectedBy(watcher, target), "Observer reads its own cached detection");
        Check(!ArchotechEyeVisibilityUtility.IsDetectedBy(otherWatcher, target), "Other observer cannot borrow detection");
        using (VisibilityContext.Enter(watcher))
        {
            Check(!target.IsPsychologicallyInvisible(), "Detected target passes combat invisibility check");
            Check(ArchotechEyeVisibilityUtility.IsVanillaInvisible(target), "Cache refresh still reads underlying invisibility");
        }
        using (VisibilityContext.Enter(otherWatcher))
        {
            Check(target.IsPsychologicallyInvisible(), "Undetected target stays invisible to other observers");
        }
        Check(!target.IsHiddenFromPlayer(), "Player can see detected target");
        Check(target.ThreatDisabled(watcher), "Inactive target remains threat-disabled despite detection");
        Check(VisibilityContext.CurrentSearcher == null, "Combat Harmony finalizer restores observer");

        PawnDrawParms parms = new PawnDrawParms { tint = Color.white, flags = PawnRenderFlags.Invisible };
        DetectedPawnRenderingPatch.ApplySilhouette(target, ref parms);
        Check(Math.Abs(parms.tint.a - 0.3f) < 0.001f, "World silhouette alpha is 0.3");
        Check(parms.flags.FlagSet(PawnRenderFlags.Invisible), "Vanilla invisible rendering material is retained");
        parms = new PawnDrawParms { tint = Color.white, flags = PawnRenderFlags.Portrait };
        DetectedPawnRenderingPatch.ApplySilhouette(target, ref parms);
        Check(parms.tint.a == 1f, "Detected pawn portrait stays unchanged");
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(target, (sbyte)-1);
        Check(!ArchotechEyeVisibilityUtility.IsDetectedForPlayer(target), "Despawned target immediately loses visibility");
        component.MapRemoved();
        Check(!component.HasDetectedTargets(watcher), "Removed map drops detection cache");
    }

    private static Pawn HealthyPawn(int id, IntVec3 position)
    {
        Pawn pawn = BarePawn(id);
        pawn.def = new ThingDef { defName = "TestPawn", race = new RaceProperties() };
        AccessTools.Field(typeof(RaceProperties), "fleshType").SetValue(pawn.def.race, new FleshTypeDef { defName = "TestFlesh" });
        pawn.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        pawn.health.hediffSet = new HediffSet(pawn);
        pawn.mindState = (Pawn_MindState)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_MindState));
        pawn.mindState.mentalStateHandler = new MentalStateHandler(pawn);
        pawn.mindState.lastBecameInvisibleTick = 500;
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)0);
        pawn.SetPositionDirect(position);
        return pawn;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAIL: " + message);
        }
        assertions++;
        Console.WriteLine("OK: " + message);
    }
}
