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
    /// Per sun group x analysis cell, WHICH face the sun hit first — the attribution twin of
    /// SolarVisibilityCache.
    ///
    /// It is a SEPARATE structure on purpose. The Stage 0-4 visibility cache is one bit per cell
    /// per sun group and is relied on by everything upstream; widening it to carry an occluder
    /// identity would multiply its size by 32 and change an accepted, released on-disk format for
    /// the benefit of one downstream stage. This cache is built alongside it and can be discarded
    /// without touching the visibility result.
    ///
    /// Storage is an occluder GUID table stored ONCE plus one 32-bit index per sample. A GUID per
    /// sample would be 16 bytes; the table plus an index is 4. int rather than ushort because a
    /// large model's context can exceed 65535 faces and silently wrapping the attribution of a real
    /// building is not a trade worth 2 bytes a sample.
    ///
    /// Two negative sentinels, never table indices:
    ///   FirstHitVisible    (-1) the ray reached the sun
    ///   FirstHitBackFacing (-2) the cell faces away; no ray was cast
    ///
    /// Identity covers the context geometry hash, the target geometry hash AND a hash of the
    /// attribution table itself, so a cache can never be reused against a geometry set whose faces
    /// have been added, removed or reordered — the indices would still resolve, silently, to the
    /// wrong faces. Attribution is only meaningful against the exact geometry it was built from, so
    /// the table is stored here rather than looked up against a mutable SolarModel at read time.
    /// </summary>
    public class SolarAttributionCache : IJSAMObject, ISolarObject
    {
        public const int CurrentSchemaVersion = 1;

        private int schemaVersion = CurrentSchemaVersion;
        private string contextGeometryHash;
        private string targetGeometryHash;
        private string attributionTableHash;
        private double cellSize = double.NaN;
        private double binSizeDegrees = double.NaN;
        private double sunPositionShiftInMinutes;
        private int year;
        private int cellCount;
        private int cellIndexOffset;
        private List<Guid> occluderGuids;
        private int[][] firstHit;

        public SolarAttributionCache(string contextGeometryHash, string targetGeometryHash, double cellSize, double binSizeDegrees, double sunPositionShiftInMinutes, int year, int cellCount, IEnumerable<Guid> occluderGuids, int[][] firstHit, int cellIndexOffset = 0)
        {
            this.cellIndexOffset = cellIndexOffset;
            this.contextGeometryHash = contextGeometryHash;
            this.targetGeometryHash = targetGeometryHash;
            this.cellSize = cellSize;
            this.binSizeDegrees = binSizeDegrees;
            this.sunPositionShiftInMinutes = sunPositionShiftInMinutes;
            this.year = year;
            this.cellCount = cellCount;
            this.occluderGuids = occluderGuids == null ? new List<Guid>() : new List<Guid>(occluderGuids);
            this.firstHit = Clone(firstHit);
            attributionTableHash = ComputeAttributionTableHash(this.occluderGuids);
        }

        public SolarAttributionCache(SolarAttributionCache solarAttributionCache)
        {
            if (solarAttributionCache != null)
            {
                schemaVersion = solarAttributionCache.schemaVersion;
                contextGeometryHash = solarAttributionCache.contextGeometryHash;
                targetGeometryHash = solarAttributionCache.targetGeometryHash;
                attributionTableHash = solarAttributionCache.attributionTableHash;
                cellSize = solarAttributionCache.cellSize;
                binSizeDegrees = solarAttributionCache.binSizeDegrees;
                sunPositionShiftInMinutes = solarAttributionCache.sunPositionShiftInMinutes;
                year = solarAttributionCache.year;
                cellCount = solarAttributionCache.cellCount;
                cellIndexOffset = solarAttributionCache.cellIndexOffset;
                occluderGuids = new List<Guid>(solarAttributionCache.occluderGuids ?? new List<Guid>());
                firstHit = Clone(solarAttributionCache.firstHit);
            }
        }

        public SolarAttributionCache(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public int SchemaVersion { get { return schemaVersion; } }

        public string ContextGeometryHash { get { return contextGeometryHash; } }

        public string TargetGeometryHash { get { return targetGeometryHash; } }

        /// <summary>Hash of the ordered occluder GUID table: pins the meaning of every stored index.</summary>
        public string AttributionTableHash { get { return attributionTableHash; } }

        public double CellSize { get { return cellSize; } }

        public double BinSizeDegrees { get { return binSizeDegrees; } }

        public double SunPositionShiftInMinutes { get { return sunPositionShiftInMinutes; } }

        public int Year { get { return year; } }

        /// <summary>Cells attributed here. This cache is indexed 0..CellCount-1, LOCALLY.</summary>
        public int CellCount { get { return cellCount; } }

        /// <summary>
        /// Where this cache's local cell 0 sits in the visibility cache's shared cell space. Read
        /// the visibility cache at CellIndexOffset + c where you read this one at c.
        /// </summary>
        public int CellIndexOffset { get { return cellIndexOffset; } }

        public int BinCount { get { return firstHit == null ? 0 : firstHit.Length; } }

        /// <summary>The stable occluder table. Stored index i means OccluderGuids[i].</summary>
        public List<Guid> OccluderGuids { get { return occluderGuids == null ? null : new List<Guid>(occluderGuids); } }

        public int OccluderCount { get { return occluderGuids == null ? 0 : occluderGuids.Count; } }

        /// <summary>Table index of the first face hit, or a FirstHit* sentinel.</summary>
        public int FirstHitIndex(int binIndex, int cellIndex)
        {
            if (firstHit == null || binIndex < 0 || binIndex >= firstHit.Length)
            {
                return Query.FirstHitBackFacing;
            }

            int[] bin = firstHit[binIndex];
            if (bin == null || cellIndex < 0 || cellIndex >= bin.Length)
            {
                return Query.FirstHitBackFacing;
            }

            return bin[cellIndex];
        }

        /// <summary>GUID of the first face hit, or Guid.Empty for either sentinel.</summary>
        public Guid FirstHitGuid(int binIndex, int cellIndex)
        {
            int index = FirstHitIndex(binIndex, cellIndex);
            return index < 0 || occluderGuids == null || index >= occluderGuids.Count ? Guid.Empty : occluderGuids[index];
        }

        /// <summary>Storage of the attribution payload, bytes: the table plus 4 bytes per sample.</summary>
        public long StorageBytes
        {
            get
            {
                return (long)BinCount * cellCount * 4 + (long)OccluderCount * 16;
            }
        }

        /// <summary>
        /// True when this cache was built from exactly the geometry and sampling described. The
        /// attribution-table hash is part of the test: matching context and target hashes are not
        /// enough, because the stored indices only mean anything against the table order they were
        /// written with.
        /// </summary>
        public bool Matches(string contextGeometryHash, string targetGeometryHash, string attributionTableHash, double cellSize, double binSizeDegrees, double sunPositionShiftInMinutes, int year, int cellCount)
        {
            return this.contextGeometryHash == contextGeometryHash
                && this.targetGeometryHash == targetGeometryHash
                && this.attributionTableHash == attributionTableHash
                && this.cellSize == cellSize
                && this.binSizeDegrees == binSizeDegrees
                && this.sunPositionShiftInMinutes == sunPositionShiftInMinutes
                && this.year == year
                && this.cellCount == cellCount;
        }

        /// <summary>Order-sensitive hash of a GUID table, as stored in the identity.</summary>
        public static string ComputeAttributionTableHash(IEnumerable<Guid> guids)
        {
            if (guids == null)
            {
                return null;
            }

            System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();
            foreach (Guid guid in guids)
            {
                stringBuilder.Append(guid.ToString("N"));
                stringBuilder.Append('|');
            }

            using (System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stringBuilder.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static int[][] Clone(int[][] values)
        {
            if (values == null)
            {
                return null;
            }

            int[][] result = new int[values.Length][];
            for (int i = 0; i < values.Length; i++)
            {
                result[i] = values[i] == null ? null : (int[])values[i].Clone();
            }

            return result;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("SchemaVersion")) { schemaVersion = jObject["SchemaVersion"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("ContextGeometryHash")) { contextGeometryHash = jObject["ContextGeometryHash"]?.GetValue<string>(); }
            if (jObject.ContainsKey("TargetGeometryHash")) { targetGeometryHash = jObject["TargetGeometryHash"]?.GetValue<string>(); }
            if (jObject.ContainsKey("AttributionTableHash")) { attributionTableHash = jObject["AttributionTableHash"]?.GetValue<string>(); }
            if (jObject.ContainsKey("CellSize")) { cellSize = jObject["CellSize"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("BinSizeDegrees")) { binSizeDegrees = jObject["BinSizeDegrees"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("SunPositionShiftInMinutes")) { sunPositionShiftInMinutes = jObject["SunPositionShiftInMinutes"]?.GetValue<double>() ?? default; }
            if (jObject.ContainsKey("Year")) { year = jObject["Year"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("CellCount")) { cellCount = jObject["CellCount"]?.GetValue<int>() ?? default; }
            cellIndexOffset = jObject.ContainsKey("CellIndexOffset") ? (jObject["CellIndexOffset"]?.GetValue<int>() ?? 0) : 0;

            occluderGuids = new List<Guid>();
            if (jObject.ContainsKey("OccluderGuids") && jObject["OccluderGuids"] is JsonArray guidArray)
            {
                foreach (JsonNode node in guidArray)
                {
                    if (Guid.TryParse(node?.GetValue<string>(), out Guid guid))
                    {
                        occluderGuids.Add(guid);
                    }
                }
            }

            firstHit = null;
            if (jObject.ContainsKey("FirstHit") && jObject["FirstHit"] is JsonArray binArray)
            {
                firstHit = new int[binArray.Count][];
                for (int b = 0; b < binArray.Count; b++)
                {
                    string base64 = binArray[b]?.GetValue<string>();
                    if (base64 == null)
                    {
                        continue;
                    }

                    byte[] bytes = System.Convert.FromBase64String(base64);
                    int[] values = new int[bytes.Length / 4];
                    Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
                    firstHit[b] = values;
                }
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("SchemaVersion", schemaVersion);
            if (contextGeometryHash != null) { jObject.Add("ContextGeometryHash", contextGeometryHash); }
            if (targetGeometryHash != null) { jObject.Add("TargetGeometryHash", targetGeometryHash); }
            if (attributionTableHash != null) { jObject.Add("AttributionTableHash", attributionTableHash); }
            jObject.Add("CellSize", cellSize);
            jObject.Add("BinSizeDegrees", binSizeDegrees);
            jObject.Add("SunPositionShiftInMinutes", sunPositionShiftInMinutes);
            jObject.Add("Year", year);
            jObject.Add("CellCount", cellCount);
            jObject.Add("CellIndexOffset", cellIndexOffset);

            JsonArray guidArray = new JsonArray();
            foreach (Guid guid in occluderGuids ?? new List<Guid>())
            {
                guidArray.Add(guid.ToString());
            }
            jObject.Add("OccluderGuids", guidArray);

            if (firstHit != null)
            {
                JsonArray binArray = new JsonArray();
                foreach (int[] values in firstHit)
                {
                    if (values == null)
                    {
                        binArray.Add(null);
                        continue;
                    }

                    byte[] bytes = new byte[values.Length * 4];
                    Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                    binArray.Add(System.Convert.ToBase64String(bytes));
                }

                jObject.Add("FirstHit", binArray);
            }

            return jObject;
        }
    }
}
