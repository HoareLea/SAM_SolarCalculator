// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.Weather.SolarCalculator
{
    /// <summary>
    /// Per-cell direct-beam visibility cache: for every sun bin, a packed bitset marking which
    /// analysis cells are lit by that bin's representative sun direction. Geometry-expensive to
    /// build once; any AnalysisPeriod is then evaluated arithmetically, with no geometric pass.
    ///
    /// The cache is deliberately WEATHER-INDEPENDENT: bins are defined by solar geometry (bin
    /// centres), so the same cache remains valid for any weather file and any AnalysisPeriod within
    /// the cache's year. The identity string covers everything that must invalidate the cache:
    /// schema/algorithm version, order-independent occluder hash, ORDER- and ORIENTATION-sensitive
    /// analysis-cell hash, cell size, bin angular resolution, minimum horizon angle, tolerances,
    /// and the sun-position drivers (latitude, longitude, fractional UTC offset, year, and the
    /// sun-position sampling shift of the timeline the bins were built for).
    /// </summary>
    public class SolarVisibilityCache : IJSAMObject, ISolarObject
    {
        /// <summary>Cache schema/algorithm version. Bump whenever the layout or raycast changes.</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>Visibility algorithm tag recorded in the identity.</summary>
        public const string Algorithm = "CellRaycast";

        private int schemaVersion = CurrentSchemaVersion;
        private string contextGeometryHash;
        private string targetGeometryHash;
        private double cellSize = double.NaN;
        private double binSizeDegrees = double.NaN;
        private double minHorizonAngle = double.NaN;
        private double tolerance_Area = double.NaN;
        private double tolerance_Snap = double.NaN;
        private double tolerance_Angle = double.NaN;
        private double tolerance_Distance = double.NaN;
        private double latitude = double.NaN;
        private double longitude = double.NaN;
        private double timeZoneOffset = double.NaN;
        private double sunPositionShiftInMinutes;
        private int year;
        private int cellCount;
        private List<SunBin> bins;
        private ulong[][] litBits;

        [NonSerialized]
        private Dictionary<Tuple<int, int>, int> binIndexByCoordinates;

        public SolarVisibilityCache(string contextGeometryHash, string targetGeometryHash, double cellSize, double binSizeDegrees, double minHorizonAngle, double tolerance_Area, double tolerance_Snap, double tolerance_Angle, double tolerance_Distance, double latitude, double longitude, double timeZoneOffset, double sunPositionShiftInMinutes, int year, int cellCount, IEnumerable<SunBin> bins, ulong[][] litBits)
        {
            this.contextGeometryHash = contextGeometryHash;
            this.targetGeometryHash = targetGeometryHash;
            this.cellSize = cellSize;
            this.binSizeDegrees = binSizeDegrees;
            this.minHorizonAngle = minHorizonAngle;
            this.tolerance_Area = tolerance_Area;
            this.tolerance_Snap = tolerance_Snap;
            this.tolerance_Angle = tolerance_Angle;
            this.tolerance_Distance = tolerance_Distance;
            this.latitude = latitude;
            this.longitude = longitude;
            this.timeZoneOffset = timeZoneOffset;
            this.sunPositionShiftInMinutes = sunPositionShiftInMinutes;
            this.year = year;
            this.cellCount = cellCount;
            this.bins = bins == null ? null : new List<SunBin>(bins);
            this.litBits = litBits;
            binIndexByCoordinates = null;
        }

        public SolarVisibilityCache(SolarVisibilityCache solarVisibilityCache)
        {
            if (solarVisibilityCache != null)
            {
                schemaVersion = solarVisibilityCache.schemaVersion;
                contextGeometryHash = solarVisibilityCache.contextGeometryHash;
                targetGeometryHash = solarVisibilityCache.targetGeometryHash;
                cellSize = solarVisibilityCache.cellSize;
                binSizeDegrees = solarVisibilityCache.binSizeDegrees;
                minHorizonAngle = solarVisibilityCache.minHorizonAngle;
                tolerance_Area = solarVisibilityCache.tolerance_Area;
                tolerance_Snap = solarVisibilityCache.tolerance_Snap;
                tolerance_Angle = solarVisibilityCache.tolerance_Angle;
                tolerance_Distance = solarVisibilityCache.tolerance_Distance;
                latitude = solarVisibilityCache.latitude;
                longitude = solarVisibilityCache.longitude;
                timeZoneOffset = solarVisibilityCache.timeZoneOffset;
                sunPositionShiftInMinutes = solarVisibilityCache.sunPositionShiftInMinutes;
                year = solarVisibilityCache.year;
                cellCount = solarVisibilityCache.cellCount;
                bins = solarVisibilityCache.bins?.ConvertAll(x => x == null ? null : new SunBin(x));
                if (solarVisibilityCache.litBits != null)
                {
                    litBits = new ulong[solarVisibilityCache.litBits.Length][];
                    for (int i = 0; i < litBits.Length; i++)
                    {
                        litBits[i] = solarVisibilityCache.litBits[i] == null ? null : (ulong[])solarVisibilityCache.litBits[i].Clone();
                    }
                }
                binIndexByCoordinates = null;
            }
        }

        public SolarVisibilityCache(JsonObject jObject)
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

        public double CellSize
        {
            get
            {
                return cellSize;
            }
        }

        public double BinSizeDegrees
        {
            get
            {
                return binSizeDegrees;
            }
        }

        public double MinHorizonAngle
        {
            get
            {
                return minHorizonAngle;
            }
        }

        /// <summary>Fractional UTC offset in hours (e.g. 5.5 for UTC+05:30) the bins were built with.</summary>
        public double TimeZoneOffset
        {
            get
            {
                return timeZoneOffset;
            }
        }

        /// <summary>
        /// Sun-position sampling shift of the timeline the bins were built for, in minutes
        /// (e.g. +30 for an interval-start EPW weather timeline, -30 for TAS EDSL compatibility).
        /// Bin membership is only meaningful when the evaluation applies the same shift.
        /// </summary>
        public double SunPositionShiftInMinutes
        {
            get
            {
                return sunPositionShiftInMinutes;
            }
        }

        public int Year
        {
            get
            {
                return year;
            }
        }

        public int CellCount
        {
            get
            {
                return cellCount;
            }
        }

        public int BinCount
        {
            get
            {
                return bins?.Count ?? 0;
            }
        }

        public List<SunBin> Bins
        {
            get
            {
                return bins?.ConvertAll(x => x == null ? null : new SunBin(x));
            }
        }

        /// <summary>
        /// Everything that must match for this cache to be reusable, as one comparable string.
        /// Weather data and AnalysisPeriod are intentionally NOT part of the identity.
        /// </summary>
        public string GetIdentity()
        {
            return string.Join(";",
                schemaVersion,
                Algorithm,
                contextGeometryHash,
                targetGeometryHash,
                cellSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                binSizeDegrees.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                minHorizonAngle.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                tolerance_Area.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                tolerance_Snap.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                tolerance_Angle.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                tolerance_Distance.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                latitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                longitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                timeZoneOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                sunPositionShiftInMinutes.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                year,
                cellCount);
        }

        public bool HasSameIdentity(SolarVisibilityCache solarVisibilityCache)
        {
            return solarVisibilityCache != null && GetIdentity() == solarVisibilityCache.GetIdentity();
        }

        /// <summary>
        /// True when this cache matches every identity input of a would-be build. Use it to decide
        /// reuse without rebuilding: any change in occluder geometry, cell geometry/order/orientation,
        /// cell size, bin resolution, tolerances, location, timezone, sun-position shift or year
        /// returns false.
        /// </summary>
        public bool Matches(string contextGeometryHash, string targetGeometryHash, double cellSize, double binSizeDegrees, double minHorizonAngle, double tolerance_Area, double tolerance_Snap, double tolerance_Angle, double tolerance_Distance, double latitude, double longitude, double timeZoneOffset, double sunPositionShiftInMinutes, int year, int cellCount)
        {
            return schemaVersion == CurrentSchemaVersion
                && this.contextGeometryHash == contextGeometryHash
                && this.targetGeometryHash == targetGeometryHash
                && this.cellSize == cellSize
                && this.binSizeDegrees == binSizeDegrees
                && this.minHorizonAngle == minHorizonAngle
                && this.tolerance_Area == tolerance_Area
                && this.tolerance_Snap == tolerance_Snap
                && this.tolerance_Angle == tolerance_Angle
                && this.tolerance_Distance == tolerance_Distance
                && this.latitude == latitude
                && this.longitude == longitude
                && this.timeZoneOffset == timeZoneOffset
                && this.sunPositionShiftInMinutes == sunPositionShiftInMinutes
                && this.year == year
                && this.cellCount == cellCount;
        }

        /// <summary>Bin index for the given solar angles, or -1 when no bin covers them.</summary>
        public int FindBin(double altitude, double azimuth)
        {
            if (bins == null || double.IsNaN(altitude) || double.IsNaN(azimuth))
            {
                return -1;
            }

            if (binIndexByCoordinates == null)
            {
                binIndexByCoordinates = new Dictionary<Tuple<int, int>, int>();
                foreach (SunBin sunBin in bins)
                {
                    if (sunBin != null)
                    {
                        binIndexByCoordinates[new Tuple<int, int>(sunBin.AltitudeBin, sunBin.AzimuthBin)] = sunBin.Index;
                    }
                }
            }

            Create.BinCoordinates(altitude, azimuth, binSizeDegrees, out int altitudeBin, out int azimuthBin);
            return binIndexByCoordinates.TryGetValue(new Tuple<int, int>(altitudeBin, azimuthBin), out int index) ? index : -1;
        }

        /// <summary>True when the analysis cell is lit by direct beam for the given bin index.</summary>
        public bool IsLit(int binIndex, int cellIndex)
        {
            if (litBits == null || binIndex < 0 || binIndex >= litBits.Length)
            {
                return false;
            }

            ulong[] bits = litBits[binIndex];
            if (bits == null || cellIndex < 0 || cellIndex >= cellCount)
            {
                return false;
            }

            return (bits[cellIndex >> 6] & (1UL << (cellIndex & 63))) != 0;
        }

        /// <summary>Raw packed bits for a bin (cellCount bits, little-endian ulong packing). Build-time use.</summary>
        public ulong[] GetBits(int binIndex)
        {
            if (litBits == null || binIndex < 0 || binIndex >= litBits.Length)
            {
                return null;
            }

            return litBits[binIndex];
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

            if (jObject.ContainsKey("BinSizeDegrees"))
            {
                binSizeDegrees = jObject["BinSizeDegrees"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("MinHorizonAngle"))
            {
                minHorizonAngle = jObject["MinHorizonAngle"]?.GetValue<double>() ?? double.NaN;
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

            if (jObject.ContainsKey("Latitude"))
            {
                latitude = jObject["Latitude"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("Longitude"))
            {
                longitude = jObject["Longitude"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("TimeZoneOffset"))
            {
                timeZoneOffset = jObject["TimeZoneOffset"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("SunPositionShiftInMinutes"))
            {
                sunPositionShiftInMinutes = jObject["SunPositionShiftInMinutes"]?.GetValue<double>() ?? default;
            }

            if (jObject.ContainsKey("Year"))
            {
                year = jObject["Year"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("CellCount"))
            {
                cellCount = jObject["CellCount"]?.GetValue<int>() ?? default;
            }

            bins = null;
            if (jObject.ContainsKey("Bins"))
            {
                JsonArray jArray = jObject["Bins"] as JsonArray;
                if (jArray != null)
                {
                    bins = new List<SunBin>();
                    foreach (JsonNode jNode in jArray)
                    {
                        bins.Add(Core.Create.IJSAMObject<SunBin>(jNode as JsonObject));
                    }
                }
            }

            litBits = null;
            if (jObject.ContainsKey("LitBits"))
            {
                JsonArray jArray = jObject["LitBits"] as JsonArray;
                if (jArray != null)
                {
                    litBits = new ulong[jArray.Count][];
                    for (int i = 0; i < jArray.Count; i++)
                    {
                        litBits[i] = FromBase64(jArray[i]?.GetValue<string>());
                    }
                }
            }

            binIndexByCoordinates = null;
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
            jObject.Add("BinSizeDegrees", binSizeDegrees);
            jObject.Add("MinHorizonAngle", minHorizonAngle);
            jObject.Add("Tolerance_Area", tolerance_Area);
            jObject.Add("Tolerance_Snap", tolerance_Snap);
            jObject.Add("Tolerance_Angle", tolerance_Angle);
            jObject.Add("Tolerance_Distance", tolerance_Distance);
            jObject.Add("Latitude", latitude);
            jObject.Add("Longitude", longitude);
            jObject.Add("TimeZoneOffset", timeZoneOffset);
            jObject.Add("SunPositionShiftInMinutes", sunPositionShiftInMinutes);
            jObject.Add("Year", year);
            jObject.Add("CellCount", cellCount);

            if (bins != null)
            {
                JsonArray jArray = new JsonArray();
                foreach (SunBin sunBin in bins)
                {
                    jArray.Add(sunBin?.ToJsonObject());
                }
                jObject.Add("Bins", jArray);
            }

            if (litBits != null)
            {
                JsonArray jArray = new JsonArray();
                foreach (ulong[] bits in litBits)
                {
                    jArray.Add(ToBase64(bits));
                }
                jObject.Add("LitBits", jArray);
            }

            return jObject;
        }

        internal static ulong[][] CreateBitStorage(int binCount, int cellCount)
        {
            int words = (cellCount + 63) >> 6;
            ulong[][] result = new ulong[binCount][];
            for (int i = 0; i < binCount; i++)
            {
                result[i] = new ulong[words];
            }

            return result;
        }

        private static string ToBase64(ulong[] bits)
        {
            if (bits == null)
            {
                return null;
            }

            byte[] bytes = new byte[bits.Length * 8];
            Buffer.BlockCopy(bits, 0, bytes, 0, bytes.Length);
            return System.Convert.ToBase64String(bytes);
        }

        private static ulong[] FromBase64(string base64)
        {
            if (base64 == null)
            {
                return null;
            }

            byte[] bytes = System.Convert.FromBase64String(base64);
            ulong[] result = new ulong[bytes.Length / 8];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
    }
}
