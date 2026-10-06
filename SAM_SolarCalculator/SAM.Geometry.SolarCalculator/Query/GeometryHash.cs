// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Object.Spatial;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// Stable content hash over the CONTEXT (occluder) faces. Occlusion is independent of face
        /// orientation and of the order the faces are supplied in, so vertices are tolerance-rounded
        /// and both the per-face vertex sets and the face hashes are sorted before hashing. Used for
        /// visibility-cache invalidation.
        /// </summary>
        public static string GeometryHash(this IEnumerable<LinkedFace3D> contextFaces, double tolerance = Core.Tolerance.Distance)
        {
            List<string> faceHashes = new List<string>();

            if (contextFaces != null)
            {
                foreach (LinkedFace3D linkedFace3D in contextFaces)
                {
                    faceHashes.Add(FaceVertexHash(linkedFace3D?.Face3D, tolerance));
                }
            }

            faceHashes.Sort(StringComparer.Ordinal);
            return Hash(string.Join("|", faceHashes));
        }

        /// <summary>
        /// Stable identity of the ordered analysis-cell set. Unlike the occluder hash this is
        /// ORDER-SENSITIVE (cached visibility arrays are indexed by cell position) and
        /// ORIENTATION-SENSITIVE (a flipped cell normal flips the front-facing test and the
        /// incidence angle). Each cell is canonicalised first: its plane normal (tolerance-rounded)
        /// plus its vertex rings rotated to start at their lexicographically smallest rounded
        /// vertex, so an equivalent polygon with a different start index produces the same hash
        /// while a flipped face never collides with the original.
        /// </summary>
        public static string TargetHash(this IList<AnalysisCell> analysisCells, double tolerance = Core.Tolerance.Distance)
        {
            if (analysisCells == null)
            {
                return Hash("null");
            }

            StringBuilder stringBuilder = new StringBuilder();
            for (int i = 0; i < analysisCells.Count; i++)
            {
                stringBuilder.Append(i);
                stringBuilder.Append(':');
                stringBuilder.Append(CellHash(analysisCells[i], tolerance));
                stringBuilder.Append('|');
            }

            return Hash(stringBuilder.ToString());
        }

        private static string CellHash(AnalysisCell analysisCell, double tolerance)
        {
            Face3D face3D = analysisCell?.Face3D;
            if (face3D == null || tolerance <= 0 || double.IsNaN(tolerance))
            {
                return "null";
            }

            Vector3D normal = face3D.GetPlane()?.Normal?.Unit;
            string normalHash = normal == null ? "null" : string.Format(
                System.Globalization.CultureInfo.InvariantCulture, "N({0},{1},{2})",
                (long)Math.Round(normal.X / tolerance),
                (long)Math.Round(normal.Y / tolerance),
                (long)Math.Round(normal.Z / tolerance));

            List<string> rings = new List<string>();
            List<IClosedPlanar3D> closedPlanar3Ds = face3D.GetEdge3Ds();
            if (closedPlanar3Ds != null)
            {
                foreach (IClosedPlanar3D closedPlanar3D in closedPlanar3Ds)
                {
                    string ring = CanonicalRing((closedPlanar3D as ISegmentable3D)?.GetPoints(), tolerance);
                    if (ring != null)
                    {
                        rings.Add(ring);
                    }
                }
            }

            // External ring first, internal rings sorted among themselves: identical topology,
            // regardless of ring authoring order beyond the external one.
            if (rings.Count > 1)
            {
                rings.Sort(1, rings.Count - 1, StringComparer.Ordinal);
            }

            return normalHash + string.Join(";", rings);
        }

        /// <summary>
        /// Vertex ring canonicalised by rotation to its lexicographically smallest rounded vertex.
        /// Ring ORDER is preserved (it encodes winding); only the start index is normalised.
        /// </summary>
        private static string CanonicalRing(List<Point3D> point3Ds, double tolerance)
        {
            if (point3Ds == null || point3Ds.Count == 0)
            {
                return null;
            }

            List<string> vertices = new List<string>(point3Ds.Count);
            foreach (Point3D point3D in point3Ds)
            {
                if (point3D == null)
                {
                    continue;
                }

                vertices.Add(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2}",
                    (long)Math.Round(point3D.X / tolerance),
                    (long)Math.Round(point3D.Y / tolerance),
                    (long)Math.Round(point3D.Z / tolerance)));
            }

            // Drop a repeated closing vertex so closed rings authored either way canonicalise equally.
            if (vertices.Count > 1 && vertices[0] == vertices[vertices.Count - 1])
            {
                vertices.RemoveAt(vertices.Count - 1);
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            int start = 0;
            for (int i = 1; i < vertices.Count; i++)
            {
                if (string.CompareOrdinal(vertices[i], vertices[start]) < 0)
                {
                    start = i;
                }
            }

            StringBuilder stringBuilder = new StringBuilder();
            for (int i = 0; i < vertices.Count; i++)
            {
                if (i > 0)
                {
                    stringBuilder.Append(';');
                }
                stringBuilder.Append(vertices[(start + i) % vertices.Count]);
            }

            return stringBuilder.ToString();
        }

        private static string FaceVertexHash(Face3D face3D, double tolerance)
        {
            if (face3D == null || tolerance <= 0 || double.IsNaN(tolerance))
            {
                return "null";
            }

            List<string> points = new List<string>();
            List<IClosedPlanar3D> closedPlanar3Ds = face3D.GetEdge3Ds();
            if (closedPlanar3Ds != null)
            {
                foreach (IClosedPlanar3D closedPlanar3D in closedPlanar3Ds)
                {
                    List<Point3D> point3Ds = (closedPlanar3D as ISegmentable3D)?.GetPoints();
                    if (point3Ds == null)
                    {
                        continue;
                    }

                    foreach (Point3D point3D in point3Ds)
                    {
                        if (point3D == null)
                        {
                            continue;
                        }

                        long x = (long)Math.Round(point3D.X / tolerance);
                        long y = (long)Math.Round(point3D.Y / tolerance);
                        long z = (long)Math.Round(point3D.Z / tolerance);
                        points.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2}", x, y, z));
                    }
                }
            }

            points.Sort(StringComparer.Ordinal);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "[{0}]{1}", points.Count, string.Join(";", points));
        }

        private static string Hash(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder stringBuilder = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                {
                    stringBuilder.Append(b.ToString("x2"));
                }

                return stringBuilder.ToString();
            }
        }
    }
}
