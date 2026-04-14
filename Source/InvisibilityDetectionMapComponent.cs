using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

public sealed class InvisibilityDetectionMapComponent : MapComponent
{
    private const int RefreshIntervalTicks = 60;

    private Dictionary<Pawn, HashSet<Pawn>> detectedByWatcher = new Dictionary<Pawn, HashSet<Pawn>>();
    private Dictionary<Pawn, HashSet<Pawn>> previousDetectedByWatcher = new Dictionary<Pawn, HashSet<Pawn>>();
    private HashSet<Pawn> detectedForPlayer = new HashSet<Pawn>();
    private HashSet<Pawn> previousDetectedForPlayer = new HashSet<Pawn>();
    private readonly List<Pawn> invisibleTargets = new List<Pawn>();
    private readonly HashSet<Pawn> changedTargets = new HashSet<Pawn>();
    private int lastRefreshTick = -1;

    public InvisibilityDetectionMapComponent(Map map) : base(map)
    {
    }

    public override void MapComponentTick()
    {
        base.MapComponentTick();

        int ticksGame = Find.TickManager.TicksGame;
        if (ticksGame == 0 || lastRefreshTick < 0 || ticksGame - lastRefreshTick >= RefreshIntervalTicks)
        {
            RefreshDetectionCache();
        }
    }

    public bool IsDetectedBy(Pawn observer, Pawn target)
    {
        if (observer == null || target == null)
        {
            return false;
        }

        if (!detectedByWatcher.TryGetValue(observer, out HashSet<Pawn> detectedTargets))
        {
            return false;
        }

        return detectedTargets.Contains(target);
    }

    public bool IsDetectedForPlayer(Pawn target)
    {
        if (target == null)
        {
            return false;
        }

        return detectedForPlayer.Contains(target);
    }

    private void RefreshDetectionCache()
    {
        lastRefreshTick = Find.TickManager.TicksGame;
        Swap(ref detectedByWatcher, ref previousDetectedByWatcher);
        Swap(ref detectedForPlayer, ref previousDetectedForPlayer);
        detectedByWatcher.Clear();
        detectedForPlayer.Clear();
        invisibleTargets.Clear();

        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null || spawnedPawns.Count == 0)
        {
            NotifyChangedTargets();
            return;
        }

        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn spawnedPawn = spawnedPawns[i];
            if (ArchotechEyeVisibilityUtility.IsVanillaInvisible(spawnedPawn))
            {
                invisibleTargets.Add(spawnedPawn);
            }
        }

        if (invisibleTargets.Count == 0)
        {
            NotifyChangedTargets();
            return;
        }

        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn watcher = spawnedPawns[i];
            if (!IsValidWatcher(watcher))
            {
                continue;
            }

            HashSet<Pawn> detectedTargets = null;
            for (int j = 0; j < invisibleTargets.Count; j++)
            {
                Pawn target = invisibleTargets[j];
                if (!CanWatcherDetectTarget(watcher, target))
                {
                    continue;
                }

                detectedTargets ??= new HashSet<Pawn>();
                detectedTargets.Add(target);

                if (watcher.IsColonistPlayerControlled)
                {
                    detectedForPlayer.Add(target);
                }
            }

            if (detectedTargets != null)
            {
                detectedByWatcher[watcher] = detectedTargets;
            }
        }

        NotifyChangedTargets();
    }

    private static bool IsValidWatcher(Pawn watcher)
    {
        if (watcher?.Spawned != true)
        {
            return false;
        }

        return ArchotechEyeVisibilityUtility.HasArchotechEye(watcher);
    }

    private bool CanWatcherDetectTarget(Pawn watcher, Pawn target)
    {
        if (watcher == null || target == null || watcher == target)
        {
            return false;
        }

        if (!target.Spawned || target.MapHeld != map || watcher.MapHeld != map)
        {
            return false;
        }

        if (watcher.Position.DistanceTo(target.Position) > ArchotechEyeVisibilityUtility.DetectionRadius)
        {
            return false;
        }

        return GenSight.LineOfSight(watcher.Position, target.Position, map, skipFirstCell: true);
    }

    private void NotifyChangedTargets()
    {
        changedTargets.Clear();
        bool newlyDetectedSightstealerForPlayer = false;

        AddTargets(changedTargets, previousDetectedForPlayer);
        AddTargets(changedTargets, detectedForPlayer);

        foreach (KeyValuePair<Pawn, HashSet<Pawn>> pair in previousDetectedByWatcher)
        {
            AddTargets(changedTargets, pair.Value);
        }

        foreach (KeyValuePair<Pawn, HashSet<Pawn>> pair2 in detectedByWatcher)
        {
            AddTargets(changedTargets, pair2.Value);
        }

        foreach (Pawn changedTarget in changedTargets)
        {
            if (changedTarget?.Spawned != true || changedTarget.Map != map)
            {
                continue;
            }

            map.attackTargetsCache?.UpdateTarget(changedTarget);
            changedTarget.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        foreach (Pawn playerDetectedTarget in detectedForPlayer)
        {
            if (previousDetectedForPlayer.Contains(playerDetectedTarget))
            {
                continue;
            }

            if (ArchotechEyeVisibilityUtility.IsSightstealer(playerDetectedTarget))
            {
                newlyDetectedSightstealerForPlayer = true;
                break;
            }
        }

        if (newlyDetectedSightstealerForPlayer)
        {
            Messages.Message("ArchotechEyeInvisibilityDetection_SightstealerRevealed".Translate(), MessageTypeDefOf.CautionInput, historical: false);
        }
    }

    private static void AddTargets(HashSet<Pawn> accumulator, IEnumerable<Pawn> targets)
    {
        if (targets == null)
        {
            return;
        }

        foreach (Pawn target in targets)
        {
            if (target != null)
            {
                accumulator.Add(target);
            }
        }
    }

    private static void Swap<T>(ref T left, ref T right)
    {
        T temp = left;
        left = right;
        right = temp;
    }
}
