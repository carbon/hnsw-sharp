﻿// <copyright file="SmallWorld.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace HNSW.Net
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;

    /// <summary>
    /// The Hierarchical Navigable Small World Graphs.
    /// https://arxiv.org/abs/1603.09320
    /// </summary>
    /// <typeparam name="TItem">The type of items to connect into small world.</typeparam>
    /// <typeparam name="TDistance">The type of distance between items (expect any numeric type: float, double, decimal, int, ...).</typeparam>
    public partial class SmallWorld<TItem, TDistance> where TDistance : struct, IComparable<TDistance>
    {
        /// <summary>
        /// The distance function in the items space.
        /// </summary>
        private readonly Func<TItem, TItem, TDistance> Distance;

        /// <summary>
        /// The hierarchical small world graph instance.
        /// </summary>
        private Graph<TItem, TDistance> Graph;

        /// <summary>
        /// Initializes a new instance of the <see cref="SmallWorld{TItem, TDistance}"/> class.
        /// </summary>
        /// <param name="distance">The distance function to use in the small world.</param>
        public SmallWorld(Func<TItem, TItem, TDistance> distance)
        {
            Distance = distance;
        }

        /// <summary>
        /// Type of heuristic to select best neighbours for a node.
        /// </summary>
        public enum NeighbourSelectionHeuristic
        {
            /// <summary>
            /// Marker for the Algorithm 3 (SELECT-NEIGHBORS-SIMPLE) from the article.
            /// Implemented in <see cref="Node.Algorithm3{TItem, TDistance}"/>
            /// </summary>
            SelectSimple,

            /// <summary>
            /// Marker for the Algorithm 4 (SELECT-NEIGHBORS-HEURISTIC) from the article.
            /// Implemented in <see cref="Node.Algorithm4{TItem, TDistance}"/>
            /// </summary>
            SelectHeuristic
        }

        /// <summary>
        /// Builds hnsw graph from the items.
        /// </summary>
        /// <param name="items">The items to connect into the graph.</param>
        /// <param name="generator">The random number generator for building graph.</param>
        /// <param name="parameters">Parameters of the algorithm.</param>
        public void BuildGraph(IReadOnlyList<TItem> items, IProvideRandomValues generator, Parameters parameters)
        {
            var graph = new Graph<TItem, TDistance>(Distance, parameters);
            graph.AddItems(items, generator);
            Graph = graph;
        }

        /// <summary>
        /// Run knn search for a given item.
        /// </summary>
        /// <param name="item">The item to search nearest neighbours.</param>
        /// <param name="k">The number of nearest neighbours.</param>
        /// <returns>The list of found nearest neighbours.</returns>
        public IList<KNNSearchResult> KNNSearch(TItem item, int k)
        {
            return Graph.KNearest(item, k);
        }

        /// <summary>
        /// Serializes the graph WITHOUT linked items.
        /// </summary>
        /// <returns>Bytes representing the graph.</returns>
        public byte[] SerializeGraph()
        {
            if (Graph == null)
            {
                throw new InvalidOperationException("The graph does not exist");
            }

            using (var stream = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(stream, Graph.Parameters.M);
                formatter.Serialize(stream, Graph.Serialize());
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Deserializes the graph from byte array.
        /// </summary>
        /// <param name="items">The items to assign to the graph's verticies.</param>
        /// <param name="bytes">The serialized parameters and edges.</param>
        public void DeserializeGraph(IReadOnlyList<TItem> items, byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            {
                var formatter = new BinaryFormatter();
                var m = (int)formatter.Deserialize(stream);
                var graphBytes = (byte[])formatter.Deserialize(stream);

                var parameters = new Parameters { M = m };
                var graph = new Graph<TItem, TDistance>(Distance, parameters);
                graph.Deserialize(items, graphBytes);

                Graph = graph;
            }
        }

        /// <summary>
        /// Prints edges of the graph.
        /// Mostly for debug and test purposes.
        /// </summary>
        /// <returns>String representation of the graph's edges.</returns>
        public string Print()
        {
            return Graph.Print();
        }

        /// <summary>
        /// Parameters of the algorithm.
        /// </summary>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "By Design")]
        public class Parameters
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Parameters"/> class.
            /// </summary>
            public Parameters()
            {
                M = 10;
                LevelLambda = 1 / Math.Log(M);
                NeighbourHeuristic = NeighbourSelectionHeuristic.SelectSimple;
                ConstructionPruning = 200;
                ExpandBestSelection = false;
                KeepPrunedConnections = false;
                EnableDistanceCacheForConstruction = false;
            }

            /// <summary>
            /// Gets or sets the parameter which defines the maximum number of neighbors in the zero and above-zero layers.
            /// The maximum number of neighbors for the zero layer is 2 * M.
            /// The maximum number of neighbors for higher layers is M.
            /// </summary>
            public int M { get; set; }

            /// <summary>
            /// Gets or sets the max level decay parameter.
            /// https://en.wikipedia.org/wiki/Exponential_distribution
            /// See 'mL' parameter in the HNSW article.
            /// </summary>
            public double LevelLambda { get; set; }

            /// <summary>
            /// Gets or sets parameter which specifies the type of heuristic to use for best neighbours selection.
            /// </summary>
            public NeighbourSelectionHeuristic NeighbourHeuristic { get; set; }

            /// <summary>
            /// Gets or sets the number of candidates to consider as neighbours for a given node at the graph construction phase.
            /// See 'efConstruction' parameter in the article.
            /// </summary>
            public int ConstructionPruning { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether to expand candidates if <see cref="NeighbourSelectionHeuristic.SelectHeuristic"/> is used.
            /// See 'extendCandidates' parameter in the article.
            /// </summary>
            public bool ExpandBestSelection { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether to keep pruned candidates if <see cref="NeighbourSelectionHeuristic.SelectHeuristic"/> is used.
            /// See 'keepPrunedConnections' parameter in the article.
            /// </summary>
            public bool KeepPrunedConnections { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether to cache calculated distances at graph construction time.
            /// </summary>
            public bool EnableDistanceCacheForConstruction { get; set; }
        }

        /// <summary>
        /// Representation of knn search result.
        /// </summary>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "By Design")]
        public class KNNSearchResult
        {
            /// <summary>
            /// Gets or sets the id of the item = rank of the item in source collection
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// Gets or sets the item itself.
            /// </summary>
            public TItem Item { get; set; }

            /// <summary>
            /// Gets or sets the distance between the item and the knn search query.
            /// </summary>
            public TDistance Distance { get; set; }
        }
    }
}
