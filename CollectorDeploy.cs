using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CollectorDeployer
{
    internal static class CollectorDeploy
    {
        public const int CollectorCount = 40;
        private const int OrbitalCollectorItemId = 2105;
        private const int SandItemId = 1099;

        // Same spacing limit BuildTool_Click enforces between two stellar-class stations (collectors included).
        private const float CollectorMinSqrDistance = 14297f;
        private const float MatchSqrDistance = 1f;

        private sealed class Plan
        {
            public Player player;
            public PlanetFactory factory;
            public ItemProto item;
            public readonly List<Vector3> missingTargets = new List<Vector3>();
            public readonly List<ExistingCollector> dismantle = new List<ExistingCollector>();
            public int existingCount;
            public int keptCount;
            public int recoverableCount;
            public int packageCount;

            public int NeededCount => missingTargets.Count;
            public int AvailableCount => packageCount + recoverableCount;
            public int ReservedFromPackage => Math.Min(packageCount, NeededCount);
        }

        private struct ExistingCollector
        {
            public int objId;
            public int protoId;
            public Vector3 pos;
            public bool recoverable;
            public bool isEntity;
        }

        private static bool _dialogOpen;

        public static void Request()
        {
            if (_dialogOpen)
                return;

            Plan plan = MakeValidPlan();
            if (plan == null)
                return;

            Dictionary<int, int> overflow = PredictOverflow(plan);
            if (overflow.Count == 0)
            {
                Execute(plan);
                return;
            }

            string items = string.Join(Lang.T("、", ", "), overflow.Select(kv => $"{ItemName(kv.Key)} ×{kv.Value}"));
            string message = Lang.T(
                $"背包空间不足，拆除旧的轨道采集器后以下物品会掉落：\n{items}\n\n是否继续？",
                $"Not enough inventory space. Removing the old orbital collectors will drop:\n{items}\n\nContinue?");

            _dialogOpen = true;
            UIMessageBox.Show(Lang.TipTitle, message, "否".Translate(), "是".Translate(), UIMessageBox.WARNING,
                () => _dialogOpen = false,
                () =>
                {
                    _dialogOpen = false;
                    Plan fresh = MakeValidPlan();
                    if (fresh != null)
                        Execute(fresh);
                });
        }

        // Returns null after showing the reason when nothing should be done.
        private static Plan MakeValidPlan()
        {
            Plan plan = MakePlan(out string error);
            if (plan == null)
            {
                UIRealtimeTip.Popup(error, true, 0);
                return null;
            }

            if (plan.existingCount > CollectorCount)
            {
                UIRealtimeTip.Popup(Lang.T(
                    $"本星球已有 {plan.existingCount} 个轨道采集器，超过 {CollectorCount} 个，不进行部署",
                    $"This planet already has {plan.existingCount} orbital collectors (more than {CollectorCount}); nothing deployed"), true, 0);
                return null;
            }

            if (plan.NeededCount == 0 && plan.dismantle.Count == 0)
            {
                UIRealtimeTip.Popup(Lang.T(
                    $"赤道上已部署 {CollectorCount} 个轨道采集器",
                    $"{CollectorCount} orbital collectors are already deployed on the equator"), false, 0);
                return null;
            }

            if (plan.AvailableCount < plan.NeededCount)
            {
                PopupInsufficient(plan);
                return null;
            }
            return plan;
        }

        private static void Execute(Plan plan)
        {
            Player player = plan.player;
            PlanetFactory factory = plan.factory;
            PlayerAction_Build actionBuild = player.controller.actionBuild;

            // Take collectors out of the package before dismantling so their slots can hold what comes back.
            int inHand = TakeCollectors(player, plan.item.ID, plan.ReservedFromPackage);

            int dismantled = 0;
            foreach (ExistingCollector collector in plan.dismantle)
            {
                if (actionBuild.DoDismantleObject(collector.objId))
                    dismantled++;
            }

            inHand += TakeCollectors(player, plan.item.ID, plan.NeededCount - inHand);

            int built = 0;
            PrefabDesc desc = plan.item.prefabDesc;
            foreach (Vector3 pos in plan.missingTargets)
            {
                if (inHand <= 0)
                    break;

                Quaternion rot = Maths.SphericalRotation(pos, 0f);
                PrebuildData prebuild = default;
                prebuild.isDestroyed = false;
                prebuild.protoId = (short)plan.item.ID;
                prebuild.modelIndex = (short)desc.modelIndex;
                prebuild.pos = pos;
                prebuild.pos2 = pos;
                prebuild.rot = rot;
                prebuild.rot2 = rot;
                prebuild.InitParametersArray(0);

                int prebuildId = factory.AddPrebuildDataWithComponents(prebuild);
                if (prebuildId <= 0)
                {
                    CollectorDeployerPlugin.Log?.LogWarning($"AddPrebuildDataWithComponents failed at {pos}");
                    continue;
                }

                inHand--;
                factory.BuildFinally(player, prebuildId);
                built++;
            }

            if (inHand > 0)
                player.TryAddItemToPackage(plan.item.ID, inHand, 0, throwTrash: true);

            if (built > 0 || dismantled > 0)
                VFAudio.Create("ui-click-2", null, Vector3.zero, play: true, 7, -1, -1L);

            int missing = plan.NeededCount - built;
            if (missing > 0)
            {
                UIRealtimeTip.Popup(Lang.T(
                    $"轨道采集器不足：已新建 {built} 个，还差 {missing} 个",
                    $"Not enough orbital collectors: built {built}, {missing} missing"), true, 0);
            }
            else
            {
                UIRealtimeTip.Popup(Lang.T(
                    $"已部署 {CollectorCount} 个轨道采集器",
                    $"{CollectorCount} orbital collectors deployed"), false, 0);
            }
            CollectorDeployerPlugin.Log?.LogInfo(
                $"Planet {factory.planet.displayName}: built {built}, dismantled {dismantled}, kept {plan.keptCount}, missing {Math.Max(0, missing)}");
        }

        private static int TakeCollectors(Player player, int itemId, int count)
        {
            if (count <= 0)
                return 0;
            player.package.TakeTailItems(ref itemId, ref count, out _);
            return count;
        }

        // Replays Execute's package changes on a copy, following Player.TryAddItemToPackage (package, then delivery slots, then trash).
        private static Dictionary<int, int> PredictOverflow(Plan plan)
        {
            var overflow = new Dictionary<int, int>();
            if (plan.dismantle.Count == 0)
                return overflow;

            Player player = plan.player;
            StorageComponent package = player.package;
            var sim = new StorageComponent(package.size) { type = package.type };
            Array.Copy(package.grids, sim.grids, package.size);

            int reserveId = plan.item.ID;
            int reserveCount = plan.ReservedFromPackage;
            if (reserveCount > 0)
                sim.TakeTailItems(ref reserveId, ref reserveCount, out _);

            var deliveryFree = new Dictionary<int, int>();
            DeliveryPackage delivery = player.deliveryPackage;
            if (delivery != null && delivery.unlocked)
            {
                for (int i = 0; i < delivery.gridLength; i++)
                {
                    int id = delivery.grids[i].itemId;
                    if (id > 0 && !deliveryFree.ContainsKey(id))
                        deliveryFree[id] = Math.Max(0, delivery.grids[i].stackSizeModified - delivery.grids[i].count);
                }
            }

            void Add(int itemId, int count)
            {
                if (itemId <= 0 || count <= 0 || itemId == SandItemId)
                    return;
                int remain = count - sim.AddItemStacked(itemId, count, 0, out _);
                if (remain > 0 && deliveryFree.TryGetValue(itemId, out int free) && free > 0)
                {
                    int taken = Math.Min(free, remain);
                    deliveryFree[itemId] = free - taken;
                    remain -= taken;
                }
                if (remain > 0)
                    overflow[itemId] = (overflow.TryGetValue(itemId, out int prev) ? prev : 0) + remain;
            }

            PlanetFactory factory = plan.factory;
            foreach (ExistingCollector collector in plan.dismantle)
            {
                if (collector.recoverable)
                    Add(collector.protoId, 1);
                if (!collector.isEntity)
                    continue;

                int stationId = factory.entityPool[collector.objId].stationId;
                if (stationId <= 0 || stationId >= factory.transport.stationCursor)
                    continue;
                StationComponent station = factory.transport.stationPool[stationId];
                if (station == null || station.id != stationId)
                    continue;
                if (station.storage != null)
                {
                    foreach (StationStore store in station.storage)
                        Add(store.itemId, store.count);
                }
                Add(5001, station.idleDroneCount);
                Add(5002, station.idleShipCount);
                Add(1210, station.warperCount);
            }
            return overflow;
        }

        private static string ItemName(int itemId)
        {
            ItemProto proto = LDB.items.Select(itemId);
            if (proto == null)
                return itemId.ToString();
            return string.IsNullOrEmpty(proto.name) ? proto.Name.Translate() : proto.name;
        }

        private static void PopupInsufficient(Plan plan)
        {
            string text = plan.recoverableCount > 0
                ? Lang.T(
                    $"轨道采集器不足：需要 {plan.NeededCount} 个，背包 {plan.packageCount} 个 + 拆除返还 {plan.recoverableCount} 个",
                    $"Not enough orbital collectors: need {plan.NeededCount}, have {plan.packageCount} in inventory + {plan.recoverableCount} from removal")
                : Lang.T(
                    $"轨道采集器不足：需要 {plan.NeededCount} 个，背包中只有 {plan.packageCount} 个",
                    $"Not enough orbital collectors: need {plan.NeededCount}, only {plan.packageCount} in inventory");
            UIRealtimeTip.Popup(text, true, 0);
        }

        private static Plan MakePlan(out string error)
        {
            error = null;
            Player player = GameMain.mainPlayer;
            PlanetData planet = GameMain.localPlanet;
            if (player == null || planet == null || planet.type != EPlanetType.Gas)
            {
                error = Lang.T("只能在气态巨星上使用", "Only available on gas giants");
                return null;
            }

            PlanetFactory factory = player.factory;
            if (factory == null || factory.planet != planet || player.package == null)
            {
                error = Lang.T("星球数据尚未加载", "Planet data is not loaded yet");
                return null;
            }

            ItemProto item = LDB.items.Select(OrbitalCollectorItemId);
            if (item?.prefabDesc == null || !item.prefabDesc.isCollectStation)
            {
                error = Lang.T("找不到轨道采集器数据", "Orbital collector data not found");
                return null;
            }

            if (!CanCollectorWork(planet, item.prefabDesc))
            {
                error = Lang.T("该星球的资源不足以驱动轨道采集器", "This planet cannot power orbital collectors");
                return null;
            }

            List<ExistingCollector> existing = FindExistingCollectors(factory);
            List<Vector3> targets = ChooseTargets(planet, existing);
            if (!TargetsAreFarEnough(targets))
            {
                error = Lang.T(
                    $"星球太小，赤道上放不下 {CollectorCount} 个轨道采集器",
                    $"Planet is too small to fit {CollectorCount} orbital collectors on the equator");
                return null;
            }

            var plan = new Plan
            {
                player = player,
                factory = factory,
                item = item,
                existingCount = existing.Count,
                packageCount = player.package.GetItemCount(item.ID),
            };

            bool[] claimed = new bool[targets.Count];
            foreach (ExistingCollector collector in existing)
            {
                int match = collector.isEntity ? FindUnclaimedMatch(targets, claimed, collector.pos) : -1;
                if (match >= 0)
                {
                    claimed[match] = true;
                    plan.keptCount++;
                    continue;
                }
                plan.dismantle.Add(collector);
                if (collector.recoverable)
                    plan.recoverableCount++;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (!claimed[i])
                    plan.missingTargets.Add(targets[i]);
            }
            return plan;
        }

        private static bool CanCollectorWork(PlanetData planet, PrefabDesc desc)
        {
            if (planet.gasItems == null || planet.gasItems.Length == 0)
                return false;
            double collectPower = desc.stationCollectSpeed * planet.gasTotalHeat;
            if (collectPower == 0.0)
                return false;
            return 1.0 - desc.workEnergyPerTick / (collectPower * (1.0 / 60.0)) > 0.0;
        }

        private static List<ExistingCollector> FindExistingCollectors(PlanetFactory factory)
        {
            var result = new List<ExistingCollector>();

            EntityData[] entityPool = factory.entityPool;
            for (int i = 1; i < factory.entityCursor; i++)
            {
                if (entityPool[i].id != i || !IsCollectorProto(entityPool[i].protoId))
                    continue;
                result.Add(new ExistingCollector
                {
                    objId = i,
                    protoId = entityPool[i].protoId,
                    pos = entityPool[i].pos,
                    recoverable = true,
                    isEntity = true,
                });
            }

            PrebuildData[] prebuildPool = factory.prebuildPool;
            for (int i = 1; i < factory.prebuildCursor; i++)
            {
                if (prebuildPool[i].id != i || !IsCollectorProto(prebuildPool[i].protoId))
                    continue;
                result.Add(new ExistingCollector
                {
                    objId = -i,
                    protoId = prebuildPool[i].protoId,
                    pos = prebuildPool[i].pos,
                    recoverable = prebuildPool[i].itemRequired == 0,
                    isEntity = false,
                });
            }
            return result;
        }

        private static bool IsCollectorProto(int protoId)
        {
            ItemProto proto = LDB.items.Select(protoId);
            return proto?.prefabDesc != null && proto.prefabDesc.isCollectStation;
        }

        // Picks the equator grid phase that keeps the most existing collectors in place.
        private static List<Vector3> ChooseTargets(PlanetData planet, List<ExistingCollector> existing)
        {
            PlanetGrid grid = planet.aux?.mainGrid;
            int segment = grid?.segment ?? (int)(planet.radius / 4f + 0.1f) * 4;
            int gridPoints = PlanetGrid.DetermineLongitudeSegmentCount(0, segment) * 5;
            bool alignedToGrid = gridPoints > 0 && gridPoints % CollectorCount == 0;
            int phaseCount = alignedToGrid ? gridPoints / CollectorCount : 1;

            List<Vector3> best = null;
            int bestMatches = -1;
            for (int phase = 0; phase < phaseCount; phase++)
            {
                List<Vector3> targets = ComputeTargets(planet, grid, gridPoints, alignedToGrid, phase);
                int matches = CountMatches(targets, existing);
                if (matches > bestMatches)
                {
                    best = targets;
                    bestMatches = matches;
                }
            }
            return best;
        }

        private static List<Vector3> ComputeTargets(PlanetData planet, PlanetGrid grid, int gridPoints, bool alignedToGrid, int phase)
        {
            // Matches BuildTool_Click on gas giants: grid snap at realRadius + 0.2, then lifted by 2.5% of realRadius.
            float altitude = planet.realRadius + 0.2f + planet.realRadius * 0.025f;
            int step = alignedToGrid ? gridPoints / CollectorCount : 0;
            var targets = new List<Vector3>(CollectorCount);
            for (int k = 0; k < CollectorCount; k++)
            {
                float lon = alignedToGrid
                    ? (phase + k * step) * (Mathf.PI * 2f) / gridPoints
                    : k * (Mathf.PI * 2f) / CollectorCount;
                Vector3 dir = grid != null ? grid.SnapTo(lon, 0f) : new Vector3(Mathf.Sin(lon), 0f, -Mathf.Cos(lon));
                dir.y = 0f;
                targets.Add(dir.normalized * altitude);
            }
            return targets;
        }

        private static int CountMatches(List<Vector3> targets, List<ExistingCollector> existing)
        {
            bool[] claimed = new bool[targets.Count];
            int matches = 0;
            foreach (ExistingCollector collector in existing)
            {
                if (!collector.isEntity)
                    continue;
                int match = FindUnclaimedMatch(targets, claimed, collector.pos);
                if (match >= 0)
                {
                    claimed[match] = true;
                    matches++;
                }
            }
            return matches;
        }

        private static int FindUnclaimedMatch(List<Vector3> targets, bool[] claimed, Vector3 pos)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (!claimed[i] && (targets[i] - pos).sqrMagnitude < MatchSqrDistance)
                    return i;
            }
            return -1;
        }

        private static bool TargetsAreFarEnough(List<Vector3> targets)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                Vector3 next = targets[(i + 1) % targets.Count];
                if ((targets[i] - next).sqrMagnitude < CollectorMinSqrDistance)
                    return false;
            }
            return true;
        }
    }
}
