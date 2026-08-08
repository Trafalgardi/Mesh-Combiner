using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static class MeshCoplanarLightmapUv
{
    private const float MinimumArea = 0.0000000001f;
    private const float MinimumExtent = 0.000001f;
    private const float PackingEpsilon = 0.000001f;
    private const float MaximumPartialEdgeDirectionAngle = 1f;

    public readonly struct Result
    {
        public readonly int triangleCount;
        public readonly int chartCount;
        public readonly int stitchedEdgeConnections;
        public readonly int exactEdgeConnections;
        public readonly int partialCollinearEdgeConnections;
        public readonly int boundaryEdgeCount;
        public readonly int assignedVertexCount;
        public readonly float packingScale;
        public readonly float positionTolerance;
        public readonly float coplanarAngleDegrees;

        public Result(
            int triangleCount,
            int chartCount,
            int exactEdgeConnections,
            int partialCollinearEdgeConnections,
            int boundaryEdgeCount,
            int assignedVertexCount,
            float packingScale,
            float positionTolerance,
            float coplanarAngleDegrees)
        {
            this.triangleCount = triangleCount;
            this.chartCount = chartCount;
            this.exactEdgeConnections = exactEdgeConnections;
            this.partialCollinearEdgeConnections = partialCollinearEdgeConnections;
            stitchedEdgeConnections = exactEdgeConnections + partialCollinearEdgeConnections;
            this.boundaryEdgeCount = boundaryEdgeCount;
            this.assignedVertexCount = assignedVertexCount;
            this.packingScale = packingScale;
            this.positionTolerance = positionTolerance;
            this.coplanarAngleDegrees = coplanarAngleDegrees;
        }
    }

    private readonly struct QuantizedPoint : IEquatable<QuantizedPoint>, IComparable<QuantizedPoint>
    {
        private readonly long _x;
        private readonly long _y;
        private readonly long _z;

        public QuantizedPoint(Vector3 value, double inverseTolerance)
        {
            _x = (long)Math.Round(value.x * inverseTolerance);
            _y = (long)Math.Round(value.y * inverseTolerance);
            _z = (long)Math.Round(value.z * inverseTolerance);
        }

        public int CompareTo(QuantizedPoint other)
        {
            int x = _x.CompareTo(other._x);
            if (x != 0) return x;
            int y = _y.CompareTo(other._y);
            return y != 0 ? y : _z.CompareTo(other._z);
        }

        public bool Equals(QuantizedPoint other) => _x == other._x && _y == other._y && _z == other._z;
        public override bool Equals(object obj) => obj is QuantizedPoint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _x.GetHashCode();
                hash = (hash * 397) ^ _y.GetHashCode();
                hash = (hash * 397) ^ _z.GetHashCode();
                return hash;
            }
        }
    }

    private readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        private readonly QuantizedPoint _a;
        private readonly QuantizedPoint _b;

        public EdgeKey(Vector3 a, Vector3 b, double inverseTolerance)
        {
            QuantizedPoint pointA = new QuantizedPoint(a, inverseTolerance);
            QuantizedPoint pointB = new QuantizedPoint(b, inverseTolerance);
            if (pointA.CompareTo(pointB) <= 0)
            {
                _a = pointA;
                _b = pointB;
            }
            else
            {
                _a = pointB;
                _b = pointA;
            }
        }

        public bool Equals(EdgeKey other) => _a.Equals(other._a) && _b.Equals(other._b);
        public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (_a.GetHashCode() * 397) ^ _b.GetHashCode();
            }
        }
    }

    private readonly struct EdgeRecord
    {
        public readonly Vector3 a;
        public readonly Vector3 b;
        public readonly int triangleIndex;
        public readonly Vector3 normal;

        public EdgeRecord(Vector3 a, Vector3 b, int triangleIndex, Vector3 normal)
        {
            this.a = a;
            this.b = b;
            this.triangleIndex = triangleIndex;
            this.normal = normal;
        }
    }

    private readonly struct Triangle
    {
        public readonly int a;
        public readonly int b;
        public readonly int c;
        public readonly Vector3 worldA;
        public readonly Vector3 worldB;
        public readonly Vector3 worldC;
        public readonly Vector3 normal;
        public readonly float area;

        public Triangle(
            int a,
            int b,
            int c,
            Vector3 worldA,
            Vector3 worldB,
            Vector3 worldC,
            Vector3 normal,
            float area)
        {
            this.a = a;
            this.b = b;
            this.c = c;
            this.worldA = worldA;
            this.worldB = worldB;
            this.worldC = worldC;
            this.normal = normal;
            this.area = area;
        }
    }

    private sealed class Chart
    {
        public int root;
        public readonly List<int> triangleIndices = new List<int>();
        public readonly HashSet<int> vertexIndices = new HashSet<int>();
        public readonly Dictionary<int, Vector2> projectedByVertex = new Dictionary<int, Vector2>();
        public Vector3 normal;
        public Vector3 tangent;
        public Vector3 bitangent;
        public Vector2 min;
        public Vector2 max;
        public Vector2 packedMin;
    }

    private sealed class DisjointSet
    {
        private readonly int[] _parent;
        private readonly byte[] _rank;

        public DisjointSet(int count)
        {
            _parent = new int[count];
            _rank = new byte[count];
            for (int i = 0; i < count; i++) _parent[i] = i;
        }

        public int Find(int value)
        {
            int root = value;
            while (_parent[root] != root) root = _parent[root];
            while (_parent[value] != value)
            {
                int next = _parent[value];
                _parent[value] = root;
                value = next;
            }

            return root;
        }

        public bool Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA == rootB) return false;

            if (_rank[rootA] < _rank[rootB]) _parent[rootA] = rootB;
            else if (_rank[rootA] > _rank[rootB]) _parent[rootB] = rootA;
            else
            {
                _parent[rootB] = rootA;
                _rank[rootA]++;
            }

            return true;
        }
    }

    public static bool TryGenerate(
        Mesh mesh,
        Matrix4x4 localToWorld,
        float positionTolerance,
        float coplanarAngleDegrees,
        int paddingTexels,
        int paddingReferenceResolution,
        out Result result,
        out string error)
    {
        result = default;
        error = null;

        if (mesh == null)
        {
            error = "Mesh is null.";
            return false;
        }

        positionTolerance = Mathf.Max(positionTolerance, 0.000001f);
        coplanarAngleDegrees = Mathf.Clamp(coplanarAngleDegrees, 0.01f, 10f);
        paddingTexels = Mathf.Clamp(paddingTexels, 0, 32);
        paddingReferenceResolution = Mathf.Clamp(paddingReferenceResolution, 128, 8192);

        Vector3[] localVertices = mesh.vertices;
        if (localVertices == null || localVertices.Length == 0)
        {
            error = "Mesh has no vertices.";
            return false;
        }

        if (!TryReadTriangles(mesh, localVertices, localToWorld, out List<Triangle> triangles, out error))
            return false;

        if (triangles.Count == 0)
        {
            error = "Mesh has no non-degenerate triangles.";
            return false;
        }

        double inverseTolerance = 1.0 / positionTolerance;
        float minimumNormalDot = Mathf.Cos(coplanarAngleDegrees * Mathf.Deg2Rad);
        DisjointSet sets = new DisjointSet(triangles.Count);
        Dictionary<EdgeKey, List<EdgeRecord>> edgeOwners = new Dictionary<EdgeKey, List<EdgeRecord>>();
        int exactEdgeConnections = 0;

        for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            Triangle triangle = triangles[triangleIndex];
            RegisterEdge(
                triangle.worldA,
                triangle.worldB,
                triangleIndex,
                triangle.normal,
                inverseTolerance,
                minimumNormalDot,
                sets,
                edgeOwners,
                ref exactEdgeConnections);
            RegisterEdge(
                triangle.worldB,
                triangle.worldC,
                triangleIndex,
                triangle.normal,
                inverseTolerance,
                minimumNormalDot,
                sets,
                edgeOwners,
                ref exactEdgeConnections);
            RegisterEdge(
                triangle.worldC,
                triangle.worldA,
                triangleIndex,
                triangle.normal,
                inverseTolerance,
                minimumNormalDot,
                sets,
                edgeOwners,
                ref exactEdgeConnections);
        }

        List<EdgeRecord> seamCandidateEdges = CollectSeamCandidateEdges(edgeOwners, minimumNormalDot);
        int partialCollinearEdgeConnections = StitchPartialCollinearBoundaryEdges(
            seamCandidateEdges,
            minimumNormalDot,
            coplanarAngleDegrees,
            positionTolerance,
            sets);

        Dictionary<int, Chart> chartsByRoot = new Dictionary<int, Chart>();
        for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            int root = sets.Find(triangleIndex);
            if (!chartsByRoot.TryGetValue(root, out Chart chart))
            {
                chart = new Chart { root = root };
                chartsByRoot.Add(root, chart);
            }

            chart.triangleIndices.Add(triangleIndex);
        }

        List<Chart> charts = chartsByRoot.Values.ToList();
        foreach (Chart chart in charts)
        {
            if (!TryBuildChartProjection(chart, triangles, out error))
                return false;
        }

        Dictionary<int, int> chartByVertex = new Dictionary<int, int>();
        for (int chartIndex = 0; chartIndex < charts.Count; chartIndex++)
        {
            foreach (int vertexIndex in charts[chartIndex].vertexIndices)
            {
                if (chartByVertex.TryGetValue(vertexIndex, out int existingChart) && existingChart != chartIndex)
                {
                    error = "Coplanar UV generation found a vertex shared by multiple generated charts. " +
                            "This mesh needs vertex splitting at a hard/UV boundary before a safe stitched UV2 can be assigned. " +
                            "Use Regenerate UV2 for this mesh or split the source vertex topology.";
                    return false;
                }

                chartByVertex[vertexIndex] = chartIndex;
            }
        }

        float padding = paddingTexels / (float)paddingReferenceResolution;
        if (!TryPackCharts(charts, padding, out float packingScale))
        {
            error = "Coplanar UV chart packing failed. Reduce padding, increase the reference resolution, " +
                    "or split the combined geometry into smaller spatial clusters.";
            return false;
        }

        Vector2[] uv2 = new Vector2[mesh.vertexCount];
        foreach (Chart chart in charts)
        {
            foreach (KeyValuePair<int, Vector2> pair in chart.projectedByVertex)
            {
                uv2[pair.Key] = chart.packedMin + (pair.Value - chart.min) * packingScale;
            }
        }

        mesh.uv2 = uv2;

        result = new Result(
            triangles.Count,
            charts.Count,
            exactEdgeConnections,
            partialCollinearEdgeConnections,
            seamCandidateEdges.Count,
            chartByVertex.Count,
            packingScale,
            positionTolerance,
            coplanarAngleDegrees);
        return true;
    }

    private static bool TryReadTriangles(
        Mesh mesh,
        Vector3[] localVertices,
        Matrix4x4 localToWorld,
        out List<Triangle> triangles,
        out string error)
    {
        triangles = new List<Triangle>();
        error = null;

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
            {
                error = "Coplanar UV generation currently requires triangle topology on every submesh. Submesh " +
                        subMeshIndex + " uses " + mesh.GetTopology(subMeshIndex) + ".";
                return false;
            }

            int[] indices = mesh.GetIndices(subMeshIndex);
            if (indices.Length % 3 != 0)
            {
                error = "Submesh " + subMeshIndex + " has an invalid triangle index count.";
                return false;
            }

            for (int i = 0; i < indices.Length; i += 3)
            {
                int indexA = indices[i];
                int indexB = indices[i + 1];
                int indexC = indices[i + 2];
                if ((uint)indexA >= localVertices.Length ||
                    (uint)indexB >= localVertices.Length ||
                    (uint)indexC >= localVertices.Length)
                {
                    error = "Mesh contains an out-of-range triangle index.";
                    return false;
                }

                Vector3 worldA = localToWorld.MultiplyPoint3x4(localVertices[indexA]);
                Vector3 worldB = localToWorld.MultiplyPoint3x4(localVertices[indexB]);
                Vector3 worldC = localToWorld.MultiplyPoint3x4(localVertices[indexC]);
                Vector3 cross = Vector3.Cross(worldB - worldA, worldC - worldA);
                float doubleArea = cross.magnitude;
                if (doubleArea <= MinimumArea) continue;

                triangles.Add(new Triangle(
                    indexA,
                    indexB,
                    indexC,
                    worldA,
                    worldB,
                    worldC,
                    cross / doubleArea,
                    doubleArea * 0.5f));
            }
        }

        return true;
    }

    private static void RegisterEdge(
        Vector3 a,
        Vector3 b,
        int triangleIndex,
        Vector3 normal,
        double inverseTolerance,
        float minimumNormalDot,
        DisjointSet sets,
        Dictionary<EdgeKey, List<EdgeRecord>> edgeOwners,
        ref int exactEdgeConnections)
    {
        EdgeKey edge = new EdgeKey(a, b, inverseTolerance);
        if (!edgeOwners.TryGetValue(edge, out List<EdgeRecord> owners))
        {
            owners = new List<EdgeRecord>(2);
            edgeOwners.Add(edge, owners);
        }

        for (int ownerIndex = 0; ownerIndex < owners.Count; ownerIndex++)
        {
            EdgeRecord other = owners[ownerIndex];
            if (Vector3.Dot(normal, other.normal) < minimumNormalDot) continue;
            if (sets.Union(triangleIndex, other.triangleIndex)) exactEdgeConnections++;
        }

        owners.Add(new EdgeRecord(a, b, triangleIndex, normal));
    }

    private static List<EdgeRecord> CollectSeamCandidateEdges(
        Dictionary<EdgeKey, List<EdgeRecord>> edgeOwners,
        float minimumNormalDot)
    {
        List<EdgeRecord> candidates = new List<EdgeRecord>();

        foreach (List<EdgeRecord> owners in edgeOwners.Values)
        {
            if (owners.Count == 1)
            {
                candidates.Add(owners[0]);
                continue;
            }

            if (owners.Count == 2 && Vector3.Dot(owners[0].normal, owners[1].normal) >= minimumNormalDot)
            {
                // Ordinary interior triangulation edge on one smooth/coplanar surface.
                // It cannot represent a T-junction between disconnected modular surfaces.
                continue;
            }

            // Hard perimeter edges on closed solids have two owners with different normals
            // (for example front face + side face). Non-manifold/exactly-overlapped groups can
            // contain more owners. Keep all such records so coplanar records can be matched
            // against differently segmented collinear edges in neighboring modules.
            candidates.AddRange(owners);
        }

        return candidates;
    }

    private static int StitchPartialCollinearBoundaryEdges(
        List<EdgeRecord> boundaryEdges,
        float minimumNormalDot,
        float coplanarAngleDegrees,
        float positionTolerance,
        DisjointSet sets)
    {
        if (boundaryEdges.Count < 2) return 0;

        float directionAngle = Mathf.Min(coplanarAngleDegrees, MaximumPartialEdgeDirectionAngle);
        float minimumDirectionDot = Mathf.Cos(directionAngle * Mathf.Deg2Rad);
        int stitchedConnections = 0;

        for (int i = 0; i < boundaryEdges.Count - 1; i++)
        {
            EdgeRecord edgeA = boundaryEdges[i];
            for (int j = i + 1; j < boundaryEdges.Count; j++)
            {
                EdgeRecord edgeB = boundaryEdges[j];
                if (edgeA.triangleIndex == edgeB.triangleIndex) continue;
                if (sets.Find(edgeA.triangleIndex) == sets.Find(edgeB.triangleIndex)) continue;
                if (Vector3.Dot(edgeA.normal, edgeB.normal) < minimumNormalDot) continue;
                if (!ExpandedBoundsOverlap(edgeA, edgeB, positionTolerance)) continue;
                if (!SegmentsHaveCollinearOverlap(edgeA, edgeB, positionTolerance, minimumDirectionDot)) continue;

                if (sets.Union(edgeA.triangleIndex, edgeB.triangleIndex)) stitchedConnections++;
            }
        }

        return stitchedConnections;
    }

    private static bool ExpandedBoundsOverlap(EdgeRecord a, EdgeRecord b, float tolerance)
    {
        Vector3 expansion = Vector3.one * tolerance;
        Vector3 minA = Vector3.Min(a.a, a.b) - expansion;
        Vector3 maxA = Vector3.Max(a.a, a.b) + expansion;
        Vector3 minB = Vector3.Min(b.a, b.b) - expansion;
        Vector3 maxB = Vector3.Max(b.a, b.b) + expansion;

        return minA.x <= maxB.x && maxA.x >= minB.x &&
               minA.y <= maxB.y && maxA.y >= minB.y &&
               minA.z <= maxB.z && maxA.z >= minB.z;
    }

    private static bool SegmentsHaveCollinearOverlap(
        EdgeRecord a,
        EdgeRecord b,
        float tolerance,
        float minimumDirectionDot)
    {
        Vector3 deltaA = a.b - a.a;
        Vector3 deltaB = b.b - b.a;
        float lengthA = deltaA.magnitude;
        float lengthB = deltaB.magnitude;
        if (lengthA <= tolerance || lengthB <= tolerance) return false;

        Vector3 directionA = deltaA / lengthA;
        Vector3 directionB = deltaB / lengthB;
        if (Mathf.Abs(Vector3.Dot(directionA, directionB)) < minimumDirectionDot) return false;

        if (DistancePointToInfiniteLine(b.a, a.a, directionA) > tolerance ||
            DistancePointToInfiniteLine(b.b, a.a, directionA) > tolerance ||
            DistancePointToInfiniteLine(a.a, b.a, directionB) > tolerance ||
            DistancePointToInfiniteLine(a.b, b.a, directionB) > tolerance)
        {
            return false;
        }

        float projectedB0 = Vector3.Dot(b.a - a.a, directionA);
        float projectedB1 = Vector3.Dot(b.b - a.a, directionA);
        float bMin = Mathf.Min(projectedB0, projectedB1);
        float bMax = Mathf.Max(projectedB0, projectedB1);
        float overlapStart = Mathf.Max(0f, bMin);
        float overlapEnd = Mathf.Min(lengthA, bMax);

        return overlapEnd - overlapStart > tolerance;
    }

    private static float DistancePointToInfiniteLine(Vector3 point, Vector3 linePoint, Vector3 lineDirection)
    {
        return Vector3.Cross(point - linePoint, lineDirection).magnitude;
    }

    private static bool TryBuildChartProjection(Chart chart, List<Triangle> triangles, out string error)
    {
        error = null;
        Vector3 weightedNormal = Vector3.zero;

        foreach (int triangleIndex in chart.triangleIndices)
        {
            Triangle triangle = triangles[triangleIndex];
            weightedNormal += triangle.normal * triangle.area;
            chart.vertexIndices.Add(triangle.a);
            chart.vertexIndices.Add(triangle.b);
            chart.vertexIndices.Add(triangle.c);
        }

        if (weightedNormal.sqrMagnitude <= MinimumArea)
        {
            error = "A generated coplanar chart has no stable normal.";
            return false;
        }

        chart.normal = weightedNormal.normalized;
        Vector3 referenceAxis = Mathf.Abs(chart.normal.y) < 0.9f ? Vector3.up : Vector3.right;
        chart.tangent = Vector3.Cross(referenceAxis, chart.normal).normalized;
        chart.bitangent = Vector3.Cross(chart.normal, chart.tangent).normalized;
        chart.min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        chart.max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        foreach (int triangleIndex in chart.triangleIndices)
        {
            Triangle triangle = triangles[triangleIndex];
            ProjectVertex(chart, triangle.a, triangle.worldA);
            ProjectVertex(chart, triangle.b, triangle.worldB);
            ProjectVertex(chart, triangle.c, triangle.worldC);
        }

        Vector2 size = chart.max - chart.min;
        if (size.x <= MinimumExtent || size.y <= MinimumExtent)
        {
            error = "A generated coplanar chart is degenerate after planar projection.";
            return false;
        }

        return true;
    }

    private static void ProjectVertex(Chart chart, int vertexIndex, Vector3 worldPosition)
    {
        if (chart.projectedByVertex.ContainsKey(vertexIndex)) return;

        Vector2 projected = new Vector2(
            Vector3.Dot(worldPosition, chart.tangent),
            Vector3.Dot(worldPosition, chart.bitangent));
        chart.projectedByVertex.Add(vertexIndex, projected);
        chart.min = Vector2.Min(chart.min, projected);
        chart.max = Vector2.Max(chart.max, projected);
    }

    private static bool TryPackCharts(List<Chart> charts, float padding, out float scale)
    {
        scale = 0f;
        if (charts.Count == 0) return false;

        List<Chart> ordered = charts
            .OrderByDescending(chart => chart.max.y - chart.min.y)
            .ThenByDescending(chart => chart.max.x - chart.min.x)
            .ThenBy(chart => chart.root)
            .ToList();

        float largestWidth = ordered.Max(chart => chart.max.x - chart.min.x);
        float largestHeight = ordered.Max(chart => chart.max.y - chart.min.y);
        float usable = Mathf.Max(MinimumExtent, 1f - padding * 2f);
        float high = Mathf.Min(
            usable / Mathf.Max(largestWidth, MinimumExtent),
            usable / Mathf.Max(largestHeight, MinimumExtent));
        if (high <= MinimumExtent) return false;

        if (TryShelfPack(ordered, high, padding, true))
        {
            scale = high;
            return true;
        }

        float low = 0f;
        for (int iteration = 0; iteration < 32; iteration++)
        {
            float candidate = (low + high) * 0.5f;
            if (TryShelfPack(ordered, candidate, padding, false)) low = candidate;
            else high = candidate;
        }

        if (low <= MinimumExtent || !TryShelfPack(ordered, low, padding, true)) return false;
        scale = low;
        return true;
    }

    private static bool TryShelfPack(List<Chart> charts, float scale, float padding, bool writePlacement)
    {
        float cursorX = 0f;
        float cursorY = 0f;
        float rowHeight = 0f;

        foreach (Chart chart in charts)
        {
            Vector2 size = chart.max - chart.min;
            float outerWidth = size.x * scale + padding * 2f;
            float outerHeight = size.y * scale + padding * 2f;
            if (outerWidth > 1f + PackingEpsilon || outerHeight > 1f + PackingEpsilon) return false;

            if (cursorX > 0f && cursorX + outerWidth > 1f + PackingEpsilon)
            {
                cursorY += rowHeight;
                cursorX = 0f;
                rowHeight = 0f;
            }

            if (cursorY + outerHeight > 1f + PackingEpsilon) return false;
            if (writePlacement) chart.packedMin = new Vector2(cursorX + padding, cursorY + padding);
            cursorX += outerWidth;
            rowHeight = Mathf.Max(rowHeight, outerHeight);
        }

        return true;
    }
}
