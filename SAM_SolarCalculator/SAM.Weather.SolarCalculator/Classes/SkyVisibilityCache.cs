// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Weather.SolarCalculator
{
    /// <summary>
    /// Per-cell directional sky/ground visibility, built by ray-casting every sky/ground patch
    /// direction once. Stores, per analysis cell:
    ///   SkyViewFactor      - cosine-weighted visible fraction of the sky dome
    ///                        (unobstructed: (1+cosB)/2; vertical = 0.5). Weights the Perez
    ///                        isotropic diffuse component.
    ///   HorizonViewFactor  - cosine-weighted visible fraction of the lowest sky band,
    ///                        normalised to 1 when the whole band is visible. Weights the Perez
    ///                        horizon-brightening component.
    ///   GroundViewFactor   - cosine-weighted visible fraction of the ground dome (isotropic
    ///                        ground assumed; unobstructed: (1-cosB)/2). Weights ground-reflected.
    /// The circumsolar component uses the direct-beam SolarVisibilityCache (obstruction in the
    /// actual sun direction), so all three anisotropic diffuse components are handled separately.
    /// Identity covers schema/algorithm version, patch subdivision, order-independent occluder
    /// hash, ORDER- and ORIENTATION-sensitive cell hash, cell size and tolerances; it is
    /// independent of location, year, weather and AnalysisPeriod.
    /// </summary>
    public class SkyVisibilityCache : IJSAMObject, ISolarObject
    {
        public const int CurrentSchemaVersion = 2;

        public const string Algorithm = "PatchRaycast";

        private int schemaVersion = CurrentSchemaVersion;
        private string contextGeometryHash;
        private string targetGeometryHash;
        private double cellSize = double.NaN;
        private double tolerance_Area = double.NaN;
        private double tolerance_Snap = double.NaN;
        private double tolerance_Angle = double.NaN;
        private double tolerance_Distance = double.NaN;
        private Geometry.SolarCalculator.SkyPatchSubdivision skyPatchSubdivision = Geometry.SolarCalculator.SkyPatchSubdivision.Undefined;
        private int cellCount;
        private double[] skyViewFactors;
        private double[] horizonViewFactors;
        private double[] groundViewFactors;

        public SkyVisibilityCache(string contextGeometryHash, string targetGeometryHash, double cellSize, double tolerance_Area, double tolerance_Snap, double tolerance_Angle, double tolerance_Distance, Geometry.SolarCalculator.SkyPatchSubdivision skyPatchSubdivision, int cellCount, double[] skyViewFactors, double[] horizonViewFactors, double[] groundViewFactors)
        {
            this.contextGeometryHash = contextGeometryHash;
            this.targetGeometryHash = targetGeometryHash;
            this.cellSize = cellSize;
            this.tolerance_Area = tolerance_Area;
            this.tolerance_Snap = tolerance_Snap;
            this.tolerance_Angle = tolerance_Angle;
            this.tolerance_Distance = tolerance_Distance;
            this.skyPatchSubdivision = skyPatchSubdivision;
            this.cellCount = cellCount;
            this.skyViewFactors = skyViewFactors;
            this.horizonViewFactors = horizonViewFactors;
            this.groundViewFactors = groundViewFactors;
        }

        public SkyVisibilityCache(SkyVisibilityCache skyVisibilityCache)
        {
            if (skyVisibilityCache != null)
            {
                schemaVersion = skyVisibilityCache.schemaVersion;
                contextGeometryHash = skyVisibilityCache.contextGeometryHash;
                targetGeometryHash = skyVisibilityCache.targetGeometryHash;
                cellSize = skyVisibilityCache.cellSize;
                tolerance_Area = skyVisibilityCache.tolerance_Area;
                tolerance_Snap = skyVisibilityCache.tolerance_Snap;
                tolerance_Angle = skyVisibilityCache.tolerance_Angle;
                tolerance_Distance = skyVisibilityCache.tolerance_Distance;
                skyPatchSubdivision = skyVisibilityCache.skyPatchSubdivision;
                cellCount = skyVisibilityCache.cellCount;
                skyViewFactors = skyVisibilityCache.skyViewFactors == null ? null : (double[])skyVisibilityCache.skyViewFactors.Clone();
                horizonViewFactors = skyVisibilityCache.horizonViewFactors == null ? null : (double[])skyVisibilityCache.horizonViewFactors.Clone();
                groundViewFactors = skyVisibilityCache.groundViewFactors == null ? null : (double[])skyVisibilityCache.groundViewFactors.Clone();
            }
        }

        public SkyVisibilityCache(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public int SchemaVersion
        {
            get
            {
                return schemaVersion;
            }
        }

        /// <summary>Order-independent hash of the occluder geometry.</summary>
        public string ContextGeometryHash
        {
            get
            {
                return contextGeometryHash;
            }
        }

        /// <summary>Order- and orientation-sensitive hash of the analysis cells.</summary>
        public string TargetGeometryHash
        {
            get
            {
                return targetGeometryHash;
            }
        }

        public Geometry.SolarCalculator.SkyPatchSubdivision SkyPatchSubdivision
        {
            get
            {
                return skyPatchSubdivision;
            }
        }

        public int CellCount
        {
            get
            {
                return cellCount;
            }
        }

        public string GetIdentity()
        {
            return string.Join(";",
                schemaVersion,
                Algorithm,
                skyPatchSubdivision,
                contextGeometryHash,
                targetGeometryHash,
                cellSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                tolerance_Area.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                tolerance_Snap.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                tolerance_Angle.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                tolerance_Distance.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                cellCount);
        }

        public bool HasSameIdentity(SkyVisibilityCache skyVisibilityCache)
        {
            return skyVisibilityCache != null && GetIdentity() == skyVisibilityCache.GetIdentity();
        }

        /// <summary>
        /// True when this cache matches every identity input of a would-be build. Independent of
        /// location, year, weather and AnalysisPeriod by construction.
        /// </summary>
        public bool Matches(string contextGeometryHash, string targetGeometryHash, double cellSize, Geometry.SolarCalculator.SkyPatchSubdivision skyPatchSubdivision, double tolerance_Area, double tolerance_Snap, double tolerance_Angle, double tolerance_Distance, int cellCount)
        {
            return schemaVersion == CurrentSchemaVersion
                && this.contextGeometryHash == contextGeometryHash
                && this.targetGeometryHash == targetGeometryHash
                && this.cellSize == cellSize
                && this.skyPatchSubdivision == skyPatchSubdivision
                && this.tolerance_Area == tolerance_Area
                && this.tolerance_Snap == tolerance_Snap
                && this.tolerance_Angle == tolerance_Angle
                && this.tolerance_Distance == tolerance_Distance
                && this.cellCount == cellCount;
        }

        /// <summary>Cosine-weighted sky view factor of a cell (unobstructed vertical = 0.5).</summary>
        public double SkyViewFactor(int cellIndex)
        {
            return skyViewFactors != null && cellIndex >= 0 && cellIndex < skyViewFactors.Length ? skyViewFactors[cellIndex] : double.NaN;
        }

        /// <summary>Cosine-weighted visible fraction of the horizon band (1 = band fully visible).</summary>
        public double HorizonViewFactor(int cellIndex)
        {
            return horizonViewFactors != null && cellIndex >= 0 && cellIndex < horizonViewFactors.Length ? horizonViewFactors[cellIndex] : double.NaN;
        }

        /// <summary>Cosine-weighted ground view factor of a cell (unobstructed vertical = 0.5).</summary>
        public double GroundViewFactor(int cellIndex)
        {
            return groundViewFactors != null && cellIndex >= 0 && cellIndex < groundViewFactors.Length ? groundViewFactors[cellIndex] : double.NaN;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("SchemaVersion"))
            {
                schemaVersion = jObject["SchemaVersion"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("ContextGeometryHash"))
            {
                contextGeometryHash = jObject["ContextGeometryHash"]?.GetValue<string>();
            }

            if (jObject.ContainsKey("TargetGeometryHash"))
            {
                targetGeometryHash = jObject["TargetGeometryHash"]?.GetValue<string>();
            }

            if (jObject.ContainsKey("CellSize"))
            {
                cellSize = jObject["CellSize"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("Tolerance_Area"))
            {
                tolerance_Area = jObject["Tolerance_Area"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("Tolerance_Snap"))
            {
                tolerance_Snap = jObject["Tolerance_Snap"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("Tolerance_Angle"))
            {
                tolerance_Angle = jObject["Tolerance_Angle"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("Tolerance_Distance"))
            {
                tolerance_Distance = jObject["Tolerance_Distance"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("SkyPatchSubdivision"))
            {
                Enum.TryParse(jObject["SkyPatchSubdivision"]?.GetValue<string>(), out skyPatchSubdivision);
            }

            if (jObject.ContainsKey("CellCount"))
            {
                cellCount = jObject["CellCount"]?.GetValue<int>() ?? default;
            }

            skyViewFactors = Doubles(jObject, "SkyViewFactors");
            horizonViewFactors = Doubles(jObject, "HorizonViewFactors");
            groundViewFactors = Doubles(jObject, "GroundViewFactors");

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("SchemaVersion", schemaVersion);

            if (contextGeometryHash != null)
            {
                jObject.Add("ContextGeometryHash", contextGeometryHash);
            }

            if (targetGeometryHash != null)
            {
                jObject.Add("TargetGeometryHash", targetGeometryHash);
            }

            jObject.Add("CellSize", cellSize);
            jObject.Add("Tolerance_Area", tolerance_Area);
            jObject.Add("Tolerance_Snap", tolerance_Snap);
            jObject.Add("Tolerance_Angle", tolerance_Angle);
            jObject.Add("Tolerance_Distance", tolerance_Distance);
            jObject.Add("SkyPatchSubdivision", skyPatchSubdivision.ToString());
            jObject.Add("CellCount", cellCount);

            Add(jObject, "SkyViewFactors", skyViewFactors);
            Add(jObject, "HorizonViewFactors", horizonViewFactors);
            Add(jObject, "GroundViewFactors", groundViewFactors);

            return jObject;
        }

        private static void Add(JsonObject jObject, string name, double[] values)
        {
            if (values == null)
            {
                return;
            }

            JsonArray jArray = new JsonArray();
            foreach (double value in values)
            {
                jArray.Add(value);
            }
            jObject.Add(name, jArray);
        }

        private static double[] Doubles(JsonObject jObject, string name)
        {
            if (!jObject.ContainsKey(name))
            {
                return null;
            }

            JsonArray jArray = jObject[name] as JsonArray;
            if (jArray == null)
            {
                return null;
            }

            List<double> values = new List<double>(jArray.Count);
            foreach (JsonNode jNode in jArray)
            {
                values.Add(jNode?.GetValue<double>() ?? double.NaN);
            }

            return values.ToArray();
        }
    }
}
