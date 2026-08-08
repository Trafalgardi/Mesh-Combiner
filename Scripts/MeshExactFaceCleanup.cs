using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal static class MeshExactFaceCleanup
{
    private const float OpposingNormalDotThreshold = -0.9999f;
    private const float MinimumNormalSqrMagnitude = 0.000000000001f;

    private readonly struct QuantizedPoint : IEquatable<QuantizedPoint>, IComparable<QuantizedPoint>
    {
        private readonly long _x;
        private readonly long _y;
        private readonly long _z;

        public QuantizedPoint(Vector3 position, double inverseTolerance)
        {
            _x = (long)Math.Round(position.x * inverseTolerance);
            _y = (long)Math.Round(position.y * inverseTolerance);
            _z = (long)Math.Round(position.z * inverseTolerance);
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

    private readonly struct TrianglePositionKey : IEquatable<TrianglePositionKey>
    {
        private readonly QuantizedPoint _a;
        private readonly QuantizedPoint _b;
        private readonly QuantizedPoint _c;

        public TrianglePositionKey(Vector3 a, Vector3 b, Vector3 c, double inverseTolerance)
        {
            QuantizedPoint p0 = new QuantizedPoint(a, inverseTolerance);
            QuantizedPoint p1 = new QuantizedPoint(b, inverseTolerance);
            QuantizedPoint p2 = new QuantizedPoint(c, inverseTolerance);

            if (p0.CompareTo(p1) > 0) Swap(ref p0, ref p1);
            if (p1.CompareTo(p2) > 0) Swap(ref p1, ref p2);
            if (p0.CompareTo(p1) > 0) Swap(ref p0, ref p1);

            _a = p0;
            _b = p1;
            _c = p2;
        }

        public bool Equals(TrianglePositionKey other) =>
            _a.Equals(other._a) && _b.Equals(other._b) && _c.Equals(other._c);

        public override bool Equals(object obj) => obj is TrianglePositionKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _a.GetHashCode();
                hash = (hash * 397) ^ _b.GetHashCode();
                hash = (hash * 397) ^ _c.GetHashCode();
                return hash;
            }
        }

        private static void Swap(ref QuantizedPoint a, ref QuantizedPoint b)
        {
            QuantizedPoint temporary = a;
            a = b;
            b = temporary;
        }
    }

    private readonly struct FaceReference
    {
        public readonly int subMeshIndex;
        public readonly int triangleStartIndex;
        public readonly Vector3 normal;

        public FaceReference(int subMeshIndex, int triangleStartIndex, Vector3 normal)
        {
            this.subMeshIndex = subMeshIndex;
            this.triangleStartIndex = triangleStartIndex;
            this.normal = normal;
        }
    }

    public static int RemoveExactOpposingTrianglePairs(Mesh mesh, float positionTolerance)
    {
        if (mesh == null) return 0;

        positionTolerance = Mathf.Max(positionTolerance, 0.000001f);
        double inverseTolerance = 1.0 / positionTolerance;
        Vector3[] vertices = mesh.vertices;

        Dictionary<TrianglePositionKey, List<FaceReference>> unmatchedFaces =
            new Dictionary<TrianglePositionKey, List<FaceReference>>();
        Dictionary<int, HashSet<int>> removalsBySubMesh = new Dictionary<int, HashSet<int>>();
        int removedPairs = 0;

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles) continue;

            int[] indices = mesh.GetIndices(subMeshIndex);
            for (int triangleStart = 0; triangleStart + 2 < indices.Length; triangleStart += 3)
            {
                int indexA = indices[triangleStart];
                int indexB = indices[triangleStart + 1];
                int indexC = indices[triangleStart + 2];

                Vector3 a = vertices[indexA];
                Vector3 b = vertices[indexB];
                Vector3 c = vertices[indexC];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                float normalSqrMagnitude = normal.sqrMagnitude;
                if (normalSqrMagnitude <= MinimumNormalSqrMagnitude) continue;
                normal /= Mathf.Sqrt(normalSqrMagnitude);

                TrianglePositionKey key = new TrianglePositionKey(a, b, c, inverseTolerance);
                if (!unmatchedFaces.TryGetValue(key, out List<FaceReference> candidates))
                {
                    candidates = new List<FaceReference>(1);
                    unmatchedFaces.Add(key, candidates);
                }

                int opposingCandidateIndex = -1;
                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    if (Vector3.Dot(candidates[candidateIndex].normal, normal) <= OpposingNormalDotThreshold)
                    {
                        opposingCandidateIndex = candidateIndex;
                        break;
                    }
                }

                if (opposingCandidateIndex < 0)
                {
                    candidates.Add(new FaceReference(subMeshIndex, triangleStart, normal));
                    continue;
                }

                FaceReference opposingFace = candidates[opposingCandidateIndex];
                candidates.RemoveAt(opposingCandidateIndex);
                MarkTriangleForRemoval(removalsBySubMesh, opposingFace.subMeshIndex, opposingFace.triangleStartIndex);
                MarkTriangleForRemoval(removalsBySubMesh, subMeshIndex, triangleStart);
                removedPairs++;
            }
        }

        if (removedPairs == 0) return 0;

        foreach (KeyValuePair<int, HashSet<int>> pair in removalsBySubMesh)
        {
            int subMeshIndex = pair.Key;
            HashSet<int> triangleStartsToRemove = pair.Value;
            int[] sourceIndices = mesh.GetIndices(subMeshIndex);
            List<int> filteredIndices = new List<int>(sourceIndices.Length - triangleStartsToRemove.Count * 3);

            for (int triangleStart = 0; triangleStart + 2 < sourceIndices.Length; triangleStart += 3)
            {
                if (triangleStartsToRemove.Contains(triangleStart)) continue;
                filteredIndices.Add(sourceIndices[triangleStart]);
                filteredIndices.Add(sourceIndices[triangleStart + 1]);
                filteredIndices.Add(sourceIndices[triangleStart + 2]);
            }

            mesh.SetIndices(filteredIndices, MeshTopology.Triangles, subMeshIndex, false);
        }

        mesh.RecalculateBounds();
        return removedPairs;
    }

    private static void MarkTriangleForRemoval(
        Dictionary<int, HashSet<int>> removalsBySubMesh,
        int subMeshIndex,
        int triangleStartIndex)
    {
        if (!removalsBySubMesh.TryGetValue(subMeshIndex, out HashSet<int> removals))
        {
            removals = new HashSet<int>();
            removalsBySubMesh.Add(subMeshIndex, removals);
        }

        removals.Add(triangleStartIndex);
    }
}
