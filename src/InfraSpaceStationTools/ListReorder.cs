using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InfraSpaceStationTools
{
    [HarmonyPatch(typeof(TrainLineItem), nameof(TrainLineItem.SetTrainLine))]
    internal static class TrainLineReorderAttach
    {
        private static void Postfix(TrainLineItem __instance)
        {
            ListReorderDrag.Ensure(__instance, ListReorderDrag.Kind.Lines);
        }
    }

    [HarmonyPatch(typeof(TrainStationItem), nameof(TrainStationItem.SetTrainStation))]
    internal static class TrainStationReorderAttach
    {
        private static void Postfix(TrainStationItem __instance, bool isReachable)
        {
            ListReorderDrag.Ensure(__instance, ListReorderDrag.Kind.Stations);
            StationRowSelection.Apply(__instance, isReachable);
        }
    }

    [HarmonyPatch(typeof(TrainLinePanel), "SyncFromData")]
    internal static class TrainLineSyncDuringReorder
    {
        private static bool Prefix()
        {
            return !ListReorderDrag.Busy;
        }
    }

    internal sealed class ListReorderDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        internal enum Kind
        {
            Lines,
            Stations,
        }

        private static readonly FieldInfo LineItemsField =
            AccessTools.Field(typeof(TrainLinePanel), "trainLineItems");

        private static readonly FieldInfo StationItemsField =
            AccessTools.Field(typeof(TrainLinePanel), "trainStationItems");

        private static readonly FieldInfo SelectedLineField =
            AccessTools.Field(typeof(TrainLinePanel), "selectedTrainLine");

        private static readonly FieldInfo StationField =
            AccessTools.Field(typeof(TrainStationItem), "trainStation");

        private static readonly MethodInfo ShowLineMethod =
            AccessTools.Method(typeof(TrainLinePanel), "ShowTrainLine");

        private static ListReorderDrag cursorOwner;

        private Kind kind;

        private bool dragging;

        private Button removeButton;

        internal static bool Busy { get; private set; }

        internal static void Ensure(Component row, Kind kind)
        {
            if (row == null)
            {
                return;
            }

            ListReorderDrag drag = row.GetComponent<ListReorderDrag>();
            if (drag == null)
            {
                drag = row.gameObject.AddComponent<ListReorderDrag>();
            }

            drag.kind = kind;
            drag.removeButton = RemoveButton(row);
            Graphic[] graphics = row.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null ||
                    graphic.gameObject == row.gameObject ||
                    drag.IsRemoveControl(graphic.transform))
                {
                    continue;
                }

                DragForwarder forwarder = graphic.GetComponent<DragForwarder>();
                if (forwarder == null)
                {
                    forwarder = graphic.gameObject.AddComponent<DragForwarder>();
                }

                forwarder.Target = drag;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = eventData != null && !IsRemoveControl(eventData.pointerPress == null ? null : eventData.pointerPress.transform);
            Busy = dragging;
            if (dragging)
            {
                ShowMoveCursor();
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || eventData == null || transform.parent == null)
            {
                return;
            }

            int index = transform.GetSiblingIndex();
            int previous = ActiveSibling(index, -1);
            int next = ActiveSibling(index, 1);
            float pointerY = PointerY(eventData);
            if (previous >= 0 && pointerY > Midpoint(index, previous))
            {
                MoveTo(previous);
                return;
            }

            if (next >= 0 && pointerY < Midpoint(index, next))
            {
                MoveTo(next);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging)
            {
                Busy = false;
                return;
            }

            dragging = false;
            RestoreGameCursor();
            try
            {
                if (eventData != null)
                {
                    eventData.eligibleForClick = false;
                }

                if (kind == Kind.Lines)
                {
                    CommitLines();
                }
                else
                {
                    CommitStations();
                }
            }
            finally
            {
                Busy = false;
            }
        }

        private void OnDisable()
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            Busy = false;
            RestoreGameCursor();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (kind != Kind.Stations || eventData == null || !eventData.eligibleForClick)
            {
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (IsRemoveControl(eventData.pointerPress == null ? null : eventData.pointerPress.transform))
            {
                return;
            }

            StationRowSelection.Select(GetComponent<TrainStationItem>());
        }

        private void ShowMoveCursor()
        {
            cursorOwner = this;
            Texture2D texture = Resources.Load<Texture2D>("UI/cursor_move");
            if (texture == null)
            {
                return;
            }

            Cursor.SetCursor(texture, new Vector2(texture.width * 0.5f, texture.height * 0.5f), CursorMode.Auto);
        }

        private void RestoreGameCursor()
        {
            if (cursorOwner != this)
            {
                return;
            }

            cursorOwner = null;
            Cursor.SetCursor(Resources.Load<Texture2D>("UI/cursor_default"), Vector2.zero, CursorMode.Auto);
        }

        private void MoveTo(int sibling)
        {
            transform.SetSiblingIndex(sibling);
            RectTransform parent = transform.parent as RectTransform;
            if (parent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            }
        }

        private int ActiveSibling(int start, int direction)
        {
            Transform parent = transform.parent;
            int index = start + direction;
            while (index >= 0 && index < parent.childCount)
            {
                if (parent.GetChild(index).gameObject.activeInHierarchy)
                {
                    return index;
                }

                index += direction;
            }

            return -1;
        }

        private float Midpoint(int index, int other)
        {
            Transform parent = transform.parent;
            return (parent.GetChild(index).position.y + parent.GetChild(other).position.y) * 0.5f;
        }

        private float PointerY(PointerEventData eventData)
        {
            RectTransform parent = transform.parent as RectTransform;
            if (parent == null)
            {
                return eventData.position.y;
            }

            Vector3 world;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parent,
                eventData.position,
                eventData.pressEventCamera,
                out world))
            {
                return eventData.position.y;
            }

            return world.y;
        }

        private void CommitLines()
        {
            TrainLinePanel panel = WorldScripts.Inst == null ? null : WorldScripts.Inst.trainLinePanel;
            TrainLineManager manager = WorldScripts.Inst == null ? null : WorldScripts.Inst.trainLineManager;
            if (panel == null || manager == null || manager.lines == null || LineItemsField == null)
            {
                return;
            }

            List<TrainLineItem> rows = Rows<TrainLineItem>(panel.trainLineItemTemplate);
            List<TrainLine> order = new List<TrainLine>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].gameObject.activeInHierarchy)
                {
                    continue;
                }

                TrainLine line = rows[i].GetTrainLine();
                if (line != null)
                {
                    order.Add(line);
                }
            }

            if (order.Count == 0 || Same(manager.lines, order))
            {
                return;
            }

            manager.lines.Clear();
            manager.lines.AddRange(order);
            List<TrainLineItem> stored = LineItemsField.GetValue(panel) as List<TrainLineItem>;
            if (stored != null)
            {
                stored.Clear();
                stored.AddRange(rows);
            }

            Reselect(panel, Selected(panel));
        }

        private void CommitStations()
        {
            TrainLinePanel panel = WorldScripts.Inst == null ? null : WorldScripts.Inst.trainLinePanel;
            if (panel == null || StationField == null || StationItemsField == null || ShowLineMethod == null)
            {
                return;
            }

            TrainLine line = Selected(panel);
            if (line == null || line.trainStations == null)
            {
                return;
            }

            List<TrainStationItem> rows = Rows<TrainStationItem>(panel.trainStationItemTemplate);
            List<Building> order = new List<Building>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].gameObject.activeInHierarchy)
                {
                    continue;
                }

                Building station = StationField.GetValue(rows[i]) as Building;
                if (station != null)
                {
                    order.Add(station);
                }
            }

            if (order.Count == 0 || Same(line.trainStations, order))
            {
                return;
            }

            List<Train> trains = line.trains == null ? new List<Train>() : new List<Train>(line.trains);
            List<Building> targets = new List<Building>(trains.Count);
            for (int i = 0; i < trains.Count; i++)
            {
                targets.Add(trains[i] == null ? null : trains[i].GetTargetStation());
            }

            line.trainStations.Clear();
            line.trainStations.AddRange(order);
            for (int i = 0; i < trains.Count; i++)
            {
                if (trains[i] == null)
                {
                    continue;
                }

                int index = targets[i] == null ? 0 : line.trainStations.IndexOf(targets[i]);
                trains[i].SetLineTargetStation(index < 0 ? 0 : index);
            }

            line.UpdateAvailableRailsByStation();
            PathFinder.SetRoadNetworkDirty();
            if (WorldScripts.Inst.railPathfinder != null)
            {
                WorldScripts.Inst.railPathfinder.SetNetworkDirty();
            }

            List<TrainStationItem> stored = StationItemsField.GetValue(panel) as List<TrainStationItem>;
            if (stored != null)
            {
                stored.Clear();
                stored.AddRange(rows);
            }

            ShowLineMethod.Invoke(panel, new object[] { line });
        }

        private List<T> Rows<T>(T template) where T : Component
        {
            List<T> rows = new List<T>();
            Transform parent = transform.parent;
            if (parent == null)
            {
                return rows;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                T row = parent.GetChild(i).GetComponent<T>();
                if (row != null && row != template)
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        private static bool Same<T>(List<T> current, List<T> order) where T : class
        {
            if (current.Count != order.Count)
            {
                return false;
            }

            for (int i = 0; i < current.Count; i++)
            {
                if (current[i] != order[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static TrainLine Selected(TrainLinePanel panel)
        {
            return SelectedLineField == null ? null : SelectedLineField.GetValue(panel) as TrainLine;
        }

        private static void Reselect(TrainLinePanel panel, TrainLine line)
        {
            if (panel.trainLinesButtonGroup == null)
            {
                return;
            }

            panel.trainLinesButtonGroup.ResetButtons(false);
            if (line == null || LineItemsField == null)
            {
                return;
            }

            List<TrainLineItem> items = LineItemsField.GetValue(panel) as List<TrainLineItem>;
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].GetTrainLine() == line)
                {
                    panel.trainLinesButtonGroup.SetIsPressed(i, true, false);
                    return;
                }
            }
        }

        private bool IsRemoveControl(Transform target)
        {
            Button remove = removeButton;
            if (target == null || remove == null)
            {
                return false;
            }

            return target == remove.transform || target.IsChildOf(remove.transform);
        }

        private static Button RemoveButton(Component row)
        {
            TrainLineItem line = row.GetComponent<TrainLineItem>();
            if (line != null)
            {
                return line.removeButton;
            }

            TrainStationItem station = row.GetComponent<TrainStationItem>();
            return station == null ? null : station.removeButton;
        }
    }

    internal sealed class DragForwarder : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        internal ListReorderDrag Target;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Target != null)
            {
                Target.OnBeginDrag(eventData);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Target != null)
            {
                Target.OnDrag(eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Target != null)
            {
                Target.OnEndDrag(eventData);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Target != null)
            {
                Target.OnPointerClick(eventData);
            }
        }
    }

    internal static class StationRowSelection
    {
        private static readonly FieldInfo LineField =
            AccessTools.Field(typeof(TrainStationItem), "trainLine");

        private static readonly FieldInfo StationField =
            AccessTools.Field(typeof(TrainStationItem), "trainStation");

        private static Sprite pressedSprite;

        private static TrainLine selectedLine;

        private static Building selectedStation;

        internal static void Select(TrainStationItem item)
        {
            if (item == null)
            {
                return;
            }

            TrainLine line = LineField.GetValue(item) as TrainLine;
            Building station = StationField.GetValue(item) as Building;
            if (line == null || station == null)
            {
                return;
            }

            selectedLine = line;
            selectedStation = station;
            Refresh(item.transform.parent);
        }

        internal static void Apply(TrainStationItem item, bool reachable)
        {
            if (item == null || item.backgroundImage == null)
            {
                return;
            }

            StationVisual visual = item.GetComponent<StationVisual>();
            if (visual == null)
            {
                visual = item.gameObject.AddComponent<StationVisual>();
            }

            visual.Reachable = reachable;
            Sprite pressed = PressedSprite(item);
            if (reachable && item.backgroundImage.sprite != pressed)
            {
                visual.DefaultSprite = item.backgroundImage.sprite;
            }

            Paint(item, visual);
        }

        private static void Refresh(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                TrainStationItem row = parent.GetChild(i).GetComponent<TrainStationItem>();
                if (row == null || !row.gameObject.activeInHierarchy)
                {
                    continue;
                }

                StationVisual visual = row.GetComponent<StationVisual>();
                if (visual != null)
                {
                    Paint(row, visual);
                }
            }
        }

        private static void Paint(TrainStationItem item, StationVisual visual)
        {
            Image image = item.backgroundImage;
            if (image == null)
            {
                return;
            }

            bool selected = IsSelected(item);
            Sprite pressed = PressedSprite(item);
            bool sameArt = pressed != null && pressed == visual.DefaultSprite;
            if (selected && pressed != null && !sameArt)
            {
                image.sprite = pressed;
            }
            else if (visual.Reachable && visual.DefaultSprite != null)
            {
                image.sprite = visual.DefaultSprite;
            }
            else if (!visual.Reachable && item.redSprite != null)
            {
                image.sprite = item.redSprite;
            }

            image.color = selected && (pressed == null || sameArt)
                ? new Color(0.62f, 0.78f, 0.95f, 1f)
                : Color.white;
        }

        private static bool IsSelected(TrainStationItem item)
        {
            TrainLine line = LineField.GetValue(item) as TrainLine;
            Building station = StationField.GetValue(item) as Building;
            return line != null && station != null && line == selectedLine && station == selectedStation;
        }

        private static Sprite PressedSprite(TrainStationItem item)
        {
            if (pressedSprite != null)
            {
                return pressedSprite;
            }

            if (item == null)
            {
                return null;
            }

            TrainLinePanel panel = item.GetComponentInParent<TrainLinePanel>();
            if (panel == null)
            {
                return null;
            }

            TrainLineItem lineItem = panel.GetComponentInChildren<TrainLineItem>(true);
            ToggleButton toggle = lineItem == null ? null : lineItem.GetComponent<ToggleButton>();
            if (toggle == null || toggle.buttonDown == null)
            {
                return null;
            }

            pressedSprite = toggle.buttonDown;
            return pressedSprite;
        }

        private sealed class StationVisual : MonoBehaviour
        {
            internal bool Reachable;

            internal Sprite DefaultSprite;
        }
    }
}
