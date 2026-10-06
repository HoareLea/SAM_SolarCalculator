// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// The candidate shading space in front of one aperture: a regular voxel grid in the
    /// aperture-local frame. The frame is orthonormal: axisZ = the aperture OUTWARD normal
    /// (shading is external only, so local z runs from 0 at the aperture plane to maxDepth),
    /// axisX/axisY = the ApertureSolarTarget's horizontal / up-slope axes. The grid covers the
    /// aperture's local 2D bounding box expanded by the left/right/up/down margins.
    ///
    /// Voxel linear index is deterministic: index = i + countX x (j + countY x k), x fastest.
    /// Voxel (i, j, k) spans local [i.vs, (i+1).vs) x [j.vs, (j+1).vs) x [k.vs, (k+1).vs) from the
    /// volume origin (the world position of the minimum corner).
    /// </summary>
    public class ShadingVolume : IJSAMObject, ISolarObject
    {
        private Point3D origin;
        private Vector3D axisX;
        private Vector3D axisY;
        private Vector3D axisZ;
        private double voxelSize = double.NaN;
        private int countX;
        private int countY;
        private int countZ;

        public ShadingVolume(Point3D origin, Vector3D axisX, Vector3D axisY, Vector3D axisZ, double voxelSize, int countX, int countY, int countZ)
        {
            this.origin = origin == null ? null : Core.Query.Clone(origin);
            this.axisX = axisX == null ? null : new Vector3D(axisX).Unit;
            this.axisY = axisY == null ? null : new Vector3D(axisY).Unit;
            this.axisZ = axisZ == null ? null : new Vector3D(axisZ).Unit;
            this.voxelSize = voxelSize;
            this.countX = countX;
            this.countY = countY;
            this.countZ = countZ;
        }

        public ShadingVolume(ShadingVolume shadingVolume)
        {
            if (shadingVolume != null)
            {
                origin = shadingVolume.origin == null ? null : Core.Query.Clone(shadingVolume.origin);
                axisX = shadingVolume.axisX == null ? null : new Vector3D(shadingVolume.axisX);
                axisY = shadingVolume.axisY == null ? null : new Vector3D(shadingVolume.axisY);
                axisZ = shadingVolume.axisZ == null ? null : new Vector3D(shadingVolume.axisZ);
                voxelSize = shadingVolume.voxelSize;
                countX = shadingVolume.countX;
                countY = shadingVolume.countY;
                countZ = shadingVolume.countZ;
            }
        }

        public ShadingVolume(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>World position of the minimum corner of voxel (0, 0, 0).</summary>
        public Point3D Origin
        {
            get
            {
                return origin == null ? null : Core.Query.Clone(origin);
            }
        }

        /// <summary>Local X axis (aperture-horizontal), unit, world components.</summary>
        public Vector3D AxisX
        {
            get
            {
                return axisX == null ? null : new Vector3D(axisX);
            }
        }

        /// <summary>Local Y axis (aperture up-slope), unit, world components.</summary>
        public Vector3D AxisY
        {
            get
            {
                return axisY == null ? null : new Vector3D(axisY);
            }
        }

        /// <summary>Local Z axis (aperture outward normal), unit, world components.</summary>
        public Vector3D AxisZ
        {
            get
            {
                return axisZ == null ? null : new Vector3D(axisZ);
            }
        }

        /// <summary>
        /// Largest absolute world coordinate of the volume origin — the magnitude that bounds the
        /// cancellation error in TryToLocal, and therefore the achievable precision of every local
        /// coordinate derived from this volume. Exposed without cloning the origin because the
        /// voxel traversal reads it per ray.
        /// </summary>
        public double OriginMagnitude
        {
            get
            {
                if (origin == null)
                {
                    return 0.0;
                }

                double result = System.Math.Abs(origin.X);
                result = System.Math.Max(result, System.Math.Abs(origin.Y));
                return System.Math.Max(result, System.Math.Abs(origin.Z));
            }
        }

        public double VoxelSize
        {
            get
            {
                return voxelSize;
            }
        }

        public int CountX
        {
            get
            {
                return countX;
            }
        }

        public int CountY
        {
            get
            {
                return countY;
            }
        }

        public int CountZ
        {
            get
            {
                return countZ;
            }
        }

        public int VoxelCount
        {
            get
            {
                return countX * countY * countZ;
            }
        }

        /// <summary>Maximum projection depth from the aperture plane (local z extent), m.</summary>
        public double MaxDepth
        {
            get
            {
                return countZ * voxelSize;
            }
        }

        /// <summary>Deterministic linear voxel index: i + countX x (j + countY x k).</summary>
        public int VoxelIndex(int i, int j, int k)
        {
            return i + countX * (j + countY * k);
        }

        /// <summary>World position of a voxel's centre, or null for an invalid index.</summary>
        public Point3D GetCentre(int voxelIndex)
        {
            if (voxelIndex < 0 || voxelIndex >= VoxelCount || origin == null)
            {
                return null;
            }

            int i = voxelIndex % countX;
            int j = (voxelIndex / countX) % countY;
            int k = voxelIndex / (countX * countY);

            return ToWorld((i + 0.5) * voxelSize, (j + 0.5) * voxelSize, (k + 0.5) * voxelSize);
        }

        /// <summary>World position of a local-frame point (metres from the volume origin).</summary>
        public Point3D ToWorld(double x, double y, double z)
        {
            if (origin == null || axisX == null || axisY == null || axisZ == null)
            {
                return null;
            }

            return new Point3D(
                origin.X + x * axisX.X + y * axisY.X + z * axisZ.X,
                origin.Y + x * axisX.Y + y * axisY.Y + z * axisZ.Y,
                origin.Z + x * axisX.Z + y * axisY.Z + z * axisZ.Z);
        }

        /// <summary>Local-frame coordinates (metres from the volume origin) of a world point.</summary>
        public bool TryToLocal(Point3D point3D, out double x, out double y, out double z)
        {
            x = double.NaN;
            y = double.NaN;
            z = double.NaN;

            if (point3D == null || origin == null || axisX == null || axisY == null || axisZ == null)
            {
                return false;
            }

            double dx = point3D.X - origin.X;
            double dy = point3D.Y - origin.Y;
            double dz = point3D.Z - origin.Z;

            x = dx * axisX.X + dy * axisX.Y + dz * axisX.Z;
            y = dx * axisY.X + dy * axisY.Y + dz * axisY.Z;
            z = dx * axisZ.X + dy * axisZ.Y + dz * axisZ.Z;
            return true;
        }

        /// <summary>Voxel indices containing a world point, or false when outside the grid.</summary>
        public bool TryGetVoxelIndices(Point3D point3D, out int i, out int j, out int k)
        {
            i = -1;
            j = -1;
            k = -1;

            if (!TryToLocal(point3D, out double x, out double y, out double z))
            {
                return false;
            }

            i = (int)System.Math.Floor(x / voxelSize);
            j = (int)System.Math.Floor(y / voxelSize);
            k = (int)System.Math.Floor(z / voxelSize);

            return i >= 0 && i < countX && j >= 0 && j < countY && k >= 0 && k < countZ;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Origin"))
            {
                origin = new Point3D(jObject["Origin"] as JsonObject);
            }

            if (jObject.ContainsKey("AxisX"))
            {
                axisX = new Vector3D(jObject["AxisX"] as JsonObject);
            }

            if (jObject.ContainsKey("AxisY"))
            {
                axisY = new Vector3D(jObject["AxisY"] as JsonObject);
            }

            if (jObject.ContainsKey("AxisZ"))
            {
                axisZ = new Vector3D(jObject["AxisZ"] as JsonObject);
            }

            if (jObject.ContainsKey("VoxelSize"))
            {
                voxelSize = jObject["VoxelSize"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("CountX"))
            {
                countX = jObject["CountX"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("CountY"))
            {
                countY = jObject["CountY"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("CountZ"))
            {
                countZ = jObject["CountZ"]?.GetValue<int>() ?? default;
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            if (origin != null)
            {
                jObject.Add("Origin", origin.ToJsonObject());
            }

            if (axisX != null)
            {
                jObject.Add("AxisX", axisX.ToJsonObject());
            }

            if (axisY != null)
            {
                jObject.Add("AxisY", axisY.ToJsonObject());
            }

            if (axisZ != null)
            {
                jObject.Add("AxisZ", axisZ.ToJsonObject());
            }

            jObject.Add("VoxelSize", voxelSize);
            jObject.Add("CountX", countX);
            jObject.Add("CountY", countY);
            jObject.Add("CountZ", countZ);

            return jObject;
        }
    }
}
