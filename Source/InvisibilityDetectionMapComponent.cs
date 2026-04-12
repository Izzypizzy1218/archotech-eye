using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

public sealed class InvisibilityDetectionMapComponent : MapComponent
{
    private const int RefreshIntervalTicks = 60;

    private readonly Dictionary<Pawn, HashSet<Pawn>> detectedByWatcher = new Dictionary<Pawn, HashSet<Pawn>>();
    private readonly HashSet<Pawn> detectedForPlayer = new HashSet<Pawn>();
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

        Dictionary<Pawn, HashSet<Pawn>> previousByWatcher = CloneDetectionDictionary(detectedByWatcher);
        HashSet<Pawn> previousForPlayer = new HashSet<Pawn>(detectedForPlayer);

        detectedByWatcher.Clear();
        detectedForPlayer.Clear();

        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null || spawnedPawns.Count == 0)
        {
            NotifyChangedTargets(previousByWatcher, previousForPlayer);
            return;
        }

        List<Pawn> invisibleTargets = spawnedPawns.Where(ArchotechEyeVisibilityUtility.IsVanillaInvisible).ToList();
        if (invisibleTargets.Count == 0)
        {
            NotifyChangedTargets(previousByWatcher, previousForPlayer);
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

        NotifyChangedTargets(previousByWatcher, previousForPlayer);
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

    private void NotifyChangedTargets(Dictionary<Pawn, HashSet<Pawn>> previousByWatcher, HashSet<Pawn> previousForPlayer)
    {
        HashSet<Pawn> changedTargets = new HashSet<Pawn>();
        bool newlyDetectedSightstealerForPlayer = false;

        AddTargets(changedTargets, previousForPlayer);
        AddTargets(changedTargets, detectedForPlayer);

        foreach (KeyValuePair<Pawn, HashSet<Pawn>> pair in previousByWatcher)
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
            if (previousForPlayer.Contains(playerDetectedTarget))
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
            Find.LetterStack.ReceiveLetter(
                "ArchotechEyeInvisibilityDetection_SightstealerRevealedLabel".Translate(),
                "ArchotechEyeInvisibilityDetection_SightstealerRevealedLetter".Translate(),
                LetterDefOf.ThreatSmall,
                lookTargets: null);
        }
    }

    private static Dictionary<Pawn, HashSet<Pawn>> CloneDetectionDictionary(Dictionary<Pawn, HashSet<Pawn>> source)
    {
        Dictionary<Pawn, HashSet<Pawn>> clone = new Dictionary<Pawn, HashSet<Pawn>>();
        foreach (KeyValuePair<Pawn, HashSet<Pawn>> pair in source)
        {
            clone[pair.Key] = new HashSet<Pawn>(pair.Value);
        }

        return clone;
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
}
