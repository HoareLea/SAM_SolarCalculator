// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// UTC offset of a Location in hours, preserved as a fractional value (e.g. 5.5 for UTC+05:30).
        /// Returns double.NaN when the Location carries no TimeZone parameter, or the TimeZone string
        /// does not resolve to a known SAM.Core.UTC value (unsupported/broken mapping). Genuine
        /// UTC+00:00 still returns 0 — callers must not conflate "no usable timezone" with Greenwich by
        /// treating this return value as a default; check double.IsNaN first. This replaces the
        /// previous System.Convert.ToInt32(Core.Query.Double(uTC)) call sites, which rounded/truncated
        /// fractional time zones and shifted the computed sun position by up to 30 minutes.
        /// </summary>
        public static double TimeZoneOffset(this Location location)
        {
            if (location != null && location.TryGetValue(LocationParameter.TimeZone, out string timeZoneString))
            {
                UTC uTC = Core.Query.UTC(NormaliseZeroOffset(timeZoneString));
                return Core.Query.Double(uTC);
            }

            return double.NaN;
        }

        /// <summary>
        /// SAM.Core.UTC.PlusMinus0000's Description is "UTC±00:00" (the Unicode plus-minus sign), so
        /// SAM.Core.Query.UTC(string) does not recognise the ordinarily-written "UTC+00:00" /
        /// "UTC-00:00" -- both fall through to UTC.Undefined even though they genuinely mean
        /// UTC+00:00. Translating them to the exact Description string here (rather than special-
        /// casing the return value) keeps a single resolution path through Core.Query.UTC / Double,
        /// so any other alias SAM.Core later recognises for zero benefits automatically.
        /// </summary>
        private static string NormaliseZeroOffset(string timeZoneString)
        {
            string trimmed = timeZoneString?.Trim();
            if (string.Equals(trimmed, "UTC+00:00", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "UTC-00:00", System.StringComparison.OrdinalIgnoreCase))
            {
                return "UTC±00:00";
            }

            return timeZoneString;
        }
    }
}
