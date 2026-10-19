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
    private static string ratkinAnomalyDirectory;

    private static int Main(string[] args)
    {
        string gameDir = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Steam\steamapps\common\RimWorld";
        string harmonyDir = args.Length > 1 ? args[1] : @"C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
        ratkinAnomalyDirectory = args.Length > 2 ? args[2] : @"C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\3716441120";
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { gameDir, harmonyDir, Path.Combine(ratkinAnomalyDirectory, "1.6", "Assemblies"),
                @"C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\1578693166\1.7\Assemblies" })
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
            string modPath = args.Length > 3 ? Path.GetFullPath(args[3]) :
                Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "ArchotechEye_InvisibilityDetection.dll");
            Console.WriteLine("Testing mod assembly: " + modPath);
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
        TestRatkinAnomaly();
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
        Pawn target = HealthyPawn(6, new IntVec3(41, 0, 5));
        target.health.hediffSet.hediffs.Add(InvisibilityHediff(target, -99999));
        MethodInfo canDetect = AccessTools.Method(typeof(InvisibilityDetectionMapComponent), "CanWatcherDetectTarget");
        Check(ArchotechEyeVisibilityUtility.DetectionRadius == 36f, "Detection radius is 36 tiles");
        Check((bool)canDetect.Invoke(component, new object[] { watcher, target }), "Exactly 36 tiles is detected");
        target.SetPositionDirect(new IntVec3(42, 0, 5));
        Check(!(bool)canDetect.Invoke(component, new object[] { watcher, target }), "Beyond 36 tiles is rejected");
        target.SetPositionDirect(new IntVec3(29, 0, 5));
        Check((bool)canDetect.Invoke(component, new object[] { watcher, target }), "24 tiles is now in range");
        target.SetPositionDirect(new IntVec3(41, 0, 5));
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

    private static Pawn HealthyPawn(int id, IntVec3 position, Type pawnType = null)
    {
        Pawn pawn = pawnType == null ? BarePawn(id) : (Pawn)RuntimeHelpers.GetUninitializedObject(pawnType);
        pawn.thingIDNumber = id;
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

    private static void TestRatkinAnomaly()
    {
        string dllPath = Path.Combine(ratkinAnomalyDirectory, "1.6", "Assemblies", "RatkinAnomaly.dll");
        if (!File.Exists(dllPath))
        {
            Console.WriteLine("SKIP: Ratkin Anomaly is not installed; generic invisibility checks still ran.");
            return;
        }

        Assembly ratkin = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
        Map map = Current.Game.Maps[0];
        InvisibilityDetectionMapComponent component = map.GetComponent<InvisibilityDetectionMapComponent>();
        Pawn observer = HealthyPawn(20, new IntVec3(5, 0, 5));
        Pawn otherObserver = HealthyPawn(21, new IntVec3(6, 0, 5));
        MethodInfo canDetect = AccessTools.Method(typeof(InvisibilityDetectionMapComponent), "CanWatcherDetectTarget");
        HediffDefOf.HoraxianInvisibility = new HediffDef { defName = "HoraxianInvisibility" };
        int id = 30;
        foreach (string name in new[] { "De_lovely", "Obsession" })
        {
            Type type = ratkin.GetType("RatkinAnomaly." + name, throwOnError: true);
            Pawn target = HealthyPawn(id++, new IntVec3(41, 0, 5), type);
            HediffWithComps hediff = InvisibilityHediff(target, -99999);
            hediff.def = HediffDefOf.HoraxianInvisibility;
            target.health.hediffSet.hediffs.Add(hediff);
            object nativeComp = AccessTools.Property(type, "Invisibility").GetValue(target);
            Check(ReferenceEquals(nativeComp, hediff.TryGetComp<HediffComp_Invisibility>()), name + " uses the actual vanilla invisibility comp");
            Check(ArchotechEyeVisibilityUtility.IsVanillaInvisible(target), name + " is recognized as invisible");
            Check((bool)canDetect.Invoke(component, new object[] { observer, target }), name + " can be detected at 36 tiles");
            target.SetPositionDirect(new IntVec3(42, 0, 5));
            Check(!(bool)canDetect.Invoke(component, new object[] { observer, target }), name + " stays hidden at 37 tiles");
            target.SetPositionDirect(new IntVec3(41, 0, 5));
            Building wall = (Building)RuntimeHelpers.GetUninitializedObject(typeof(Building));
            wall.def = new ThingDef { fillPercent = 1f, category = ThingCategory.Building };
            int wallIndex = map.cellIndices.CellToIndex(new IntVec3(15, 0, 5));
            map.edificeGrid.InnerArray[wallIndex] = wall;
            Check(!(bool)canDetect.Invoke(component, new object[] { observer, target }), name + " stays hidden behind a wall");
            map.edificeGrid.InnerArray[wallIndex] = null;

            object snapshot = AccessTools.Field(typeof(InvisibilityDetectionMapComponent), "snapshot").GetValue(component);
            ((Dictionary<Pawn, HashSet<Pawn>>)AccessTools.Field(snapshot.GetType(), "ByWatcher").GetValue(snapshot))[observer] = new HashSet<Pawn> { target };
            ((HashSet<Pawn>)AccessTools.Field(snapshot.GetType(), "ForPlayer").GetValue(snapshot)).Add(target);
            using (VisibilityContext.Enter(observer))
            {
                Check(!target.IsPsychologicallyInvisible(), name + " passes targeting for its detecting observer");
            }
            using (VisibilityContext.Enter(otherObserver))
            {
                Check(target.IsPsychologicallyInvisible(), name + " remains invisible to other observers");
            }
            Check(!target.IsHiddenFromPlayer(), name + " is revealed to the player");
            PawnDrawParms parms = new PawnDrawParms { tint = Color.white, flags = PawnRenderFlags.Invisible };
            DetectedPawnRenderingPatch.ApplySilhouette(target, ref parms);
            Check(Math.Abs(parms.tint.a - 0.3f) < 0.001f, name + " renders at alpha 0.3");
            Check(!((HediffComp_Invisibility)nativeComp).PsychologicallyVisible, name + " remains naturally invisible without forcing its quest discovery");
        }
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
