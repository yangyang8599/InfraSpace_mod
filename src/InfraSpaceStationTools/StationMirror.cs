using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InfraSpaceStationTools
{
    internal static class StationMirror
    {
        private static readonly FieldInfo CurrentBuildingField =
            AccessTools.Field(typeof(BuildingConstructionManager), "currentBuilding");

        private static readonly FieldInfo IsBuildingField =
            AccessTools.Field(typeof(BuildingConstructionManager), "isBuilding");

        private static readonly FieldInfo IsMovingField =
            AccessTools.Field(typeof(BuildingConstructionManager), "isMoving");

        private struct AppliedSwap
        {
            public int Depth;
            public int[] Rails;
            public int[] Roads;
        }

        private struct StationFacing
        {
            public List<int> Rails;
            public List<int> Roads;
        }

        private static readonly Dictionary<BuildingDescription, AppliedSwap> appliedSwaps =
            new Dictionary<BuildingDescription, AppliedSwap>();

        private static readonly Dictionary<long, StationFacing> moveSnapshots =
            new Dictionary<long, StationFacing>();

        private static int cycleFrame = -1;

        private static int modeToggleFrame = -1;

        private static int selectedPort;

        private static long selectedBuildingId;

        private static bool portEditMode;

        private static bool poseFrozen;

        private static Vector3 frozenPosition;

        private static float frozenYaw;

        private static bool armMoveRelease;

        private static bool hadBuildingOnPress;

        private static Vector2 pressMouse;

        private static readonly FieldInfo LeftButtonPressedField =
            AccessTools.Field(typeof(BuildingConstructionManager), "isLeftButtonPressed");

        private static readonly FieldInfo SelectedStationField =
            AccessTools.Field(typeof(StationInfoPanel), "station");

        private static readonly FieldInfo CollectionCenterField =
            AccessTools.Field(typeof(CollectionInfoPanel), "collectionCenter");

        private static readonly FieldInfo DistributionCenterField =
            AccessTools.Field(typeof(DistributionInfoPanel), "distributionCenter");

        private static readonly FieldInfo PermanentSelectionField =
            AccessTools.Field(typeof(BuildingConstructionManager), "permanentlySelectedBuilding");

        private static bool pressStartedOnPanel;

        internal static bool ShowHint { get; private set; }

        internal static Building PlacementTarget { get; private set; }

        internal static int SelectedPort => selectedPort;

        internal static bool PortEditMode => portEditMode;

        internal static bool EditingBuiltStation { get; private set; }

        internal static bool PlacementIsLogistics => IsLogisticsCenter(PlacementTarget);

        internal static Rect PanelRect
        {
            get
            {
                const float width = 520f;
                float height = IsLogisticsCenter(PlacementTarget) ? 460f : 700f;
                float x = Screen.width - width - 28f;
                float y = Mathf.Max(20f, (Screen.height - height) * 0.5f);
                return new Rect(x, y, width, Mathf.Min(height, Screen.height - 40f));
            }
        }

        internal static string PanelTitle()
        {
            if (PortEditMode)
            {
                return "出入口点选";
            }

            ProductionLogic logic = LogicOf(PlacementTarget);
            if (logic is CollectionProductionLogic)
            {
                return "收集中心出入口";
            }

            if (logic is DistributionProductionLogic)
            {
                return "配送中心出入口";
            }

            return "火车站出入口";
        }

        internal static string HintText()
        {
            bool logistics = IsLogisticsCenter(PlacementTarget);
            if (PortEditMode)
            {
                string target = logistics ? "公路口" : "公路口和铁道方向";
                return EditingBuiltStation
                    ? "点击编号切换" + target + "。按 X 退出。"
                    : "点击编号切换" + target + "。按 X 退出后再点击地面放下。";
            }

            return logistics
                ? "按 X 进入点选。轻点建筑只拿起，拖动松手会放下。"
                : "按 X 进入点选。轻点车站只拿起，拖动松手会放下。";
        }

        internal static bool PointerOverHint
        {
            get
            {
                if (!ShowHint)
                {
                    return false;
                }

                Vector2 mouse = Input.mousePosition;
                mouse.y = Screen.height - mouse.y;
                return PanelRect.Contains(mouse);
            }
        }

        internal static void EnsureHint()
        {
            if (GameObject.Find("TrainStationMirrorHint") != null)
            {
                return;
            }

            GameObject hint = new GameObject("TrainStationMirrorHint");
            UnityEngine.Object.DontDestroyOnLoad(hint);
            hint.AddComponent<TrainStationMirrorHint>();
        }

        internal static void HandlePlacementInput(BuildingConstructionManager manager)
        {
            Building building = ResolveTarget(manager);
            bool active = building != null;
            EditingBuiltStation = active && building.IsBuilt();
            ShowHint = active && (portEditMode || !EditingBuiltStation);
            PlacementTarget = active ? building : null;
            if (!active)
            {
                pressStartedOnPanel = false;
                portEditMode = false;
                poseFrozen = false;
                armMoveRelease = false;
                return;
            }

            if (!ShowHint)
            {
                return;
            }

            if (building.GetID() != selectedBuildingId)
            {
                selectedBuildingId = building.GetID();
                selectedPort = 0;
            }

            int portCount = CountRoadPorts(building);
            if (portCount > 0 && cycleFrame != Time.frameCount)
            {
                int step = 0;
                if (Input.GetKeyDown(KeyCode.Period))
                {
                    step = 1;
                }
                else if (Input.GetKeyDown(KeyCode.Comma))
                {
                    step = -1;
                }

                if (step != 0)
                {
                    cycleFrame = Time.frameCount;
                    selectedPort = (selectedPort + step + portCount) % portCount;
                }
            }

            if (portEditMode && !EditingBuiltStation)
            {
                HoldPose(building);
            }
        }

        private static Building ResolveTarget(BuildingConstructionManager manager)
        {
            Building current = ReadCurrent(manager);
            bool placing = (bool)IsBuildingField.GetValue(manager) || (bool)IsMovingField.GetValue(manager);
            if (placing && IsPortBuilding(current))
            {
                return current;
            }

            Building station = ReadSelectedTrainStation();
            if (station != null)
            {
                return station;
            }

            return ReadSelectedLogisticsCenter();
        }

        internal static void Tick()
        {
            BuildingConstructionManager manager = WorldScripts.Inst == null
                ? null
                : WorldScripts.Inst.buildingConstructionManager;
            if (manager == null)
            {
                return;
            }

            PollEditHotkey(manager);
            HandlePlacementInput(manager);
        }

        private static Building ReadSelectedTrainStation()
        {
            StationInfoPanel panel = WorldScripts.Inst == null ? null : WorldScripts.Inst.stationInfoPanel;
            bool panelOpen = panel != null && panel.gameObject.activeInHierarchy;
            if (panelOpen && SelectedStationField != null)
            {
                Building station = SelectedStationField.GetValue(panel) as Building;
                if (IsTrainStation(station))
                {
                    return station;
                }
            }

            if (!panelOpen || PermanentSelectionField == null)
            {
                return null;
            }

            BuildingConstructionManager manager = WorldScripts.Inst.buildingConstructionManager;
            Building selected = manager == null ? null : PermanentSelectionField.GetValue(manager) as Building;
            if (!IsTrainStation(selected))
            {
                return null;
            }

            return selected;
        }

        internal static void NoteClickStart(BuildingConstructionManager manager)
        {
            hadBuildingOnPress = ReadCurrent(manager) != null;
            pressMouse = Input.mousePosition;
        }

        internal static void NoteClickEnd(BuildingConstructionManager manager)
        {
            if (hadBuildingOnPress || !(bool)IsMovingField.GetValue(manager))
            {
                return;
            }

            Building building = ReadCurrent(manager);
            if (IsPortBuilding(building))
            {
                armMoveRelease = true;
            }
        }

        internal static bool ShouldSuppressMoveRelease()
        {
            bool panel = ShowHint && (pressStartedOnPanel || PointerOverHint);
            bool keep = armMoveRelease && Vector2.Distance(pressMouse, Input.mousePosition) < 48f;
            bool suppress = portEditMode || panel || keep;
            armMoveRelease = false;
            return suppress;
        }

        internal static bool TryTogglePortUnderCursor()
        {
            Building building = PlacementTarget;
            if (building == null || Camera.main == null || PointerOverHint)
            {
                return false;
            }

            List<NetEdgeBuildingDescription> edges = building.GetDescription()?.netEdges;
            if (edges == null)
            {
                return false;
            }

            Vector2 mouse = Input.mousePosition;
            mouse.y = Screen.height - mouse.y;
            Vector3 origin = building.GetPosition();
            Quaternion rotation = building.GetRotation();
            int roadIndex = 0;
            int railIndex = 0;
            int hitIndex = -1;
            bool hitRail = false;
            float hitDistance = 44f;
            foreach (NetEdgeBuildingDescription edge in edges)
            {
                bool rail = edge.type == NetworkType.Rail;
                bool road = edge.type == NetworkType.Road;
                if (!rail && !road)
                {
                    continue;
                }

                int index = rail ? railIndex : roadIndex;
                if (rail)
                {
                    railIndex++;
                }
                else
                {
                    roadIndex++;
                }

                Vector3 world = origin + rotation * MarkerPoint(edge);
                Vector3 screen = Camera.main.WorldToScreenPoint(world);
                if (screen.z <= 0.1f)
                {
                    continue;
                }

                Vector2 point = new Vector2(screen.x, Screen.height - screen.y);
                float distance = Vector2.Distance(mouse, point);
                if (distance <= hitDistance)
                {
                    hitDistance = distance;
                    hitIndex = index;
                    hitRail = rail;
                }
            }

            if (hitIndex < 0)
            {
                return false;
            }

            if (hitRail)
            {
                TogglePlacementRail(hitIndex);
            }
            else
            {
                TogglePlacementPort(hitIndex);
            }

            return true;
        }

        internal static Vector3 MarkerPoint(NetEdgeBuildingDescription edge)
        {
            float startDistance = edge.startPos.x * edge.startPos.x + edge.startPos.z * edge.startPos.z;
            float endDistance = edge.endPos.x * edge.endPos.x + edge.endPos.z * edge.endPos.z;
            Vector3 outer = startDistance >= endDistance ? edge.startPos : edge.endPos;
            Vector3 flat = new Vector3(outer.x, 0f, outer.z);
            if (flat.sqrMagnitude > 0.01f)
            {
                outer += flat.normalized * 2.2f;
            }

            outer.y = 3.2f;
            return outer;
        }

        private static void HoldPose(Building building)
        {
            if (!poseFrozen)
            {
                frozenPosition = building.GetPosition();
                frozenYaw = building.GetRotationY();
                poseFrozen = true;
            }

            building.SetPosition(frozenPosition, false);
            building.SetRotation(frozenYaw);
        }

        internal static bool ShouldIgnorePlacementClick()
        {
            return portEditMode || (ShowHint && (pressStartedOnPanel || PointerOverHint));
        }

        internal static void PollEditHotkey(BuildingConstructionManager manager)
        {
            if (ResolveTarget(manager) == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.X) && modeToggleFrame != Time.frameCount)
            {
                modeToggleFrame = Time.frameCount;
                portEditMode = !portEditMode;
                poseFrozen = false;
            }
        }

        internal static void NotePressOnPanel()
        {
            pressStartedOnPanel = true;
        }

        internal static void NotePressOnMap()
        {
            pressStartedOnPanel = false;
        }

        internal static void ClearPlacementPress(BuildingConstructionManager manager)
        {
            pressStartedOnPanel = false;
            LeftButtonPressedField.SetValue(manager, false);
        }

        internal static void TogglePlacementRail(int index)
        {
            if (PlacementTarget == null)
            {
                return;
            }

            ToggleRail(PlacementTarget, index);
        }

        internal static void TogglePlacementPort(int index)
        {
            if (PlacementTarget == null)
            {
                return;
            }

            selectedPort = index;
            ToggleRoadPort(PlacementTarget, index);
        }

        internal static void ContinueFrom(Building placed, Building next)
        {
            if (!IsPortBuilding(next))
            {
                return;
            }

            int[] flippedRails = CopyFlippedRails(placed);
            if (flippedRails.Length > 0)
            {
                SetRailPorts(next, flippedRails);
            }

            int[] flipped = CopyFlippedRoads(placed);
            if (flipped.Length > 0)
            {
                SetRoadPorts(next, flipped);
            }
        }

        internal static Building ReadCurrent(BuildingConstructionManager manager)
        {
            return CurrentBuildingField.GetValue(manager) as Building;
        }

        internal static void Forget(Building building)
        {
            if (building == null)
            {
                return;
            }

            long buildingId = building.GetID();
            moveSnapshots.Remove(buildingId);
            StationToolsMod.Instance?.mirroredTrainStations?.Remove(buildingId);
            StationToolsMod.Instance?.trainStationRailPorts?.Remove(buildingId);
            StationToolsMod.Instance?.trainStationRoadPorts?.Remove(buildingId);
        }

        internal static void RememberBeforeUnbuild(Building building)
        {
            if (!IsPortBuilding(building))
            {
                return;
            }

            moveSnapshots[building.GetID()] = new StationFacing
            {
                Rails = new List<int>(CopyFlippedRails(building)),
                Roads = new List<int>(CopyFlippedRoads(building)),
            };
        }

        internal static void RestoreMoveSnapshot(Building building)
        {
            if (building == null || !moveSnapshots.TryGetValue(building.GetID(), out StationFacing facing))
            {
                return;
            }

            SetRailPorts(building, facing.Rails == null ? Array.Empty<int>() : facing.Rails.ToArray());
            SetRoadPorts(building, facing.Roads == null ? Array.Empty<int>() : facing.Roads.ToArray());
            moveSnapshots.Remove(building.GetID());
        }

        internal static void DiscardMoveSnapshot(Building building)
        {
            if (building != null)
            {
                moveSnapshots.Remove(building.GetID());
            }
        }

        internal static bool IsMirrored(Building building)
        {
            StationToolsMod mod = StationToolsMod.Instance;
            if (building == null || mod?.mirroredTrainStations == null)
            {
                return false;
            }

            return mod.mirroredTrainStations.TryGetValue(building.GetID(), out bool mirrored) && mirrored;
        }

        internal static int[] CopyFlippedRails(Building building)
        {
            StationToolsMod mod = StationToolsMod.Instance;
            if (building == null || mod == null)
            {
                return Array.Empty<int>();
            }

            if (mod.trainStationRailPorts != null &&
                mod.trainStationRailPorts.TryGetValue(building.GetID(), out List<int> ports) &&
                ports != null)
            {
                return ports.ToArray();
            }

            if (!IsMirrored(building))
            {
                return Array.Empty<int>();
            }

            int count = CountEdges(building, NetworkType.Rail);
            int[] all = new int[count];
            for (int index = 0; index < count; index++)
            {
                all[index] = index;
            }

            return all;
        }

        internal static int[] CopyFlippedRoads(Building building)
        {
            StationToolsMod mod = StationToolsMod.Instance;
            if (building == null ||
                mod?.trainStationRoadPorts == null ||
                !mod.trainStationRoadPorts.TryGetValue(building.GetID(), out List<int> ports) ||
                ports == null ||
                ports.Count == 0)
            {
                return Array.Empty<int>();
            }

            return ports.ToArray();
        }

        internal static void BeginSwap(BuildingDescription description, int[] rails, int[] roads)
        {
            if (appliedSwaps.TryGetValue(description, out AppliedSwap applied))
            {
                applied.Depth++;
                appliedSwaps[description] = applied;
                return;
            }

            if (rails != null && rails.Length > 0)
            {
                SwapRailDirections(description, rails);
            }

            if (roads != null && roads.Length > 0)
            {
                SwapRoadEntrances(description, roads);
            }

            appliedSwaps[description] = new AppliedSwap
            {
                Depth = 1,
                Rails = rails ?? Array.Empty<int>(),
                Roads = roads ?? Array.Empty<int>(),
            };
        }

        internal static void EndSwap(BuildingDescription description)
        {
            if (!appliedSwaps.TryGetValue(description, out AppliedSwap applied))
            {
                return;
            }

            applied.Depth--;
            if (applied.Depth > 0)
            {
                appliedSwaps[description] = applied;
                return;
            }

            if (applied.Rails != null && applied.Rails.Length > 0)
            {
                SwapRailDirections(description, applied.Rails);
            }

            if (applied.Roads != null && applied.Roads.Length > 0)
            {
                SwapRoadEntrances(description, applied.Roads);
            }

            appliedSwaps.Remove(description);
        }

        private static void SetRailPorts(Building building, int[] flipped)
        {
            StationToolsMod mod = StationToolsMod.Instance;
            if (building == null || mod == null)
            {
                return;
            }

            if (mod.trainStationRailPorts == null)
            {
                mod.trainStationRailPorts = new Dictionary<long, List<int>>();
            }

            if (mod.mirroredTrainStations == null)
            {
                mod.mirroredTrainStations = new Dictionary<long, bool>();
            }

            long buildingId = building.GetID();
            mod.mirroredTrainStations.Remove(buildingId);
            if (flipped == null || flipped.Length == 0)
            {
                mod.trainStationRailPorts.Remove(buildingId);
                RefreshBuiltStation(building);
                return;
            }

            mod.trainStationRailPorts[buildingId] = new List<int>(flipped);
            RefreshBuiltStation(building);
        }

        private static void SetRoadPorts(Building building, int[] flipped)
        {
            StationToolsMod mod = StationToolsMod.Instance;
            if (building == null || mod == null)
            {
                return;
            }

            if (mod.trainStationRoadPorts == null)
            {
                mod.trainStationRoadPorts = new Dictionary<long, List<int>>();
            }

            long buildingId = building.GetID();
            if (flipped == null || flipped.Length == 0)
            {
                mod.trainStationRoadPorts.Remove(buildingId);
                RefreshBuiltStation(building);
                return;
            }

            mod.trainStationRoadPorts[buildingId] = new List<int>(flipped);
            RefreshBuiltStation(building);
        }

        private static void RefreshBuiltStation(Building building)
        {
            if (building == null || !building.IsBuilt())
            {
                return;
            }

            building.UpdateIntegratedNetEdges(false);
            PathFinder.SetRoadNetworkDirty();
        }

        private static void ToggleRail(Building building, int index)
        {
            int[] flipped = CopyFlippedRails(building);
            List<int> ports = new List<int>(flipped);
            if (ports.Contains(index))
            {
                ports.Remove(index);
            }
            else
            {
                ports.Add(index);
            }

            SetRailPorts(building, ports.ToArray());
        }

        private static void ToggleRoadPort(Building building, int index)
        {
            int[] flipped = CopyFlippedRoads(building);
            List<int> ports = new List<int>(flipped);
            if (ports.Contains(index))
            {
                ports.Remove(index);
            }
            else
            {
                ports.Add(index);
            }

            SetRoadPorts(building, ports.ToArray());
        }

        private static bool IsTrainStation(Building building)
        {
            return building?.GetDescription()?.stationModule == "train";
        }

        private static bool IsLogisticsCenter(Building building)
        {
            ProductionLogic logic = LogicOf(building);
            return logic is CollectionProductionLogic || logic is DistributionProductionLogic;
        }

        private static bool IsPortBuilding(Building building)
        {
            return IsTrainStation(building) || IsLogisticsCenter(building);
        }

        private static ProductionLogic LogicOf(Building building)
        {
            if (building == null)
            {
                return null;
            }

            ProductionLogic logic = building.consumerProducer?.productionLogic;
            if (logic != null)
            {
                return logic;
            }

            try
            {
                return building.GetDescription()?.GetCachedProductionLogic();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Building ReadSelectedLogisticsCenter()
        {
            CollectionInfoPanel collectionPanel = WorldScripts.Inst == null
                ? null
                : WorldScripts.Inst.collectionInfoPanel;
            if (collectionPanel != null &&
                collectionPanel.gameObject.activeInHierarchy &&
                CollectionCenterField != null)
            {
                CollectionProductionLogic collection =
                    CollectionCenterField.GetValue(collectionPanel) as CollectionProductionLogic;
                Building building = collection?.consumerProducer?.building;
                if (IsLogisticsCenter(building))
                {
                    return building;
                }
            }

            DistributionInfoPanel distributionPanel = WorldScripts.Inst == null
                ? null
                : WorldScripts.Inst.distributionInfoPanel;
            if (distributionPanel == null ||
                !distributionPanel.gameObject.activeInHierarchy ||
                DistributionCenterField == null)
            {
                return null;
            }

            DistributionProductionLogic distribution =
                DistributionCenterField.GetValue(distributionPanel) as DistributionProductionLogic;
            Building selected = distribution?.consumerProducer?.building;
            return IsLogisticsCenter(selected) ? selected : null;
        }

        private static void SwapRailDirections(BuildingDescription description, int[] flipped)
        {
            if (description?.netEdges == null || flipped == null || flipped.Length == 0)
            {
                return;
            }

            HashSet<int> selected = new HashSet<int>(flipped);
            int railIndex = 0;
            foreach (NetEdgeBuildingDescription edge in description.netEdges)
            {
                if (edge.type != NetworkType.Rail)
                {
                    continue;
                }

                if (selected.Contains(railIndex))
                {
                    Vector3 start = edge.startPos;
                    edge.startPos = edge.endPos;
                    edge.endPos = start;
                }

                railIndex++;
            }
        }

        private static void SwapRoadEntrances(BuildingDescription description, int[] flipped)
        {
            if (description?.netEdges == null || flipped == null || flipped.Length == 0)
            {
                return;
            }

            HashSet<int> selected = new HashSet<int>(flipped);
            int roadIndex = 0;
            foreach (NetEdgeBuildingDescription edge in description.netEdges)
            {
                if (edge.type != NetworkType.Road)
                {
                    continue;
                }

                if (selected.Contains(roadIndex))
                {
                    Vector3 start = edge.startPos;
                    edge.startPos = edge.endPos;
                    edge.endPos = start;
                    if (edge.trafficType == NetEdgeBuildingDescription.trafficTypeIncoming)
                    {
                        edge.trafficType = NetEdgeBuildingDescription.trafficTypeOutgoing;
                    }
                    else if (edge.trafficType == NetEdgeBuildingDescription.trafficTypeOutgoing)
                    {
                        edge.trafficType = NetEdgeBuildingDescription.trafficTypeIncoming;
                    }
                }

                roadIndex++;
            }
        }

        private static int CountRoadPorts(Building building)
        {
            return CountEdges(building, NetworkType.Road);
        }

        private static int CountEdges(Building building, NetworkType type)
        {
            List<NetEdgeBuildingDescription> edges = building?.GetDescription()?.netEdges;
            if (edges == null)
            {
                return 0;
            }

            int count = 0;
            foreach (NetEdgeBuildingDescription edge in edges)
            {
                if (edge.type == type)
                {
                    count++;
                }
            }

            return count;
        }

    }

    internal sealed class TrainStationMirrorHint : MonoBehaviour, ICameraScrollBlocker
    {
        private static Texture2D panelTexture;

        private static Texture2D buttonTexture;

        private static Texture2D selectedTexture;

        private static Texture2D railTexture;

        private static Texture2D railSelectedTexture;

        private static GUIStyle titleStyle;

        private static GUIStyle bodyStyle;

        private static GUIStyle buttonStyle;

        private static GUIStyle selectedStyle;

        private static GUIStyle numberStyle;

        private static GUIStyle selectedNumberStyle;

        private static Vector2 roadScroll;

        private static int scrolledPort = -1;

        private void Update()
        {
            StationMirror.Tick();
            CameraMovement movement = WorldScripts.Inst == null ? null : WorldScripts.Inst.cameraMovement;
            if (movement != null && !movement.cameraScrollBlockers.Contains(this))
            {
                movement.cameraScrollBlockers.Add(this);
            }
        }

        public bool IsCameraScrollBlocked()
        {
            return StationMirror.PointerOverHint;
        }

        private void OnGUI()
        {
            if (!StationMirror.ShowHint || StationMirror.PlacementTarget == null)
            {
                return;
            }

            EnsureStyles();
            Rect area = StationMirror.PanelRect;
            GUI.depth = -200;
            GUI.DrawTexture(area, panelTexture);
            GUILayout.BeginArea(new Rect(area.x + 28f, area.y + 24f, area.width - 56f, area.height - 48f));
            GUILayout.Label(StationMirror.PanelTitle(), titleStyle);
            GUILayout.Space(8f);
            string hint = StationMirror.HintText();

            GUILayout.Label(hint, bodyStyle);
            GUILayout.Space(12f);
            float scrollHeight = Mathf.Max(220f, area.height - 170f);
            KeepSelectionVisible(scrollHeight);
            roadScroll = GUILayout.BeginScrollView(roadScroll, GUILayout.Height(scrollHeight));
            DrawRailButtons();
            GUILayout.Space(8f);
            DrawRoadButtons();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            DrawPortNumbers();
        }

        private static void KeepSelectionVisible(float scrollHeight)
        {
            if (scrolledPort == StationMirror.SelectedPort)
            {
                return;
            }

            scrolledPort = StationMirror.SelectedPort;
            const float rowHeight = 66f;
            float top = scrolledPort * rowHeight;
            float bottom = top + 58f;
            if (top < roadScroll.y)
            {
                roadScroll.y = top;
            }
            else if (bottom > roadScroll.y + scrollHeight)
            {
                roadScroll.y = bottom - scrollHeight;
            }
        }

        private static void DrawRailButtons()
        {
            Building building = StationMirror.PlacementTarget;
            if (StationMirror.PlacementIsLogistics)
            {
                return;
            }

            List<NetEdgeBuildingDescription> edges = building.GetDescription()?.netEdges;
            if (edges == null)
            {
                return;
            }

            int[] flipped = StationMirror.CopyFlippedRails(building);
            HashSet<int> selected = new HashSet<int>(flipped);
            int railIndex = 0;
            foreach (NetEdgeBuildingDescription edge in edges)
            {
                if (edge.type != NetworkType.Rail)
                {
                    continue;
                }

                string label = "铁道 " + (railIndex + 1) + "    " + (selected.Contains(railIndex) ? "反向" : "正向");
                if (GUILayout.Button(label, selected.Contains(railIndex) ? selectedStyle : buttonStyle, GUILayout.Height(58f)))
                {
                    StationMirror.TogglePlacementRail(railIndex);
                }

                GUILayout.Space(8f);
                railIndex++;
            }
        }

        private static void DrawRoadButtons()
        {
            Building building = StationMirror.PlacementTarget;
            List<NetEdgeBuildingDescription> edges = building.GetDescription()?.netEdges;
            if (edges == null)
            {
                return;
            }

            int[] flipped = StationMirror.CopyFlippedRoads(building);
            HashSet<int> selected = new HashSet<int>(flipped);
            int roadIndex = 0;
            foreach (NetEdgeBuildingDescription edge in edges)
            {
                if (edge.type != NetworkType.Road)
                {
                    continue;
                }

                bool incoming = edge.trafficType == NetEdgeBuildingDescription.trafficTypeIncoming;
                if (selected.Contains(roadIndex))
                {
                    incoming = !incoming;
                }

                bool current = roadIndex == StationMirror.SelectedPort;
                string label = (current ? "▶  " : "    ") + "公路口 " + (roadIndex + 1) + "    " + (incoming ? "入口" : "出口");
                if (GUILayout.Button(label, current ? selectedStyle : buttonStyle, GUILayout.Height(58f)))
                {
                    StationMirror.TogglePlacementPort(roadIndex);
                }

                GUILayout.Space(8f);
                roadIndex++;
            }

            if (roadIndex == 0)
            {
                GUILayout.Label("这个建筑没有可切换的公路口。", bodyStyle);
            }
        }

        private static void DrawPortNumbers()
        {
            Building building = StationMirror.PlacementTarget;
            if (building == null || Camera.main == null)
            {
                return;
            }

            List<NetEdgeBuildingDescription> edges = building.GetDescription()?.netEdges;
            if (edges == null)
            {
                return;
            }

            EnsureStyles();
            Vector3 origin = building.GetPosition();
            Quaternion rotation = building.GetRotation();
            int[] flippedRails = StationMirror.CopyFlippedRails(building);
            HashSet<int> reversedRails = new HashSet<int>(flippedRails);
            int roadIndex = 0;
            int railIndex = 0;
            foreach (NetEdgeBuildingDescription edge in edges)
            {
                bool rail = edge.type == NetworkType.Rail;
                bool road = edge.type == NetworkType.Road;
                if (!rail && !road)
                {
                    continue;
                }

                Vector3 world = origin + rotation * StationMirror.MarkerPoint(edge);
                Vector3 screen = Camera.main.WorldToScreenPoint(world);
                if (screen.z > 0.1f)
                {
                    bool current = road && roadIndex == StationMirror.SelectedPort;
                    bool reversed = rail && reversedRails.Contains(railIndex);
                    float size = StationMirror.PortEditMode ? 72f : (current || reversed ? 64f : 46f);
                    Rect rect = new Rect(
                        screen.x - size * 0.5f,
                        Screen.height - screen.y - size * 0.5f,
                        size,
                        size);
                    Texture2D texture = rail
                        ? (reversed ? railSelectedTexture : railTexture)
                        : (current ? selectedTexture : buttonTexture);
                    GUI.DrawTexture(rect, texture);
                    string number = rail ? "轨" + (railIndex + 1) : (roadIndex + 1).ToString();
                    GUI.Label(rect, number, current || reversed ? selectedNumberStyle : numberStyle);
                }

                if (rail)
                {
                    railIndex++;
                }
                else
                {
                    roadIndex++;
                }
            }
        }

        private static void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            panelTexture = MakeTexture(new Color(0.11f, 0.18f, 0.29f, 0.96f));
            buttonTexture = MakeTexture(new Color(0.18f, 0.30f, 0.46f, 1f));
            selectedTexture = MakeTexture(new Color(0.28f, 0.48f, 0.72f, 1f));
            railTexture = MakeTexture(new Color(0.45f, 0.28f, 0.12f, 1f));
            railSelectedTexture = MakeTexture(new Color(0.72f, 0.42f, 0.14f, 1f));
            Font font = GUI.skin.label.font;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = 24,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                normal = { textColor = new Color(0.86f, 0.91f, 0.96f, 1f) }
            };
            buttonStyle = MakeButtonStyle(font, buttonTexture);
            selectedStyle = MakeButtonStyle(font, selectedTexture);
            numberStyle = MakeNumberStyle(font, 28);
            selectedNumberStyle = MakeNumberStyle(font, 36);
        }

        private static GUIStyle MakeNumberStyle(Font font, int size)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = size,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
        }

        private static GUIStyle MakeButtonStyle(Font font, Texture2D background)
        {
            return new GUIStyle(GUI.skin.button)
            {
                font = font,
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(22, 18, 8, 8),
                normal = { background = background, textColor = Color.white },
                hover = { background = background, textColor = Color.white },
                active = { background = background, textColor = Color.white }
            };
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }

    [HarmonyPatch(typeof(BuildingConstructionManager), nameof(BuildingConstructionManager.HandleLeftMouseClick))]
    internal static class TrainStationMirrorClickPatch
    {
        private static bool Prefix(BuildingConstructionManager __instance, ref bool __result)
        {
            StationMirror.PollEditHotkey(__instance);
            StationMirror.NoteClickStart(__instance);
            if (StationMirror.PortEditMode)
            {
                if (!StationMirror.PointerOverHint)
                {
                    StationMirror.TryTogglePortUnderCursor();
                }

                StationMirror.NotePressOnPanel();
                __result = true;
                return false;
            }

            if (!StationMirror.PointerOverHint)
            {
                StationMirror.NotePressOnMap();
                return true;
            }

            StationMirror.NotePressOnPanel();
            __result = true;
            return false;
        }

        private static void Postfix(BuildingConstructionManager __instance)
        {
            StationMirror.NoteClickEnd(__instance);
        }
    }

    [HarmonyPatch(typeof(BuildingConstructionManager), "HandleLeftMouseRelease")]
    internal static class TrainStationMirrorReleasePatch
    {
        private static bool Prefix(BuildingConstructionManager __instance, ref bool __result)
        {
            StationMirror.PollEditHotkey(__instance);
            if (!StationMirror.ShouldSuppressMoveRelease())
            {
                return true;
            }

            StationMirror.ClearPlacementPress(__instance);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BuildingConstructionManager), "TryPlace")]
    internal static class TrainStationMirrorPlacePatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!StationMirror.ShouldIgnorePlacementClick())
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.UpdateIntegratedNetEdges))]
    internal static class TrainStationMirrorEdgesPatch
    {
        private static void Prefix(Building __instance, bool wasBuilt, ref bool __state)
        {
            __state = false;
            if (wasBuilt || !__instance.IsBuilt())
            {
                return;
            }

            StationMirror.DiscardMoveSnapshot(__instance);
            int[] rails = StationMirror.CopyFlippedRails(__instance);
            int[] roads = StationMirror.CopyFlippedRoads(__instance);
            if (rails.Length == 0 && roads.Length == 0)
            {
                return;
            }

            BuildingDescription description = __instance.GetDescription();
            if (description?.netEdges == null)
            {
                return;
            }

            StationMirror.BeginSwap(description, rails, roads);
            __state = true;
        }

        private static Exception Finalizer(Building __instance, bool __state, Exception __exception)
        {
            if (__state)
            {
                StationMirror.EndSwap(__instance.GetDescription());
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(BuildingConstructionManager), "CustomUpdate")]
    internal static class TrainStationMirrorInputPatch
    {
        private static void Prefix(BuildingConstructionManager __instance)
        {
            StationMirror.PollEditHotkey(__instance);
        }

        private static void Postfix(BuildingConstructionManager __instance)
        {
            StationMirror.HandlePlacementInput(__instance);
        }
    }

    [HarmonyPatch(typeof(BuildingConstructionManager), "SetCurrentBuilding")]
    internal static class TrainStationMirrorContinuePatch
    {
        private static void Postfix(BuildingConstructionManager __instance, Building previousBuilding)
        {
            StationMirror.ContinueFrom(previousBuilding, StationMirror.ReadCurrent(__instance));
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.Destroy))]
    internal static class TrainStationMirrorForgetPatch
    {
        private static void Postfix(Building __instance)
        {
            StationMirror.Forget(__instance);
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.SetIsBuilt))]
    internal static class TrainStationMirrorUnbuildPatch
    {
        private static void Prefix(Building __instance, bool isBuilt)
        {
            if (!isBuilt && __instance.IsBuilt())
            {
                StationMirror.RememberBeforeUnbuild(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(BuildingConstructionManager), "CancelMove")]
    internal static class TrainStationMirrorCancelMovePatch
    {
        private static void Prefix(BuildingConstructionManager __instance)
        {
            StationMirror.RestoreMoveSnapshot(StationMirror.ReadCurrent(__instance));
        }
    }
}
