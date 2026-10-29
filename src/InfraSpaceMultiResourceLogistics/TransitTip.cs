using System.Collections.Generic;
using System.Text;

namespace InfraSpaceMultiResourceLogistics
{
    internal static class TransitTip
    {
        private const int MaxLines = 8;

        private struct AmountLine
        {
            public string Name;
            public int Amount;
        }

        private struct Shipment
        {
            public Building Other;
            public int Amount;
            public bool Incoming;
        }

        internal static string Describe(Building building, Resource resource)
        {
            if (building == null || resource == null)
            {
                return string.Empty;
            }

            Flow flow = CollectOne(building, resource);
            StringBuilder text = new StringBuilder();
            text.Append(resource.GetNameInGame());
            if (flow.IncomingTotal <= 0 && flow.OutgoingTotal <= 0)
            {
                text.Append("\n当前没有在途货车");
                return text.ToString();
            }

            if (flow.IncomingTotal > 0)
            {
                text.Append('\n');
                text.Append(FormatSide("送货", flow.IncomingTotal, flow.Incoming));
            }

            if (flow.OutgoingTotal > 0)
            {
                text.Append('\n');
                text.Append(FormatSide("派送", flow.OutgoingTotal, flow.Outgoing));
            }

            return text.ToString();
        }

        internal static string DescribeIncoming(Building building, Resource resource)
        {
            Flow flow = CollectOne(building, resource);
            return FormatSide("送货", flow.IncomingTotal, flow.Incoming);
        }

        internal static string DescribeOutgoing(Building building, Resource resource)
        {
            Flow flow = CollectOne(building, resource);
            return FormatSide("派送", flow.OutgoingTotal, flow.Outgoing);
        }

        internal static void AppendAligned(StringBuilder text, Building building, Resource resource)
        {
            if (building == null || resource == null)
            {
                return;
            }

            Flow flow = CollectOne(building, resource);
            text.Append(resource.GetNameInGame());
            if (flow.IncomingTotal <= 0 && flow.OutgoingTotal <= 0)
            {
                text.Append("\n当前没有在途货车");
                return;
            }

            if (flow.IncomingTotal > 0)
            {
                text.Append("\n送货\t");
                text.Append(flow.IncomingTotal);
                AppendAlignedLines(text, flow.Incoming);
            }

            if (flow.OutgoingTotal > 0)
            {
                text.Append("\n派送\t");
                text.Append(flow.OutgoingTotal);
                AppendAlignedLines(text, flow.Outgoing);
            }
        }

        internal static string BuildingName(Building building)
        {
            return BuildingLabel(building);
        }

        private struct Flow
        {
            public int IncomingTotal;
            public int OutgoingTotal;
            public Dictionary<string, int> Incoming;
            public Dictionary<string, int> Outgoing;
        }

        private static string FormatSide(string label, int total, Dictionary<string, int> amounts)
        {
            StringBuilder text = new StringBuilder();
            text.Append(label);
            text.Append(' ');
            text.Append(total);
            if (amounts != null && amounts.Count > 0)
            {
                AppendLines(text, amounts);
            }

            return text.ToString();
        }

        private static Flow CollectOne(Building building, Resource resource)
        {
            Flow flow = new Flow
            {
                Incoming = new Dictionary<string, int>(),
                Outgoing = new Dictionary<string, int>(),
            };
            if (building == null || resource == null)
            {
                return flow;
            }

            Collect(building, resource, flow.Incoming, flow.Outgoing, ref flow.IncomingTotal, ref flow.OutgoingTotal);
            return flow;
        }

        private static void Collect(
            Building building,
            Resource resource,
            Dictionary<string, int> incoming,
            Dictionary<string, int> outgoing,
            ref int incomingTotal,
            ref int outgoingTotal)
        {
            Registry registry = WorldScripts.Inst == null ? null : WorldScripts.Inst.registry;
            if (registry == null || registry.carsList == null)
            {
                return;
            }

            long buildingId = building.GetID();
            List<Shipment> shipments = new List<Shipment>();
            lock (registry)
            {
                List<Car> cars = registry.carsList;
                for (int i = 0; i < cars.Count; i++)
                {
                    Car car = cars[i];
                    if (car == null || !car.IsValid() || car.resource != resource || car.amount <= 0)
                    {
                        continue;
                    }

                    Building source = car.source.Get();
                    Building target = car.target.Get();
                    bool fromHere = source != null && source.GetID() == buildingId;
                    bool toHere = target != null && target.GetID() == buildingId;
                    if (toHere && !fromHere)
                    {
                        shipments.Add(new Shipment { Other = source, Amount = car.amount, Incoming = true });
                    }
                    else if (fromHere && !toHere)
                    {
                        shipments.Add(new Shipment { Other = target, Amount = car.amount, Incoming = false });
                    }
                }
            }

            for (int i = 0; i < shipments.Count; i++)
            {
                Shipment shipment = shipments[i];
                if (shipment.Incoming)
                {
                    incomingTotal += shipment.Amount;
                    AddAmount(incoming, BuildingLabel(shipment.Other), shipment.Amount);
                }
                else
                {
                    outgoingTotal += shipment.Amount;
                    AddAmount(outgoing, BuildingLabel(shipment.Other), shipment.Amount);
                }
            }
        }

        private static void AddAmount(Dictionary<string, int> amounts, string name, int amount)
        {
            int current;
            amounts.TryGetValue(name, out current);
            amounts[name] = current + amount;
        }

        private static void AppendLines(StringBuilder text, Dictionary<string, int> amounts)
        {
            List<AmountLine> lines = new List<AmountLine>(amounts.Count);
            foreach (KeyValuePair<string, int> entry in amounts)
            {
                lines.Add(new AmountLine { Name = entry.Key, Amount = entry.Value });
            }

            lines.Sort((left, right) => right.Amount.CompareTo(left.Amount));
            int shown = lines.Count < MaxLines ? lines.Count : MaxLines;
            for (int i = 0; i < shown; i++)
            {
                text.Append("\n  ");
                text.Append(lines[i].Name);
                text.Append("  ");
                text.Append(lines[i].Amount);
            }

            if (lines.Count > shown)
            {
                text.Append("\n  还有 ");
                text.Append(lines.Count - shown);
                text.Append(" 处");
            }
        }

        private static void AppendAlignedLines(StringBuilder text, Dictionary<string, int> amounts)
        {
            List<AmountLine> lines = new List<AmountLine>(amounts.Count);
            foreach (KeyValuePair<string, int> entry in amounts)
            {
                lines.Add(new AmountLine { Name = entry.Key, Amount = entry.Value });
            }

            lines.Sort((left, right) => right.Amount.CompareTo(left.Amount));
            int shown = lines.Count < MaxLines ? lines.Count : MaxLines;
            for (int i = 0; i < shown; i++)
            {
                text.Append("\n  ");
                text.Append(lines[i].Name);
                text.Append('\t');
                text.Append(lines[i].Amount);
            }

            if (lines.Count > shown)
            {
                text.Append("\n  还有\t");
                text.Append(lines.Count - shown);
                text.Append(" 处");
            }
        }

        private static string BuildingLabel(Building building)
        {
            if (building == null)
            {
                return "未知地点";
            }

            string custom = building.GetCustomNameInGame();
            if (!string.IsNullOrEmpty(custom))
            {
                return custom;
            }

            if (!string.IsNullOrEmpty(building.buildingName))
            {
                return building.buildingName;
            }

            return "未命名";
        }
    }
}
