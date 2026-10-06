// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// The analysis basis of one verified scheme, as a comparable fingerprint. Two schemes are
    /// comparable exactly when their signatures agree on every identity field; the comparison uses
    /// <see cref="SignatureHash"/> for the verdict and the field-by-field diff for the reason.
    ///
    /// Built ENTIRELY from a resolved <see cref="ApertureSolarContext"/> plus the brief, so it
    /// cannot drift from what was actually computed. <see cref="DesirabilityName"/> is carried for
    /// display only and is NEVER part of the comparison: two SeasonalDesirability instances with
    /// different periods share a class name and are not comparable, which is precisely the failure
    /// the <see cref="DesirabilityHash"/> prevents.
    /// </summary>
    public class ShadingAnalysisSignature : IJSAMObject, ISolarObject
    {
        private List<Guid> apertureGuids = new List<Guid>();
        private List<int> cellCounts = new List<int>();
        private string contextGeometryHash;
        private string targetGeometryHash;
        private double gridSize = double.NaN;
        private double sunAngleStep = double.NaN;
        private int year;
        private double timeShiftInMinutes;
        private double latitude = double.NaN;
        private double longitude = double.NaN;
        private double timeZoneOffset = double.NaN;
        private string weatherIdentityHash;
        private string desirabilityHash;
        private string desirabilityName;
        private string signatureHash;

        public ShadingAnalysisSignature(
            IEnumerable<Guid> apertureGuids,
            IEnumerable<int> cellCounts,
            string contextGeometryHash,
            string targetGeometryHash,
            double gridSize,
            double sunAngleStep,
            int year,
            double timeShiftInMinutes,
            double latitude,
            double longitude,
            double timeZoneOffset,
            string weatherIdentityHash,
            string desirabilityHash,
            string desirabilityName)
        {
            // Apertures sorted ordinal ascending; the cell counts travel WITH their aperture so the
            // two stay aligned whatever order the caller supplied them in (I12).
            List<Guid> guids = new List<Guid>(apertureGuids ?? new List<Guid>());
            List<int> counts = new List<int>(cellCounts ?? new List<int>());
            List<int> order = new List<int>();
            for (int i = 0; i < guids.Count; i++)
            {
                order.Add(i);
            }
            order.Sort((i, j) => guids[i].CompareTo(guids[j]));

            this.apertureGuids = new List<Guid>();
            this.cellCounts = new List<int>();
            foreach (int index in order)
            {
                this.apertureGuids.Add(guids[index]);
                this.cellCounts.Add(index < counts.Count ? counts[index] : 0);
            }

            this.contextGeometryHash = contextGeometryHash;
            this.targetGeometryHash = targetGeometryHash;
            this.gridSize = gridSize;
            this.sunAngleStep = sunAngleStep;
            this.year = year;
            this.timeShiftInMinutes = timeShiftInMinutes;
            this.latitude = latitude;
            this.longitude = longitude;
            this.timeZoneOffset = timeZoneOffset;
            this.weatherIdentityHash = weatherIdentityHash;
            this.desirabilityHash = desirabilityHash;
            this.desirabilityName = desirabilityName;
            signatureHash = ComputeSignatureHash();
        }

        public ShadingAnalysisSignature(ShadingAnalysisSignature shadingAnalysisSignature)
        {
            if (shadingAnalysisSignature != null)
            {
                apertureGuids = new List<Guid>(shadingAnalysisSignature.apertureGuids);
                cellCounts = new List<int>(shadingAnalysisSignature.cellCounts);
                contextGeometryHash = shadingAnalysisSignature.contextGeometryHash;
                targetGeometryHash = shadingAnalysisSignature.targetGeometryHash;
                gridSize = shadingAnalysisSignature.gridSize;
                sunAngleStep = shadingAnalysisSignature.sunAngleStep;
                year = shadingAnalysisSignature.year;
                timeShiftInMinutes = shadingAnalysisSignature.timeShiftInMinutes;
                latitude = shadingAnalysisSignature.latitude;
                longitude = shadingAnalysisSignature.longitude;
                timeZoneOffset = shadingAnalysisSignature.timeZoneOffset;
                weatherIdentityHash = shadingAnalysisSignature.weatherIdentityHash;
                desirabilityHash = shadingAnalysisSignature.desirabilityHash;
                desirabilityName = shadingAnalysisSignature.desirabilityName;
                signatureHash = shadingAnalysisSignature.signatureHash;
            }
        }

        public ShadingAnalysisSignature(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>The aperture scope, sorted ordinal ascending.</summary>
        public List<Guid> ApertureGuids { get { return new List<Guid>(apertureGuids); } }

        /// <summary>Cell count per aperture, in <see cref="ApertureGuids"/> order.</summary>
        public List<int> CellCounts { get { return new List<int>(cellCounts); } }

        public string ContextGeometryHash { get { return contextGeometryHash; } }

        public string TargetGeometryHash { get { return targetGeometryHash; } }

        public double GridSize { get { return gridSize; } }

        public double SunAngleStep { get { return sunAngleStep; } }

        public int Year { get { return year; } }

        public double TimeShiftInMinutes { get { return timeShiftInMinutes; } }

        public double Latitude { get { return latitude; } }

        public double Longitude { get { return longitude; } }

        public double TimeZoneOffset { get { return timeZoneOffset; } }

        /// <summary>Fingerprint of the weather actually used (location, timeline and the hourly radiation series).</summary>
        public string WeatherIdentityHash { get { return weatherIdentityHash; } }

        /// <summary>MD5 of the strategy's canonical JSON text — the comparison-grade identity of the brief.</summary>
        public string DesirabilityHash { get { return desirabilityHash; } }

        /// <summary>Display only. NEVER used for comparison.</summary>
        public string DesirabilityName { get { return desirabilityName; } }

        /// <summary>MD5 over every field above (except the display name), in this order.</summary>
        public string SignatureHash { get { return signatureHash; } }

        private string ComputeSignatureHash()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(string.Join(",", apertureGuids));
            stringBuilder.Append('|');
            stringBuilder.Append(string.Join(",", cellCounts));
            stringBuilder.Append('|');
            stringBuilder.Append(contextGeometryHash);
            stringBuilder.Append('|');
            stringBuilder.Append(targetGeometryHash);
            stringBuilder.Append('|');
            stringBuilder.Append(gridSize.ToString("R", CultureInfo.InvariantCulture));
            stringBuilder.Append('|');
            stringBuilder.Append(sunAngleStep.ToString("R", CultureInfo.InvariantCulture));
            stringBuilder.Append('|');
            stringBuilder.Append(year);
            stringBuilder.Append('|');
            stringBuilder.Append(timeShiftInMinutes.ToString("R", CultureInfo.InvariantCulture));
            stringBuilder.Append('|');
            stringBuilder.Append(latitude.ToString("R", CultureInfo.InvariantCulture));
            stringBuilder.Append('|');
            stringBuilder.Append(longitude.ToString("R", CultureInfo.InvariantCulture));
            stringBuilder.Append('|');
            stringBuilder.Append(timeZoneOffset.ToString("R", CultureInfo.InvariantCulture));
            stringBuilder.Append('|');
            stringBuilder.Append(weatherIdentityHash);
            stringBuilder.Append('|');
            stringBuilder.Append(desirabilityHash);

            using (MD5 md5 = MD5.Create())
            {
                return Hex(md5.ComputeHash(Encoding.UTF8.GetBytes(stringBuilder.ToString())));
            }
        }

        internal static string Hex(byte[] bytes)
        {
            StringBuilder stringBuilder = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                stringBuilder.Append(b.ToString("x2"));
            }

            return stringBuilder.ToString();
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            apertureGuids = ReadGuids(jObject, "ApertureGuids");

            cellCounts = new List<int>();
            if (jObject.ContainsKey("CellCounts") && jObject["CellCounts"] is JsonArray countsArray)
            {
                foreach (JsonNode node in countsArray)
                {
                    cellCounts.Add(node?.GetValue<int>() ?? 0);
                }
            }

            if (jObject.ContainsKey("ContextGeometryHash")) { contextGeometryHash = jObject["ContextGeometryHash"]?.GetValue<string>(); }
            if (jObject.ContainsKey("TargetGeometryHash")) { targetGeometryHash = jObject["TargetGeometryHash"]?.GetValue<string>(); }
            gridSize = Read(jObject, "GridSize");
            sunAngleStep = Read(jObject, "SunAngleStep");
            if (jObject.ContainsKey("Year")) { year = jObject["Year"]?.GetValue<int>() ?? default; }
            timeShiftInMinutes = Read(jObject, "TimeShiftInMinutes");
            latitude = Read(jObject, "Latitude");
            longitude = Read(jObject, "Longitude");
            timeZoneOffset = Read(jObject, "TimeZoneOffset");
            if (jObject.ContainsKey("WeatherIdentityHash")) { weatherIdentityHash = jObject["WeatherIdentityHash"]?.GetValue<string>(); }
            if (jObject.ContainsKey("DesirabilityHash")) { desirabilityHash = jObject["DesirabilityHash"]?.GetValue<string>(); }
            if (jObject.ContainsKey("DesirabilityName")) { desirabilityName = jObject["DesirabilityName"]?.GetValue<string>(); }

            // Re-derived, so a round-tripped object always agrees with its own contents.
            signatureHash = ComputeSignatureHash();
            return true;
        }

        private static double Read(JsonObject jObject, string name)
        {
            return jObject.ContainsKey(name) ? (jObject[name]?.GetValue<double>() ?? double.NaN) : double.NaN;
        }

        private static List<Guid> ReadGuids(JsonObject jObject, string name)
        {
            List<Guid> result = new List<Guid>();
            if (jObject.ContainsKey(name) && jObject[name] is JsonArray jArray)
            {
                foreach (JsonNode node in jArray)
                {
                    if (Guid.TryParse(node?.GetValue<string>(), out Guid guid))
                    {
                        result.Add(guid);
                    }
                }
            }

            return result;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            JsonArray guidsArray = new JsonArray();
            foreach (Guid guid in apertureGuids)
            {
                guidsArray.Add(guid.ToString());
            }
            jObject.Add("ApertureGuids", guidsArray);

            JsonArray countsArray = new JsonArray();
            foreach (int count in cellCounts)
            {
                countsArray.Add(count);
            }
            jObject.Add("CellCounts", countsArray);

            if (contextGeometryHash != null) { jObject.Add("ContextGeometryHash", contextGeometryHash); }
            if (targetGeometryHash != null) { jObject.Add("TargetGeometryHash", targetGeometryHash); }
            AddFinite(jObject, "GridSize", gridSize);
            AddFinite(jObject, "SunAngleStep", sunAngleStep);
            jObject.Add("Year", year);
            AddFinite(jObject, "TimeShiftInMinutes", timeShiftInMinutes);
            AddFinite(jObject, "Latitude", latitude);
            AddFinite(jObject, "Longitude", longitude);
            AddFinite(jObject, "TimeZoneOffset", timeZoneOffset);
            if (weatherIdentityHash != null) { jObject.Add("WeatherIdentityHash", weatherIdentityHash); }
            if (desirabilityHash != null) { jObject.Add("DesirabilityHash", desirabilityHash); }
            if (desirabilityName != null) { jObject.Add("DesirabilityName", desirabilityName); }
            jObject.Add("SignatureHash", signatureHash);

            return jObject;
        }

        /// <summary>Non-finite doubles are OMITTED from the JSON (absent = NaN on read), so the serialised form is always writable and byte-stable.</summary>
        private static void AddFinite(JsonObject jObject, string name, double value)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value))
            {
                jObject.Add(name, value);
            }
        }
    }
}
