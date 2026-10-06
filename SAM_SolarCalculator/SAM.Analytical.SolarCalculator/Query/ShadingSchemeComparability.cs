// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// Whether two verified schemes were measured on the SAME analysis basis. Comparable means
        /// every identity field of the signature is equal — doubles by exact equality, because they
        /// are inputs, not computed results — with the display-only desirability name excluded.
        ///
        /// The reason names the FIRST differing field with both values, because "incomparable" alone
        /// sends the engineer hunting; "GridSize differs: 0.250 m vs 0.500 m" says what to fix.
        /// </summary>
        public static bool Comparable(ShadingAnalysisSignature a, ShadingAnalysisSignature b, out string reason)
        {
            reason = null;

            if (a == null || b == null)
            {
                reason = "A signature is missing, so the analysis basis cannot be compared.";
                return false;
            }

            if (!SameGuids(a.ApertureGuids, b.ApertureGuids))
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "ApertureGuids differ: {0} vs {1}.",
                    GuidsText(a.ApertureGuids), GuidsText(b.ApertureGuids));
                return false;
            }

            if (!SameInts(a.CellCounts, b.CellCounts))
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "Analysis cell counts differ: ({0}) vs ({1}).",
                    string.Join(", ", a.CellCounts), string.Join(", ", b.CellCounts));
                return false;
            }

            if (a.ContextGeometryHash != b.ContextGeometryHash)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "ContextGeometryHash differs: {0} vs {1}. The surrounding geometry is not the same.",
                    a.ContextGeometryHash, b.ContextGeometryHash);
                return false;
            }

            if (a.TargetGeometryHash != b.TargetGeometryHash)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "TargetGeometryHash differs: {0} vs {1}. The aperture geometry or the analysis cells are not the same.",
                    a.TargetGeometryHash, b.TargetGeometryHash);
                return false;
            }

            if (a.GridSize != b.GridSize)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "GridSize differs: {0:0.####} m in this scheme, {1:0.####} m in the comparison basis.",
                    a.GridSize, b.GridSize);
                return false;
            }

            if (a.SunAngleStep != b.SunAngleStep)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "SunAngleStep differs: {0:0.###} deg in this scheme, {1:0.###} deg in the comparison basis.",
                    a.SunAngleStep, b.SunAngleStep);
                return false;
            }

            if (a.Year != b.Year)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "Analysis year differs: {0} vs {1}.", a.Year, b.Year);
                return false;
            }

            if (a.TimeShiftInMinutes != b.TimeShiftInMinutes)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "TimeShiftInMinutes differs: {0} vs {1}. The sun-position sampling timeline is not the same.",
                    a.TimeShiftInMinutes, b.TimeShiftInMinutes);
                return false;
            }

            if (a.Latitude != b.Latitude || a.Longitude != b.Longitude)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "Site location differs: ({0}, {1}) vs ({2}, {3}).",
                    a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                return false;
            }

            if (a.TimeZoneOffset != b.TimeZoneOffset)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "TimeZoneOffset differs: {0} vs {1}.", a.TimeZoneOffset, b.TimeZoneOffset);
                return false;
            }

            if (a.WeatherIdentityHash != b.WeatherIdentityHash)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "WeatherIdentityHash differs: {0} vs {1}. The weather series are not the same.",
                    a.WeatherIdentityHash, b.WeatherIdentityHash);
                return false;
            }

            if (a.DesirabilityHash != b.DesirabilityHash)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "DesirabilityHash differs: {0} vs {1}. The desirability brief is not the same — two strategies with the same name but different periods are NOT comparable.",
                    a.DesirabilityHash, b.DesirabilityHash);
                return false;
            }

            return true;
        }

        private static bool SameGuids(List<Guid> a, List<Guid> b)
        {
            if (a == null || b == null || a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameInts(List<int> a, List<int> b)
        {
            if (a == null || b == null || a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string GuidsText(List<Guid> guids)
        {
            List<string> parts = new List<string>();
            foreach (Guid guid in guids ?? new List<Guid>())
            {
                parts.Add(guid.ToString().Substring(0, 8));
            }

            return "[" + string.Join(", ", parts) + "]";
        }
    }
}
