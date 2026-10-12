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

        private enum PlanetStatus
        {
            Ok,
            NoPower,
            TooSmall,
            TooMany,
        }

        private struct ExistingCollector
        {
            public int objId;
            public int protoId;
            public Vector3 pos;
            public bool recoverable;
            public bool isEntity;
        }

        private sealed class PlanetPlan
        {
            public PlanetData planet;
            // Null for gas giants that have never had a factory; created when building.
            public PlanetFactory factory;
            public bool isLocal;
            public readonly List<Vector3> missingTargets = new List<Vector3>();
            public readonly List<ExistingCollector> dismantle = new List<ExistingCollector>();
            public int existingCount;
            public int keptCount;
            public int recoverableCount;

            public int NeededCount => missingTargets.Count;
            public bool IsDone => NeededCount == 0 && dismantle.Count == 0;
        }

        private sealed class Batch
        {
            public Player player;
            public ItemProto item;
            public int packageCount;
            public readonly List<PlanetPlan> plans = new List<PlanetPlan>();
            public int doneCount;
            public int tooManyCount;
            public int unusableCount;

            public int NeededCount => plans.Sum(p => p.NeededCount);
            public int RecoverableCount => plans.Sum(p => p.recoverableCount);
            public int DismantleCount => plans.Sum(p => p.dismantle.Count);
            public int AvailableCount => packageCount + RecoverableCount;
            public int ReservedFromPackage => Math.Min(packageCount, NeededCount);
        }

        private static bool _dialogOpen;

        public static void RequestLocal()
        {
            if (_dialogOpen)
                return;

            Batch batch = MakeLocalBatch();
            if (batch == null)
                return;

            Dictionary<int, int> overflow = PredictOverflow(batch);
            if (overflow.Count == 0)
            {
                Execute(batch);
                return;
            }

            string message = Lang.T(
                $"背包空间不足，拆除旧的轨道采集器后以下物品会掉落：\n{FormatItems(overflow)}\n\n是否继续？",
                $"Not enough inventory space. Removing the old orbital collectors will drop:\n{FormatItems(overflow)}\n\nContinue?");
            Confirm(Lang.TipTitle, message, MakeLocalBatch);
        }

        public static void RequestGalaxy()
        {
            if (_dialogOpen)
                return;

            Batch batch = MakeGalaxyBatch();
            if (batch == null)
                return;

            Dictionary<int, int> overflow = PredictOverflow(batch);
            string message = Lang.T(
                $"将在 {batch.plans.Count} 颗气态巨星上部署轨道采集器：新建 {batch.NeededCount} 个，拆除 {batch.DismantleCount} 个不在格点上的。",
                $"Orbital collectors will be deployed on {batch.plans.Count} gas giant(s): {batch.NeededCount} built, {batch.DismantleCount} off-grid removed.");

            var notes = new List<string>();
            if (batch.doneCount > 0)
                notes.Add(Lang.T($"已部署完毕 {batch.doneCount} 颗", $"{batch.doneCount} already done"));
            if (batch.tooManyCount > 0)
                notes.Add(Lang.T($"采集器超过 {CollectorCount} 个，跳过 {batch.tooManyCount} 颗", $"{batch.tooManyCount} skipped (more than {CollectorCount} collectors)"));
            if (batch.unusableCount > 0)
                notes.Add(Lang.T($"无法使用采集器，跳过 {batch.unusableCount} 颗", $"{batch.unusableCount} skipped (collectors can't work there)"));
            if (notes.Count > 0)
                message += "\n" + string.Join(Lang.T("；", "; "), notes);

            if (overflow.Count > 0)
            {
                message += Lang.T(
                    $"\n\n背包空间不足，以下物品会掉落：\n{FormatItems(overflow)}",
                    $"\n\nNot enough inventory space. These items will drop:\n{FormatItems(overflow)}");
            }
            message += Lang.T("\n\n是否继续？", "\n\nContinue?");

            Confirm(Lang.GalaxyTipTitle, message, MakeGalaxyBatch);
        }

        private static void Confirm(string title, string message, Func<Batch> rebuild)
        {
            _dialogOpen = true;
            UIMessageBox.Show(title, message, "否".Translate(), "是".Translate(), UIMessageBox.WARNING,
                () => _dialogOpen = false,
                () =>
                {
                    _dialogOpen = false;
                    Batch fresh = rebuild();
                    if (fresh != null)
                        Execute(fresh);
                });
        }

        // Returns null after showing the reason when nothing should be done.
        private static Batch MakeLocalBatch()
        {
            if (!TryGetContext(out Player player, out ItemProto item))
                return null;

            PlanetData planet = GameMain.localPlanet;
            if (planet == null || planet.type != EPlanetType.Gas)
            {
                UIRealtimeTip.Popup(Lang.T("只能在气态巨星上使用", "Only available on gas giants"), true, 0);
                return null;
            }
            if (player.factory == null || player.factory.planet != planet)
            {
                UIRealtimeTip.Popup(Lang.T("星球数据尚未加载", "Planet data is not loaded yet"), true, 0);
                return null;
            }

            PlanetPlan plan = PlanPlanet(player, planet, item, out PlanetStatus status);
            switch (status)
            {
                case PlanetStatus.NoPower:
                    UIRealtimeTip.Popup(Lang.T("该星球的资源不足以驱动轨道采集器", "This planet cannot power orbital collectors"), true, 0);
                    return null;
                case PlanetStatus.TooSmall:
                    UIRealtimeTip.Popup(Lang.T(
                        $"星球太小，赤道上放不下 {CollectorCount} 个轨道采集器",
                        $"Planet is too small to fit {CollectorCount} orbital collectors on the equator"), true, 0);
                    return null;
                case PlanetStatus.TooMany:
                    UIRealtimeTip.Popup(Lang.T(
                        $"本星球已有 {plan.existingCount} 个轨道采集器，超过 {CollectorCount} 个，不进行部署",
                        $"This planet already has {plan.existingCount} orbital collectors (more than {CollectorCount}); nothing deployed"), true, 0);
                    return null;
            }

            if (plan.IsDone)
            {
                UIRealtimeTip.Popup(Lang.T(
                    $"赤道上已部署 {CollectorCount} 个轨道采集器",
                    $"{CollectorCount} orbital collectors are already deployed on the equator"), false, 0);
                return null;
            }

            var batch = new Batch { player = player, item = item, packageCount = player.package.GetItemCount(item.ID) };
            batch.plans.Add(plan);
            return CheckEnough(batch) ? batch : null;
        }

        private static Batch MakeGalaxyBatch()
        {
            if (!TryGetContext(out Player player, out ItemProto item))
                return null;

            GalaxyData galaxy = GameMain.galaxy;
            if (galaxy?.stars == null)
                return null;

            var batch = new Batch { player = player, item = item, packageCount = player.package.GetItemCount(item.ID) };
            foreach (StarData star in galaxy.stars)
            {
                if (star?.planets == null)
                    continue;
                foreach (PlanetData planet in star.planets)
                {
                    if (planet == null || planet.type != EPlanetType.Gas)
                        continue;

                    PlanetPlan plan = PlanPlanet(player, planet, item, out PlanetStatus status);
                    if (status == PlanetStatus.TooMany)
                        batch.tooManyCount++;
                    else if (status != PlanetStatus.Ok)
                        batch.unusableCount++;
                    else if (plan.IsDone)
                        batch.doneCount++;
                    else
                        batch.plans.Add(plan);
                }
            }

            if (batch.plans.Count == 0)
            {
                UIRealtimeTip.Popup(batch.doneCount > 0
                    ? Lang.T($"全银河 {batch.doneCount} 颗气态巨星均已部署完毕", $"All {batch.doneCount} gas giants in the galaxy are already deployed")
                    : Lang.T("没有可以部署的气态巨星", "No gas giant to deploy on"), false, 0);
                return null;
            }

            VectorLF3 playerPos = player.uPosition;
            batch.plans.Sort((a, b) => (a.planet.uPosition - playerPos).sqrMagnitude.CompareTo((b.planet.uPosition - playerPos).sqrMagnitude));
            return CheckEnough(batch) ? batch : null;
        }

        private static bool TryGetContext(out Player player, out ItemProto item)
        {
            player = GameMain.mainPlayer;
            item = LDB.items.Select(OrbitalCollectorItemId);
            if (player?.package == null || GameMain.data == null)
            {
                UIRealtimeTip.Popup(Lang.T("游戏数据尚未加载", "Game data is not loaded yet"), true, 0);
                return false;
            }
            if (item?.prefabDesc == null || !item.prefabDesc.isCollectStation)
            {
                UIRealtimeTip.Popup(Lang.T("找不到轨道采集器数据", "Orbital collector data not found"), true, 0);
                return false;
            }
            return true;
        }

        private static bool CheckEnough(Batch batch)
        {
            if (batch.AvailableCount >= batch.NeededCount)
                return true;

            int recoverable = batch.RecoverableCount;
            string text = recoverable > 0
                ? Lang.T(
                    $"轨道采集器不足：需要 {batch.NeededCount} 个，背包 {batch.packageCount} 个 + 拆除返还 {recoverable} 个",
                    $"Not enough orbital collectors: need {batch.NeededCount}, have {batch.packageCount} in inventory + {recoverable} from removal")
                : Lang.T(
                    $"轨道采集器不足：需要 {batch.NeededCount} 个，背包中只有 {batch.packageCount} 个",
                    $"Not enough orbital collectors: need {batch.NeededCount}, only {batch.packageCount} in inventory");
            UIRealtimeTip.Popup(text, true, 0);
            return false;
        }

        private static PlanetPlan PlanPlanet(Player player, PlanetData planet, ItemProto item, out PlanetStatus status)
        {
            PlanetFactory factory = planet.factory;
            var plan = new PlanetPlan
            {
                planet = planet,
                factory = factory,
                isLocal = factory != null && factory == player.factory,
            };

            if (!CanCollectorWork(planet, item.prefabDesc))
            {
                status = PlanetStatus.NoPower;
                return plan;
            }

            List<ExistingCollector> existing = factory != null ? FindExistingCollectors(factory) : new List<ExistingCollector>();
            plan.existingCount = existing.Count;
            if (existing.Count > CollectorCount)
            {
                status = PlanetStatus.TooMany;
                return plan;
            }

            List<Vector3> targets = ChooseTargets(planet, existing);
            if (!TargetsAreFarEnough(targets))
            {
                status = PlanetStatus.TooSmall;
                return plan;
            }

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
            status = PlanetStatus.Ok;
            return plan;
        }

        private static void Execute(Batch batch)
        {
            Player player = batch.player;
            ItemProto item = batch.item;

            // Take collectors out of the package before dismantling so their slots can hold what comes back.
            int inHand = TakeCollectors(player, item.ID, batch.ReservedFromPackage);

            int dismantled = 0;
            foreach (PlanetPlan plan in batch.plans)
            {
                foreach (ExistingCollector collector in plan.dismantle)
                {
                    if (Dismantle(player, plan, collector))
                        dismantled++;
                }
            }

            int needed = batch.NeededCount;
            inHand += TakeCollectors(player, item.ID, needed - inHand);

            int built = 0;
            int planetsTouched = 0;
            foreach (PlanetPlan plan in batch.plans)
            {
                if (plan.NeededCount == 0)
                {
                    planetsTouched++;
                    continue;
                }
                if (inHand <= 0)
                    break;

                PlanetFactory factory = plan.factory ?? CreateFactory(plan.planet);
                if (factory == null)
                {
                    CollectorDeployerPlugin.Log?.LogWarning($"Could not create a factory for {plan.planet.displayName}");
                    continue;
                }

                foreach (Vector3 pos in plan.missingTargets)
                {
                    if (inHand <= 0)
                        break;
                    if (!BuildCollector(player, factory, item, pos))
                        continue;
                    inHand--;
                    built++;
                }
                planetsTouched++;
            }

            if (inHand > 0)
                player.TryAddItemToPackage(item.ID, inHand, 0, throwTrash: true);

            if (built > 0 || dismantled > 0)
                VFAudio.Create("ui-click-2", null, Vector3.zero, play: true, 7, -1, -1L);

            int missing = needed - built;
            if (missing > 0)
            {
                UIRealtimeTip.Popup(Lang.T(
                    $"轨道采集器不足：已新建 {built} 个，还差 {missing} 个",
                    $"Not enough orbital collectors: built {built}, {missing} missing"), true, 0);
            }
            else if (batch.plans.Count == 1)
            {
                UIRealtimeTip.Popup(Lang.T(
                    $"已部署 {CollectorCount} 个轨道采集器",
                    $"{CollectorCount} orbital collectors deployed"), false, 0);
            }
            else
            {
                UIRealtimeTip.Popup(Lang.T(
                    $"已在 {planetsTouched} 颗气态巨星上部署轨道采集器，新建 {built} 个",
                    $"Deployed on {planetsTouched} gas giants, {built} orbital collectors built"), false, 0);
            }
            CollectorDeployerPlugin.Log?.LogInfo(
                $"Deploy: {batch.plans.Count} planet(s), built {built}, dismantled {dismantled}, missing {Math.Max(0, missing)}");
        }

        private static int TakeCollectors(Player player, int itemId, int count)
        {
            if (count <= 0)
                return 0;
            player.package.TakeTailItems(ref itemId, ref count, out _);
            return count;
        }

        private static PlanetFactory CreateFactory(PlanetData planet)
        {
            GameData data = GameMain.data;
            if (data.factoryCount >= data.factories.Length)
                return null;
            // Same as EnemyDFHiveSystem.NotifyRelayKilled, which creates factories on planets the player never visited.
            PlanetFactory factory = data.GetOrCreateFactory(planet);
            data.galaxy.astrosFactory[planet.id] = factory;
            return factory;
        }

        private static bool BuildCollector(Player player, PlanetFactory factory, ItemProto item, Vector3 pos)
        {
            Quaternion rot = Maths.SphericalRotation(pos, 0f);
            PrebuildData prebuild = default;
            prebuild.isDestroyed = false;
            prebuild.protoId = (short)item.ID;
            prebuild.modelIndex = (short)item.prefabDesc.modelIndex;
            prebuild.pos = pos;
            prebuild.pos2 = pos;
            prebuild.rot = rot;
            prebuild.rot2 = rot;
            prebuild.InitParametersArray(0);

            int prebuildId = factory.AddPrebuildDataWithComponents(prebuild);
            if (prebuildId <= 0)
            {
                CollectorDeployerPlugin.Log?.LogWarning($"AddPrebuildDataWithComponents failed on {factory.planet.displayName} at {pos}");
                return false;
            }

            // Same as ConstructionSystem.Build for loaded and unloaded factories.
            if (factory.planet.factoryLoaded)
                factory.BuildFinally(player, prebuildId);
            else
                factory.BuildFinally(player, prebuildId, autoRefresh: false, flattenTerrain: false);
            return true;
        }

        private static bool Dismantle(Player player, PlanetPlan plan, ExistingCollector collector)
        {
            if (plan.isLocal)
                return player.controller.actionBuild.DoDismantleObject(collector.objId);

            // Remote objects: return items with objId 0, because TrashSystem.AddTrash looks a
            // non-zero objId up in the local planet's pools.
            PlanetFactory factory = plan.factory;
            if (collector.recoverable)
                ReturnItem(player, collector.protoId, 1, 0);
            if (collector.isEntity && TryGetStation(factory, collector.objId, out StationComponent station))
            {
                for (int i = 0; i < station.storage.Length; i++)
                {
                    ReturnItem(player, station.storage[i].itemId, station.storage[i].count, station.storage[i].inc);
                    station.storage[i].count = 0;
                    station.storage[i].inc = 0;
                }
                ReturnItem(player, 5001, station.idleDroneCount, 0);
                ReturnItem(player, 5002, station.idleShipCount, 0);
                ReturnItem(player, 1210, station.warperCount, 0);
                station.idleDroneCount = 0;
                station.idleShipCount = 0;
                station.warperCount = 0;
            }

            int protoId = collector.protoId;
            factory.DismantleFinally(player, collector.objId, ref protoId);
            return true;
        }

        private static void ReturnItem(Player player, int itemId, int count, int inc)
        {
            if (itemId <= 0 || count <= 0)
                return;
            int upCount = player.TryAddItemToPackage(itemId, count, inc, throwTrash: true);
            UIItemup.Up(itemId, upCount);
        }

        private static bool TryGetStation(PlanetFactory factory, int entityId, out StationComponent station)
        {
            station = null;
            int stationId = factory.entityPool[entityId].stationId;
            if (stationId <= 0 || stationId >= factory.transport.stationCursor)
                return false;
            station = factory.transport.stationPool[stationId];
            return station != null && station.id == stationId && station.storage != null;
        }

        // Replays Execute's package changes on a copy, following Player.TryAddItemToPackage (package, then delivery slots, then trash).
        private static Dictionary<int, int> PredictOverflow(Batch batch)
        {
            var overflow = new Dictionary<int, int>();
            if (batch.DismantleCount == 0)
                return overflow;

            Player player = batch.player;
            StorageComponent package = player.package;
            var sim = new StorageComponent(package.size) { type = package.type };
            Array.Copy(package.grids, sim.grids, package.size);

            int reserveId = batch.item.ID;
            int reserveCount = batch.ReservedFromPackage;
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

            foreach (PlanetPlan plan in batch.plans)
            {
                foreach (ExistingCollector collector in plan.dismantle)
                {
                    if (collector.recoverable)
                        Add(collector.protoId, 1);
                    if (!collector.isEntity || !TryGetStation(plan.factory, collector.objId, out StationComponent station))
                        continue;
                    foreach (StationStore store in station.storage)
                        Add(store.itemId, store.count);
                    Add(5001, station.idleDroneCount);
                    Add(5002, station.idleShipCount);
                    Add(1210, station.warperCount);
                }
            }
            return overflow;
        }

        private static string FormatItems(Dictionary<int, int> items)
        {
            return string.Join(Lang.T("、", ", "), items.Select(kv => $"{ItemName(kv.Key)} ×{kv.Value}"));
        }

        private static string ItemName(int itemId)
        {
            ItemProto proto = LDB.items.Select(itemId);
            if (proto == null)
                return itemId.ToString();
            return string.IsNullOrEmpty(proto.name) ? proto.Name.Translate() : proto.name;
        }

        // Matches BuildTool_Click when gas can power collectors; Genesis Book zeros gasTotalHeat and
        // workEnergyPerTick and patches the build tool to allow collectors on any gas giant (IsSuit).
        private static bool CanCollectorWork(PlanetData planet, PrefabDesc desc)
        {
            if (planet.type != EPlanetType.Gas)
                return false;

            if (desc.workEnergyPerTick <= 0f)
                return true;

            if (planet.gasItems == null || planet.gasItems.Length == 0)
                return false;

            // Runtime still collects at full speed when heat is 0 (PlanetTransport); only the vanilla
            // build preview treats that case as NotEnoughEnergyToWorkCollection.
            if (planet.gasTotalHeat == 0.0)
                return true;

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
                // Planets that are not loaded have no aux grid; PlanetGrid.SnapTo gives the same point for on-grid longitudes.
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
