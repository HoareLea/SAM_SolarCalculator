// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Weather.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Bins the sun positions of the given DateTimes on an (altitude, azimuth) grid of
        /// binSizeDegrees. Purely geometric: no weather data is consulted, so the result is
        /// independent of the selected AnalysisPeriod and weather file. Hours whose sun is below
        /// minHorizonAngle are discarded. Each bin's representative direction is the angular bin
        /// centre (deterministic, weather-independent).
        ///
        /// timeShiftInMinutes makes the sun-position sampling convention of the timeline explicit:
        /// bin membership of the weather-timeline hour h is decided by the sun at h + shift
        /// (+30 for interval-start EPW timelines, -30 for TAS EDSL compatibility). Membership and
        /// evaluation must use the same shift, which is why the shift is recorded in the cache
        /// identity. Stored hours of the year remain on the weather (unshifted) timeline.
        /// </summary>
        /// <param name="location">Reference location (latitude/longitude/time zone).</param>
        /// <param name="dateTimes">Weather-timeline DateTimes (whole hours).</param>
        /// <param name="binSizeDegrees">Angular bin size in degrees (altitude x azimuth).</param>
        /// <param name="minHorizonAngle">Minimum sun altitude above the horizon, RADIANS (Core.Tolerance convention).</param>
        /// <param name="timeShiftInMinutes">Sun-position sampling offset relative to each timestamp, minutes.</param>
        public static List<SunBin> SunBins(this Location location, IEnumerable<DateTime> dateTimes, double binSizeDegrees, double minHorizonAngle = Core.Tolerance.Angle, double timeShiftInMinutes = 0)
        {
            if (location == null || dateTimes == null || double.IsNaN(binSizeDegrees) || binSizeDegrees <= 0)
            {
                return null;
            }

            double minAltitudeDegrees = minHorizonAngle * 180.0 / Math.PI;

            Dictionary<Tuple<int, int>, List<int>> dictionary = new Dictionary<Tuple<int, int>, List<int>>();
            foreach (DateTime dateTime in dateTimes)
            {
                DateTime sunTime = timeShiftInMinutes == 0 ? dateTime : dateTime.AddMinutes(timeShiftInMinutes);

                // Read the angles from SolarTimes exactly as SAM.Analytical.SolarCalculator.Query
                // .CachedIrradiance does (Radians, never the whole-degree-rounding Degrees, and no
                // vector round-trip). Bin membership and bin lookup are then bit-identical
                // computations, so a valid hour can never fail to resolve to its own bin.
                if (!Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double altitude, out double azimuth))
                {
                    continue;
                }

                if (altitude < minAltitudeDegrees)
                {
                    continue;
                }

                int altitudeBin;
                int azimuthBin;
                BinCoordinates(altitude, azimuth, binSizeDegrees, out altitudeBin, out azimuthBin);

                Tuple<int, int> key = new Tuple<int, int>(altitudeBin, azimuthBin);
                if (!dictionary.TryGetValue(key, out List<int> hoursOfYear))
                {
                    hoursOfYear = new List<int>();
                    dictionary[key] = hoursOfYear;
                }

                int hourOfYear = (int)(dateTime - new DateTime(dateTime.Year, 1, 1)).TotalHours;
                hoursOfYear.Add(hourOfYear);
            }

            if (dictionary.Count == 0)
            {
                return new List<SunBin>();
            }

            List<Tuple<int, int>> keys = new List<Tuple<int, int>>(dictionary.Keys);
            keys.Sort((x, y) => x.Item1 != y.Item1 ? x.Item1.CompareTo(y.Item1) : x.Item2.CompareTo(y.Item2));

            List<SunBin> result = new List<SunBin>(keys.Count);
            foreach (Tuple<int, int> key in keys)
            {
                double altitudeCentre = (key.Item1 + 0.5) * binSizeDegrees;
                double azimuthCentre = (key.Item2 + 0.5) * binSizeDegrees;

                Vector3D representativeDirection = Geometry.SolarCalculator.Create.SunDirection(altitudeCentre, azimuthCentre);

                List<int> hoursOfYear = dictionary[key];
                hoursOfYear.Sort();

                result.Add(new SunBin(result.Count, key.Item1, key.Item2, representativeDirection, hoursOfYear));
            }

            return result;
        }

        /// <summary>
        /// Full-year convenience overload: bins every hour of the given year, so the result covers
        /// any AnalysisPeriod within that year.
        /// </summary>
        public static List<SunBin> SunBins(this Location location, int year, double binSizeDegrees, double minHorizonAngle = Core.Tolerance.Angle, double timeShiftInMinutes = 0)
        {
            int count = DateTime.IsLeapYear(year) ? 8784 : 8760;
            DateTime start = new DateTime(year, 1, 1);

            List<DateTime> dateTimes = new List<DateTime>(count);
            for (int i = 0; i < count; i++)
            {
                dateTimes.Add(start.AddHours(i));
            }

            return SunBins(location, dateTimes, binSizeDegrees, minHorizonAngle, timeShiftInMinutes);
        }

        /// <summary>
        /// Quantises solar angles to bin coordinates. Shared by bin creation and cache lookup so the
        /// mapping can never drift. Azimuth is compass degrees clockwise from north, 0–360.
        /// </summary>
        internal static void BinCoordinates(double altitude, double azimuth, double binSizeDegrees, out int altitudeBin, out int azimuthBin)
        {
            altitudeBin = (int)Math.Floor(altitude / binSizeDegrees);

            // Wrap the azimuth into [0, 360) rather than clamping: a clamp would map a small
            // negative azimuth (355 deg expressed as -5) onto bin 0 instead of the correct northerly
            // bin, and would make the mapping depend on which caller normalised first.
            double azimuth_Wrapped = azimuth % 360.0;
            if (azimuth_Wrapped < 0.0)
            {
                azimuth_Wrapped += 360.0;
            }

            if (azimuth_Wrapped >= 360.0)
            {
                azimuth_Wrapped = 360.0 - 1e-9;
            }

            azimuthBin = (int)Math.Floor(azimuth_Wrapped / binSizeDegrees);
        }
    }
}
