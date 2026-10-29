using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace InfraSpaceMultiResourceLogistics
{
    // 开放视图里，配对中心无路，或最近 1 分钟运入低于运出的 90% 且库存低于目标容量的 50% 时，在建筑头顶显示警示。
    internal static class CenterHeadWarnings
    {
        private const float WindowSeconds = 60f;

        private const string WarningMarkup = "<color=#DB2924>";

        private const string Marker = "\u0001multi-logistics";

        private const string RoadSprite = "UI/DashboardIcons/road";

        private static readonly FieldInfo FloatingTextField = AccessTools.Field(typeof(Building), "floatingText");

        private static readonly FieldInfo ActiveField = AccessTools.Field(typeof(FloatingText), "shouldBeActive");

        private static readonly FieldInfo TextField = AccessTools.Field(typeof(FloatingText), "textCached");

        private static readonly object Gate = new object();

        private static readonly Dictionary<long, Dictionary<string, FlowSeries>> flows =
            new Dictionary<long, Dictionary<string, FlowSeries>>();

        private static readonly Dictionary<long, HeadStatus> statuses = new Dictionary<long, HeadStatus>();

        private struct FlowSample
        {
            public float Time;
            public int Input;
            public int Output;
        }

        private sealed class FlowSeries
        {
            public int LastStorage = int.MinValue;
            public float Started;
            public readonly List<FlowSample> Samples = new List<FlowSample>();
        }

        private struct HeadStatus
        {
            public string Sprite;
            public string Tip;
        }

        private struct DrainLine
        {
            public Resource Resource;
            public int Input;
            public int Output;
        }

        internal static void Observe(
            ConsumerProducer consumer,
            IReadOnlyList<Resource> resources,
            List<Reference<Building>> partners,
            bool selfIsCollection)
        {
            Building self = consumer?.building;
            var simulator = Old.GetSimulator();
            if (self == null || !self.IsValidAndBuilt() || simulator == null)
            {
                return;
            }

            float now = simulator.simulationTime;
            string unreachable = DescribeUnreachable(self, partners, selfIsCollection);
            List<DrainLine> drains = UpdateFlow(self.GetID(), consumer, resources, now);
            string tip = Combine(unreachable, drains);
            if (string.IsNullOrEmpty(tip))
            {
                lock (Gate)
                {
                    statuses.Remove(self.GetID());
                }

                return;
            }

            HeadStatus status = new HeadStatus
            {
                Sprite = unreachable != null ? RoadSprite : SpriteOf(drains),
                Tip = tip
            };
            lock (Gate)
            {
                statuses[self.GetID()] = status;
            }
        }

        internal static void ReadMinuteFlow(long buildingId, string resourceName, out int input, out int output)
        {
            TryFlow(buildingId, resourceName, out input, out output, out _);
        }

        private static bool TryFlow(
            long buildingId,
            string resourceName,
            out int input,
            out int output,
            out bool ready)
        {
            input = 0;
            output = 0;
            ready = false;
            if (string.IsNullOrEmpty(resourceName))
            {
                return false;
            }

            var simulator = Old.GetSimulator();
            if (simulator == null)
            {
                return false;
            }

            float now = simulator.simulationTime;
            lock (Gate)
            {
                if (!flows.TryGetValue(buildingId, out Dictionary<string, FlowSeries> seriesByResource) ||
                    !seriesByResource.TryGetValue(resourceName, out FlowSeries series))
                {
                    return false;
                }

                ready = now - series.Started >= WindowSeconds;
                for (int i = 0; i < series.Samples.Count; i++)
                {
                    FlowSample sample = series.Samples[i];
                    if (now - sample.Time > WindowSeconds)
                    {
                        continue;
                    }

                    input += sample.Input;
                    output += sample.Output;
                }
            }

            return true;
        }

        private static int CapacityOf(ConsumerProducer consumer, Resource resource)
        {
            ProductionLogic logic = consumer?.productionLogic;
            if (logic is CollectionProductionLogic collection)
            {
                return CenterState.GetCapacity(collection, resource);
            }

            if (logic is DistributionProductionLogic distribution)
            {
                return CenterState.GetCapacity(distribution, resource);
            }

            return 0;
        }

        internal static int StockPercent(Building building, Resource resource)
        {
            ConsumerProducer consumer = building?.consumerProducer;
            int capacity = CapacityOf(consumer, resource);
            if (capacity <= 0 || resource == null)
            {
                return 0;
            }

            int percent = Mathf.RoundToInt(StorageOf(consumer, resource) * 100f / capacity);
            return Mathf.Clamp(percent, 0, 999);
        }

        internal static string HoverBody(Building building)
        {
            ProductionLogic logic = building?.consumerProducer?.productionLogic;
            IReadOnlyList<Resource> resources;
            if (logic is CollectionProductionLogic collection)
            {
                resources = CenterState.GetResources(collection);
            }
            else if (logic is DistributionProductionLogic distribution)
            {
                resources = CenterState.GetResources(distribution);
            }
            else
            {
                return null;
            }

            if (resources == null || resources.Count == 0)
            {
                return "没有选择物资";
            }

            string[] left = new string[resources.Count];
            int[] input = new int[resources.Count];
            int[] output = new int[resources.Count];
            bool[] warned = new bool[resources.Count];
            int count = 0;
            long buildingId = building.GetID();
            for (int i = 0; i < resources.Count; i++)
            {
                Resource resource = resources[i];
                if (resource == null || string.IsNullOrEmpty(resource.name))
                {
                    continue;
                }

                ReadMinuteFlow(buildingId, resource.name, out int inflow, out int outflow);
                left[count] = NameWithWindow(resource.GetNameInGame(), false);
                input[count] = inflow;
                output[count] = outflow;
                warned[count] = IsResourceWarned(building, resource);
                count++;
            }

            if (count == 0)
            {
                return "没有选择物资";
            }

            return JoinRows(left, input, output, warned, count, true);
        }

        internal static bool IsResourceWarned(Building building, Resource resource)
        {
            ConsumerProducer consumer = building?.consumerProducer;
            if (consumer == null || resource == null || string.IsNullOrEmpty(resource.name))
            {
                return false;
            }

            if (!TryFlow(building.GetID(), resource.name, out int input, out int output, out bool ready) ||
                !ready ||
                !IsShort(input, output))
            {
                return false;
            }

            return IsLowStock(StorageOf(consumer, resource), CapacityOf(consumer, resource));
        }

        private static bool IsShort(int input, int output)
        {
            return output > 0 && input * 10 < output * 9;
        }

        private static bool IsLowStock(int stock, int capacity)
        {
            return capacity > 0 && stock * 2 < capacity;
        }

        internal static string RateText(int input, int output)
        {
            PerMinute(input, output, out int perMinuteIn, out int perMinuteOut);
            return string.Format("入{0,3}/m,出{1,3}/m", perMinuteIn, perMinuteOut);
        }

        internal static void PerMinute(int input, int output, out int perMinuteIn, out int perMinuteOut)
        {
            perMinuteIn = Mathf.RoundToInt(input * 60f / WindowSeconds);
            perMinuteOut = Mathf.RoundToInt(output * 60f / WindowSeconds);
        }

        internal static string NameWithWindow(string name, bool near)
        {
            int minutes = Mathf.Max(1, Mathf.RoundToInt(WindowSeconds / 60f));
            return near ? name + "(近" + minutes + "m)" : name + "(" + minutes + "m)";
        }

        internal static void Show(Building building)
        {
            if (building == null || FloatingTextField == null || ActiveField == null || TextField == null)
            {
                return;
            }

            ProductionLogic logic = building.consumerProducer?.productionLogic;
            if (!(logic is CollectionProductionLogic) && !(logic is DistributionProductionLogic))
            {
                return;
            }

            if (WorldScripts.Inst?.visualizationManager == null ||
                WorldScripts.Inst.visualizationManager.GetVisualTypeToDisplayCached() != VisualType.None)
            {
                return;
            }

            FloatingText floating = FloatingTextField.GetValue(building) as FloatingText;
            if (floating == null)
            {
                return;
            }

            bool active = ActiveField.GetValue(floating) is bool shown && shown;
            bool owned = (TextField.GetValue(floating) as string) == Marker;
            HeadStatus status;
            bool hasStatus;
            lock (Gate)
            {
                hasStatus = statuses.TryGetValue(building.GetID(), out status);
            }

            if (!hasStatus || string.IsNullOrEmpty(status.Sprite))
            {
                if (owned)
                {
                    floating.Hide();
                }

                return;
            }

            if (active && !owned)
            {
                return;
            }

            floating.Show(Marker, status.Sprite, building.GetPosition());
            floating.tooltip?.Init("物流中心", status.Tip);
        }

        private static string DescribeUnreachable(
            Building self,
            List<Reference<Building>> partners,
            bool selfIsCollection)
        {
            if (partners == null || partners.Count == 0)
            {
                return null;
            }

            StringBuilder text = null;
            Building pending = null;
            for (int i = 0; i < partners.Count; i++)
            {
                Building other = partners[i]?.Get();
                if (other == null || !other.IsValidAndBuilt())
                {
                    continue;
                }

                Building source = selfIsCollection ? self : other;
                Building destination = selfIsCollection ? other : self;
                if (!MultiResourceLogisticsEngine.TryGetTruckRoad(source, destination, out bool reachable))
                {
                    if (pending == null)
                    {
                        pending = other;
                    }

                    continue;
                }

                if (reachable)
                {
                    continue;
                }

                text = AppendRoute(text, source, destination);
            }

            if (pending != null)
            {
                Building source = selfIsCollection ? self : pending;
                Building destination = selfIsCollection ? pending : self;
                if (MultiResourceLogisticsEngine.TruckRouteBlocked(source, destination))
                {
                    text = AppendRoute(text, source, destination);
                }
            }

            return text?.ToString();
        }

        private static StringBuilder AppendRoute(StringBuilder text, Building source, Building destination)
        {
            if (text == null)
            {
                text = new StringBuilder();
            }
            else
            {
                text.Append('\n');
            }

            text.Append("货车无法从 ");
            text.Append(TransitTip.BuildingName(source));
            text.Append(" 开到 ");
            text.Append(TransitTip.BuildingName(destination));
            return text;
        }

        private static List<DrainLine> UpdateFlow(
            long buildingId,
            ConsumerProducer consumer,
            IReadOnlyList<Resource> resources,
            float now)
        {
            List<DrainLine> drains = null;
            lock (Gate)
            {
                if (!flows.TryGetValue(buildingId, out Dictionary<string, FlowSeries> seriesByResource))
                {
                    seriesByResource = new Dictionary<string, FlowSeries>();
                    flows[buildingId] = seriesByResource;
                }

                HashSet<string> seen = null;
                if (resources != null)
                {
                    for (int i = 0; i < resources.Count; i++)
                    {
                        Resource resource = resources[i];
                        if (resource == null || string.IsNullOrEmpty(resource.name))
                        {
                            continue;
                        }

                        if (seen == null)
                        {
                            seen = new HashSet<string>();
                        }

                        seen.Add(resource.name);
                        if (!seriesByResource.TryGetValue(resource.name, out FlowSeries series))
                        {
                            series = new FlowSeries();
                            seriesByResource[resource.name] = series;
                        }

                        int storage = StorageOf(consumer, resource);
                        if (series.LastStorage == int.MinValue || now < series.Started)
                        {
                            series.LastStorage = storage;
                            series.Started = now;
                            series.Samples.Clear();
                            continue;
                        }

                        int delta = storage - series.LastStorage;
                        series.LastStorage = storage;
                        if (delta != 0)
                        {
                            series.Samples.Add(new FlowSample
                            {
                                Time = now,
                                Input = delta > 0 ? delta : 0,
                                Output = delta < 0 ? -delta : 0
                            });
                        }

                        int input = 0;
                        int output = 0;
                        for (int sampleIndex = series.Samples.Count - 1; sampleIndex >= 0; sampleIndex--)
                        {
                            FlowSample sample = series.Samples[sampleIndex];
                            if (now - sample.Time > WindowSeconds)
                            {
                                series.Samples.RemoveAt(sampleIndex);
                                continue;
                            }

                            input += sample.Input;
                            output += sample.Output;
                        }

                        if (now - series.Started < WindowSeconds ||
                            !IsShort(input, output) ||
                            !IsLowStock(storage, CapacityOf(consumer, resource)))
                        {
                            continue;
                        }

                        if (drains == null)
                        {
                            drains = new List<DrainLine>();
                        }

                        drains.Add(new DrainLine
                        {
                            Resource = resource,
                            Input = input,
                            Output = output
                        });
                    }
                }

                if (seen == null)
                {
                    seriesByResource.Clear();
                }
                else if (seen.Count != seriesByResource.Count)
                {
                    List<string> stale = null;
                    foreach (string name in seriesByResource.Keys)
                    {
                        if (seen.Contains(name))
                        {
                            continue;
                        }

                        if (stale == null)
                        {
                            stale = new List<string>();
                        }

                        stale.Add(name);
                    }

                    if (stale != null)
                    {
                        for (int i = 0; i < stale.Count; i++)
                        {
                            seriesByResource.Remove(stale[i]);
                        }
                    }
                }
            }

            return drains;
        }

        private static string Combine(string unreachable, List<DrainLine> drains)
        {
            if (unreachable == null && (drains == null || drains.Count == 0))
            {
                return null;
            }

            StringBuilder text = new StringBuilder();
            if (unreachable != null)
            {
                text.Append(unreachable);
            }

            if (drains != null && drains.Count > 0)
            {
                string[] left = new string[drains.Count];
                int[] input = new int[drains.Count];
                int[] output = new int[drains.Count];
                bool[] warned = new bool[drains.Count];
                int count = 0;
                for (int i = 0; i < drains.Count; i++)
                {
                    Resource resource = drains[i].Resource;
                    if (resource == null)
                    {
                        continue;
                    }

                    left[count] = NameWithWindow(resource.GetNameInGame(), false);
                    input[count] = drains[i].Input;
                    output[count] = drains[i].Output;
                    warned[count] = true;
                    count++;
                }

                if (count > 0)
                {
                    if (text.Length > 0)
                    {
                        text.Append('\n');
                    }

                    text.Append(JoinRows(left, input, output, warned, count, false));
                }
            }

            return text.ToString();
        }

        private static string JoinRows(
            string[] left,
            int[] input,
            int[] output,
            bool[] warned,
            int count,
            bool measureFont)
        {
            int[] perMinuteIn = new int[count];
            int[] perMinuteOut = new int[count];
            int digits = 3;
            for (int i = 0; i < count; i++)
            {
                perMinuteIn[i] = Mathf.RoundToInt(input[i] * 60f / WindowSeconds);
                perMinuteOut[i] = Mathf.RoundToInt(output[i] * 60f / WindowSeconds);
                digits = Mathf.Max(digits, DecimalLength(perMinuteIn[i]), DecimalLength(perMinuteOut[i]));
            }

            Text label = measureFont ? WorldScripts.Inst?.tooltipManager?.tooltipPanel?.descriptionText : null;
            Font font = label != null ? label.font : null;
            int fontSize = label != null && label.fontSize > 0 ? label.fontSize : 16;
            FontStyle fontStyle = label != null ? label.fontStyle : FontStyle.Normal;
            int[] leftWidth = new int[count];
            int space = MeasureRows(font, fontSize, fontStyle, left, count, leftWidth);
            int column = space * 4;
            for (int i = 0; i < count; i++)
            {
                column = Mathf.Max(column, leftWidth[i] + space * 4);
            }

            StringBuilder text = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    text.Append('\n');
                }

                bool warn = warned != null && warned[i];
                text.Append(Paint(left[i], warn));
                AppendGap(text, column - leftWidth[i], space, fontSize);
                text.Append(Paint("入", warn));
                text.Append(Digits(perMinuteIn[i], digits, warn));
                text.Append(Paint("/m,出", warn));
                text.Append(Digits(perMinuteOut[i], digits, warn));
                text.Append(Paint("/m", warn));
            }

            return text.ToString();
        }

        private static int MeasureRows(
            Font font,
            int fontSize,
            FontStyle fontStyle,
            string[] left,
            int count,
            int[] leftWidth)
        {
            int space;
            if (font != null &&
                TryMeasure(font, fontSize, fontStyle, " ", out space) &&
                space > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (!TryMeasure(font, fontSize, fontStyle, left[i], out leftWidth[i]))
                    {
                        space = 0;
                        break;
                    }
                }

                if (space > 0)
                {
                    return space;
                }
            }

            for (int i = 0; i < count; i++)
            {
                leftWidth[i] = DisplayWidth(left[i]);
            }

            return 1;
        }

        private static bool TryMeasure(Font font, int fontSize, FontStyle fontStyle, string text, out int width)
        {
            width = 0;
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            font.RequestCharactersInTexture(text, fontSize, fontStyle);
            for (int i = 0; i < text.Length; i++)
            {
                if (!font.GetCharacterInfo(text[i], out CharacterInfo info, fontSize, fontStyle) || info.advance <= 0)
                {
                    width = 0;
                    return false;
                }

                width += info.advance;
            }

            return true;
        }

        private static void AppendGap(StringBuilder text, int deficit, int space, int fontSize)
        {
            if (deficit <= 0 || space <= 0)
            {
                text.Append(' ');
                return;
            }

            int spaces = deficit / space;
            int remainder = deficit - spaces * space;
            if (spaces > 0)
            {
                text.Append(' ', spaces);
            }

            if (remainder <= 0 || fontSize <= 0)
            {
                if (spaces == 0)
                {
                    text.Append(' ');
                }

                return;
            }

            int size = Mathf.Clamp(Mathf.RoundToInt(fontSize * (float)remainder / space), 1, fontSize);
            text.Append("<size=").Append(size).Append("> </size>");
        }

        private static string Digits(int value, int width, bool warn)
        {
            if (value < 0)
            {
                value = 0;
            }

            string text = value.ToString();
            StringBuilder builder = new StringBuilder();
            for (int i = text.Length; i < width; i++)
            {
                builder.Append("<color=#00000000>0</color>");
            }

            builder.Append(Paint(text, warn));
            return builder.ToString();
        }

        private static string Paint(string text, bool warn)
        {
            if (!warn || string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            return WarningMarkup + text + "</color>";
        }

        private static int DecimalLength(int value)
        {
            int length = 1;
            int rest = value < 0 ? -value : value;
            while (rest >= 10)
            {
                rest /= 10;
                length++;
            }

            return length;
        }

        private static int DisplayWidth(string text)
        {
            int width = 0;
            foreach (char character in text ?? string.Empty)
            {
                width += character > 127 ? 2 : 1;
            }

            return width;
        }

        private static string SpriteOf(List<DrainLine> drains)
        {
            Resource resource = null;
            int worst = int.MinValue;
            for (int i = 0; drains != null && i < drains.Count; i++)
            {
                int deficit = drains[i].Output - drains[i].Input;
                if (deficit > worst)
                {
                    worst = deficit;
                    resource = drains[i].Resource;
                }
            }

            string path = resource?.GetSpritePath();
            return string.IsNullOrEmpty(path) ? RoadSprite : path;
        }

        private static int StorageOf(ConsumerProducer consumer, Resource resource)
        {
            if (consumer?.incomingResources == null || consumer.incomingStorage == null || resource == null)
            {
                return 0;
            }

            int index = System.Array.IndexOf(consumer.incomingResources, resource);
            if (index < 0 || index >= consumer.incomingStorage.Length)
            {
                return 0;
            }

            return consumer.incomingStorage[index];
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.UpdateFrameThread))]
    internal static class CenterHeadWarningPatch
    {
        private static void Postfix(Building __instance)
        {
            CenterHeadWarnings.Show(__instance);
        }
    }

    internal sealed class CenterHoverTip : IMouseHoverListener
    {
        private struct HoverLine
        {
            public string Name;
            public int InPerMin;
            public int OutPerMin;
            public int StockPercent;
            public bool Warn;
        }

        private static readonly Color WarningColor = new Color(0.86f, 0.16f, 0.14f, 1f);

        private static readonly List<HoverLine> Lines = new List<HoverLine>();

        private static CenterHoverTip instance;

        private static int shownFrame = -1;

        private static string emptyMessage;

        internal static void Ensure()
        {
            ClickHandler clicks = WorldScripts.Inst?.clickHandler;
            if (clicks == null)
            {
                return;
            }

            if (instance == null)
            {
                instance = new CenterHoverTip();
                clicks.AddMouseHoverListener(instance);
            }

            if (clicks.GetComponent<CenterHoverTipDrawer>() == null)
            {
                clicks.gameObject.AddComponent<CenterHoverTipDrawer>();
            }
        }

        public bool HandleMouseHover(RaycastHit[] hits)
        {
            Building building = ClosestCenter(hits);
            if (building == null)
            {
                return false;
            }

            Capture(building);
            shownFrame = Time.frameCount;
            return true;
        }

        internal static void Draw()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || shownFrame != Time.frameCount)
            {
                return;
            }

            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            int bodySize = Mathf.RoundToInt(15f * scale);
            GUIStyle text = new GUIStyle(GUI.skin.label)
            {
                fontSize = bodySize,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                richText = false,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Color.white }
            };
            GUIStyle number = new GUIStyle(text)
            {
                alignment = TextAnchor.UpperRight
            };
            GUIStyle title = new GUIStyle(text)
            {
                fontSize = Mathf.RoundToInt(20f * scale)
            };

            float gap = 12f * scale;
            float small = 3f * scale;
            float lineHeight = bodySize + 5f * scale;
            float pad = 10f * scale;
            float titleHeight = title.fontSize + 8f * scale;
            float nameWidth = 0f;
            float numberWidth = TextWidth(number, "888");
            for (int i = 0; i < Lines.Count; i++)
            {
                HoverLine line = Lines[i];
                nameWidth = Mathf.Max(nameWidth, TextWidth(text, line.Name));
                numberWidth = Mathf.Max(
                    numberWidth,
                    TextWidth(number, line.InPerMin.ToString()),
                    TextWidth(number, line.OutPerMin.ToString()));
            }

            float inWidth = TextWidth(text, "入");
            float outWidth = TextWidth(text, "出");
            float unitWidth = TextWidth(text, "/m");
            float commaWidth = TextWidth(text, ",");
            float percentDigitWidth = TextWidth(number, "888");
            float percentSignWidth = TextWidth(text, "%");
            float contentWidth = Lines.Count == 0
                ? TextWidth(text, emptyMessage)
                : nameWidth + gap + inWidth + small + numberWidth + unitWidth + commaWidth + small + outWidth + small + numberWidth + unitWidth + commaWidth + small + percentDigitWidth + percentSignWidth;
            contentWidth = Mathf.Max(contentWidth, TextWidth(title, "物流中心"));
            float width = contentWidth + pad * 2f;
            float height = pad + titleHeight + (Lines.Count == 0 ? lineHeight : Lines.Count * lineHeight) + pad;
            Vector2 mouse = Event.current.mousePosition;
            float x = mouse.x + 18f * scale;
            float y = mouse.y + 18f * scale;
            if (x + width > Screen.width - 6f)
            {
                x = mouse.x - width - 12f * scale;
            }

            if (y + height > Screen.height - 6f)
            {
                y = Screen.height - height - 6f;
            }

            x = Mathf.Max(6f, x);
            y = Mathf.Max(6f, y);
            int previousDepth = GUI.depth;
            Color previous = GUI.color;
            GUI.depth = -2000;
            GUI.color = new Color(0.08f, 0.16f, 0.28f, 0.94f);
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            title.Draw(new Rect(x + pad, y + pad, contentWidth, titleHeight), "物流中心", false, false, false, false);
            float rowY = y + pad + titleHeight;
            if (Lines.Count == 0)
            {
                text.Draw(new Rect(x + pad, rowY, contentWidth, lineHeight), emptyMessage, false, false, false, false);
            }
            else
            {
                for (int i = 0; i < Lines.Count; i++)
                {
                    HoverLine line = Lines[i];
                    Color color = line.Warn ? WarningColor : Color.white;
                    text.normal.textColor = color;
                    number.normal.textColor = color;
                    float cursor = x + pad;
                    text.Draw(new Rect(cursor, rowY, nameWidth, lineHeight), line.Name, false, false, false, false);
                    cursor += nameWidth + gap;
                    text.Draw(new Rect(cursor, rowY, inWidth, lineHeight), "入", false, false, false, false);
                    cursor += inWidth + small;
                    number.Draw(new Rect(cursor, rowY, numberWidth, lineHeight), line.InPerMin.ToString(), false, false, false, false);
                    cursor += numberWidth;
                    text.Draw(new Rect(cursor, rowY, unitWidth + commaWidth, lineHeight), "/m,", false, false, false, false);
                    cursor += unitWidth + commaWidth + small;
                    text.Draw(new Rect(cursor, rowY, outWidth, lineHeight), "出", false, false, false, false);
                    cursor += outWidth + small;
                    number.Draw(new Rect(cursor, rowY, numberWidth, lineHeight), line.OutPerMin.ToString(), false, false, false, false);
                    cursor += numberWidth;
                    text.Draw(new Rect(cursor, rowY, unitWidth + commaWidth, lineHeight), "/m,", false, false, false, false);
                    cursor += unitWidth + commaWidth + small;
                    number.Draw(
                        new Rect(cursor, rowY, percentDigitWidth, lineHeight),
                        line.StockPercent.ToString(),
                        false,
                        false,
                        false,
                        false);
                    cursor += percentDigitWidth;
                    text.Draw(new Rect(cursor, rowY, percentSignWidth, lineHeight), "%", false, false, false, false);
                    rowY += lineHeight;
                }
            }

            GUI.color = previous;
            GUI.depth = previousDepth;
        }

        private static void Capture(Building building)
        {
            Lines.Clear();
            emptyMessage = null;
            ProductionLogic logic = building?.consumerProducer?.productionLogic;
            IReadOnlyList<Resource> resources = null;
            if (logic is CollectionProductionLogic collection)
            {
                resources = CenterState.GetResources(collection);
            }
            else if (logic is DistributionProductionLogic distribution)
            {
                resources = CenterState.GetResources(distribution);
            }

            if (resources == null || resources.Count == 0)
            {
                emptyMessage = "没有选择物资";
                return;
            }

            long buildingId = building.GetID();
            for (int i = 0; i < resources.Count; i++)
            {
                Resource resource = resources[i];
                if (resource == null || string.IsNullOrEmpty(resource.name))
                {
                    continue;
                }

                CenterHeadWarnings.ReadMinuteFlow(buildingId, resource.name, out int inflow, out int outflow);
                CenterHeadWarnings.PerMinute(inflow, outflow, out int perMinuteIn, out int perMinuteOut);
                Lines.Add(new HoverLine
                {
                    Name = CenterHeadWarnings.NameWithWindow(resource.GetNameInGame(), false),
                    InPerMin = perMinuteIn,
                    OutPerMin = perMinuteOut,
                    StockPercent = CenterHeadWarnings.StockPercent(building, resource),
                    Warn = CenterHeadWarnings.IsResourceWarned(building, resource)
                });
            }

            if (Lines.Count == 0)
            {
                emptyMessage = "没有选择物资";
            }
        }

        private static float TextWidth(GUIStyle style, string value)
        {
            float measured = style.CalcSize(new GUIContent(value ?? string.Empty)).x;
            float size = Mathf.Max(12f, style.fontSize);
            float estimated = 0f;
            foreach (char character in value ?? string.Empty)
            {
                estimated += character <= 0x7f ? size * 0.56f : size;
            }

            return Mathf.Max(measured, estimated);
        }

        private static Building ClosestCenter(RaycastHit[] hits)
        {
            if (hits == null)
            {
                return null;
            }

            Building closest = null;
            float best = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null || hit.distance >= best)
                {
                    continue;
                }

                ColliderInfo info = Helper.GetComponentInHierarchy<ColliderInfo>(hit.collider.gameObject);
                Building building = info?.collidable as Building;
                ProductionLogic logic = building?.consumerProducer?.productionLogic;
                if (!(logic is CollectionProductionLogic) && !(logic is DistributionProductionLogic))
                {
                    continue;
                }

                closest = building;
                best = hit.distance;
            }

            return closest;
        }
    }

    internal sealed class CenterHoverTipDrawer : MonoBehaviour
    {
        private void OnGUI()
        {
            CenterHoverTip.Draw();
        }
    }
}
