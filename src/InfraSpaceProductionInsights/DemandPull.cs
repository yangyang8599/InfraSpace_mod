using System;
using System.Collections.Generic;

namespace InfraSpaceProductionInsights
{
    internal static class DemandPull
    {
        private const double Zero = 1e-6;

        internal sealed class Node<T>
        {
            public Dictionary<T, double> Inputs = new Dictionary<T, double>();

            public Dictionary<T, double> Outputs = new Dictionary<T, double>();

            public bool Scaled;

            public double Scale;
        }

        internal static Dictionary<T, double> Solve<T>(
            IReadOnlyList<Node<T>> nodes,
            IReadOnlyDictionary<T, double> endUse)
        {
            var production = new Dictionary<T, double>();
            var consumption = new Dictionary<T, double>();
            var consumers = new Dictionary<T, List<Node<T>>>();
            foreach (Node<T> node in nodes)
            {
                foreach (KeyValuePair<T, double> output in node.Outputs)
                {
                    Add(production, output.Key, output.Value);
                }

                foreach (KeyValuePair<T, double> input in node.Inputs)
                {
                    Add(consumption, input.Key, input.Value);
                    if (!consumers.TryGetValue(input.Key, out List<Node<T>> list))
                    {
                        list = new List<Node<T>>();
                        consumers[input.Key] = list;
                    }

                    list.Add(node);
                }
            }

            if (endUse != null)
            {
                foreach (KeyValuePair<T, double> pair in endUse)
                {
                    Add(consumption, pair.Key, pair.Value);
                }
            }

            var finals = new HashSet<T>();
            foreach (KeyValuePair<T, double> pair in production)
            {
                consumption.TryGetValue(pair.Key, out double used);
                if (pair.Value > Zero && used <= Zero)
                {
                    finals.Add(pair.Key);
                }
            }

            var spokenFor = new Dictionary<T, double>();
            var resolved = new HashSet<T>(finals);
            var resources = new HashSet<T>(production.Keys);
            foreach (T resource in consumption.Keys)
            {
                resources.Add(resource);
            }

            foreach (T resource in resources)
            {
                if (finals.Contains(resource))
                {
                    continue;
                }

                if (consumers.ContainsKey(resource))
                {
                    continue;
                }

                double needed = 0d;
                if (endUse != null)
                {
                    endUse.TryGetValue(resource, out needed);
                }

                spokenFor[resource] = needed;
                resolved.Add(resource);
            }

            int limit = nodes.Count + resources.Count + 2;
            for (int pass = 0; pass < limit; pass++)
            {
                bool progress = false;
                foreach (Node<T> node in nodes)
                {
                    if (node.Scaled)
                    {
                        continue;
                    }

                    if (!TryScale(node, production, finals, resolved, spokenFor))
                    {
                        continue;
                    }

                    progress = true;
                }

                foreach (T resource in resources)
                {
                    if (resolved.Contains(resource) || !consumers.TryGetValue(resource, out List<Node<T>> list))
                    {
                        continue;
                    }

                    bool ready = true;
                    foreach (Node<T> node in list)
                    {
                        if (!node.Scaled)
                        {
                            ready = false;
                            break;
                        }
                    }

                    if (!ready)
                    {
                        continue;
                    }

                    double needed = 0d;
                    if (endUse != null)
                    {
                        endUse.TryGetValue(resource, out needed);
                    }

                    foreach (Node<T> node in list)
                    {
                        node.Inputs.TryGetValue(resource, out double input);
                        needed += input * node.Scale;
                    }

                    spokenFor[resource] = needed;
                    resolved.Add(resource);
                    progress = true;
                }

                if (!progress)
                {
                    break;
                }
            }

            var demand = new Dictionary<T, double>();
            foreach (T resource in resolved)
            {
                if (finals.Contains(resource))
                {
                    demand[resource] = 0d;
                    continue;
                }

                spokenFor.TryGetValue(resource, out double needed);
                demand[resource] = needed;
            }

            return demand;
        }

        private static bool TryScale<T>(
            Node<T> node,
            Dictionary<T, double> production,
            HashSet<T> finals,
            HashSet<T> resolved,
            Dictionary<T, double> spokenFor)
        {
            double scale = 0d;
            bool anyOutput = false;
            foreach (KeyValuePair<T, double> output in node.Outputs)
            {
                if (output.Value <= Zero)
                {
                    continue;
                }

                anyOutput = true;
                if (!resolved.Contains(output.Key))
                {
                    return false;
                }

                production.TryGetValue(output.Key, out double produced);
                if (produced <= Zero)
                {
                    continue;
                }

                double basis = produced;
                if (!finals.Contains(output.Key))
                {
                    spokenFor.TryGetValue(output.Key, out basis);
                }

                scale = Math.Max(scale, Math.Min(1d, basis / produced));
            }

            if (!anyOutput)
            {
                scale = 0d;
            }

            node.Scale = scale;
            node.Scaled = true;
            return true;
        }

        private static void Add<T>(Dictionary<T, double> values, T key, double amount)
        {
            if (amount == 0d)
            {
                return;
            }

            values.TryGetValue(key, out double current);
            values[key] = current + amount;
        }
    }
}
