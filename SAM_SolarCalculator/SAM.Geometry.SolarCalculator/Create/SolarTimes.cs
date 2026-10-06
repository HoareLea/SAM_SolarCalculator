// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Innovative.Geometry;
using SAM.Core;
using System;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// SolarTimes for a Location at a DateTime. The full fractional UTC offset is carried into
        /// the SolarTimes constructor via DateTimeOffset (SolarCalculator 3.5.0 derives its decimal
        /// TimeZoneOffset from DateTimeOffset.Offset.TotalHours, so e.g. UTC+05:30 is preserved as 5.5).
        /// Returns null when the Location's timezone does not resolve (Query.TimeZoneOffset ->
        /// double.NaN) rather than silently defaulting to Greenwich: an unresolved timezone must fail
        /// loudly, not be indistinguishable from a genuine UTC+00:00.
        /// </summary>
        public static Innovative.SolarCalculator.SolarTimes SolarTimes(this Location location, DateTime dateTime)
        {
            if (location == null)
            {
                return null;
            }

            double timeZoneOffset = Query.TimeZoneOffset(location);
            if (double.IsNaN(timeZoneOffset))
            {
                return null;
            }

            // DateTimeOffset(DateTime, TimeSpan) throws for Kind Utc/Local with a mismatched offset;
            // SAM timeline timestamps are location-local floating times, so normalise to Unspecified.
            return new Innovative.SolarCalculator.SolarTimes(new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), TimeSpan.FromHours(timeZoneOffset)), new Angle(location.Latitude), new Angle(location.Longitude));
        }
    }
}
