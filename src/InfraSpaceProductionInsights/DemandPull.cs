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

            // 地区里还没建的配方。只用来换算原料，不能把它自己的产出当成最终产物。
            public bool Gap;
        }

        internal static Dictionary<T, double> Solve<T>(
            IReadOnlyList<Node<T>> nodes,
            Func<T, Node<T>> missingRecipe,
            Func<T, bool> rawInput)
        {
            var graph = new List<Node<T>>(nodes);
            Dictionary<T, double> demand = null;
            for (int attempt = 0; attempt < 64; attempt++)
            {
                for (int i = 0; i < graph.Count; i++)
                {
                    graph[i].Scaled = false;
                    graph[i].Scale = 0d;
                }

                int before = graph.Count;
                demand = SolveOnce(graph, missingRecipe, rawInput);
                if (graph.Count == before)
                {
                    return demand;
                }
            }

            for (int i = 0; i < graph.Count; i++)
            {
                graph[i].Scaled = false;
                graph[i].Scale = 0d;
            }

            return SolveOnce(graph, null, rawInput);
        }

        private static Dictionary<T, double> SolveOnce<T>(
            List<Node<T>> nodes,
            Func<T, Node<T>> missingRecipe,
            Func<T, bool> rawInput)
        {
            var production = new Dictionary<T, double>();
            var placedProduction = new Dictionary<T, double>();
            var factoryUse = new Dictionary<T, double>();
            var consumers = new Dictionary<T, List<Node<T>>>();
            foreach (Node<T> node in nodes)
            {
                foreach (KeyValuePair<T, double> output in node.Outputs)
                {
                    Add(production, output.Key, output.Value);
                    if (!node.Gap)
                    {
                        Add(placedProduction, output.Key, output.Value);
                    }
                }

                foreach (KeyValuePair<T, double> input in node.Inputs)
                {
                    // 同一条产线吃掉自己的产出，不把它变成中间产物，也不参加反推。
                    if (node.Outputs.ContainsKey(input.Key))
                    {
                        continue;
                    }

                    Add(factoryUse, input.Key, input.Value);
                    if (!consumers.TryGetValue(input.Key, out List<Node<T>> list))
                    {
                        list = new List<Node<T>>();
                        consumers[input.Key] = list;
                    }

                    list.Add(node);
                }
            }

            var finals = new HashSet<T>();
            foreach (KeyValuePair<T, double> pair in placedProduction)
            {
                factoryUse.TryGetValue(pair.Key, out double used);
                if (pair.Value > Zero && used <= Zero)
                {
                    finals.Add(pair.Key);
                }
            }

            var spokenFor = new Dictionary<T, double>();
            var resolved = new HashSet<T>(finals);
            var resources = new HashSet<T>(production.Keys);
            foreach (T resource in factoryUse.Keys)
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

                spokenFor[resource] = 0d;
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

                    if (!TryScale(node, production, finals, resolved, spokenFor, rawInput))
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
                    foreach (Node<T> node in list)
                    {
                        node.Inputs.TryGetValue(resource, out double input);
                        if (node.Outputs.ContainsKey(resource))
                        {
                            continue;
                        }

                        needed += input * node.Scale;
                    }

                    spokenFor[resource] = needed;
                    resolved.Add(resource);
                    progress = true;
                    production.TryGetValue(resource, out double produced);
                    bool raw = rawInput != null && rawInput(resource);
                    if (needed > Zero && produced <= Zero && missingRecipe != null && !raw)
                    {
                        Node<T> recipe = missingRecipe(resource);
                        if (recipe != null)
                        {
                            nodes.Add(recipe);
                        }
                    }
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
            Dictionary<T, double> spokenFor,
            Func<T, bool> rawInput)
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
                if (rawInput != null && rawInput(output.Key))
                {
                    continue;
                }

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

                // 不封顶。中间建筑比最终产物少时，需求仍按最终产物的满员配方往上长。
                double ratio = basis / produced;
                if (ratio > scale)
                {
                    scale = ratio;
                }
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
