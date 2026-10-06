// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// The comparison basis of a resolved solar context, as one comparable fingerprint. Built
        /// entirely from the context plus the brief actually evaluated, so the signature cannot
        /// drift from the calculation it describes.
        /// </summary>
        public static SAM.Analytical.SolarCalculator.ShadingAnalysisSignature ShadingAnalysisSignature(this ApertureSolarContext context, IDesirabilityStrategy strategy)
        {
            if (context == null)
            {
                return null;
            }

            List<ApertureSolarTarget> targets = context.Targets;
            List<Guid> apertureGuids = new List<Guid>();
            List<int> cellCounts = new List<int>();
            foreach (ApertureSolarTarget target in targets ?? new List<ApertureSolarTarget>())
            {
                if (target == null)
                {
                    continue;
                }

                apertureGuids.Add(target.ApertureGuid);
                cellCounts.Add(target.CellCount);
            }

            Core.Location location = context.Location;
            double timeZoneOffset = Geometry.SolarCalculator.Query.TimeZoneOffset(location);

            return new SAM.Analytical.SolarCalculator.ShadingAnalysisSignature(
                apertureGuids,
                cellCounts,
                context.ContextGeometryHash,
                context.TargetGeometryHash,
                context.GridSize,
                context.SunAngleStep,
                context.Year,
                context.TimeShiftInMinutes,
                location?.Latitude ?? double.NaN,
                location?.Longitude ?? double.NaN,
                timeZoneOffset,
                WeatherIdentityHash(context.WeatherData, context.Year, context.TimeShiftInMinutes, location),
                DesirabilityHash(strategy),
                strategy == null ? null : strategy.GetType().Name);
        }

        /// <summary>
        /// Fingerprint of the weather series actually used. The comparison never goes by file name:
        /// this is the location, the timeline drivers and a digest of the hourly global, diffuse and
        /// direct series in their stable ordered form (WeatherYear.GetValues walks the weather days
        /// in day order, hour by hour).
        /// </summary>
        private static string WeatherIdentityHash(WeatherData weatherData, int year, double timeShiftInMinutes, Core.Location location)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append((location?.Latitude ?? double.NaN).ToString("R", CultureInfo.InvariantCulture));
            stringBuilder.Append('|');
            stringBuilder.Append((location?.Longitude ?? double.NaN).ToString("R", CultureInfo.InvariantCulture));
            stringBuilder.Append('|');
            stringBuilder.Append(Geometry.SolarCalculator.Query.TimeZoneOffset(location).ToString("R", CultureInfo.InvariantCulture));
            stringBuilder.Append('|');
            stringBuilder.Append(year);
            stringBuilder.Append('|');
            stringBuilder.Append(timeShiftInMinutes.ToString("R", CultureInfo.InvariantCulture));

            WeatherYear weatherYear = weatherData?[year];
            AppendSeries(stringBuilder, weatherYear, WeatherDataType.GlobalSolarRadiation, "G");
            AppendSeries(stringBuilder, weatherYear, WeatherDataType.DiffuseSolarRadiation, "D");
            AppendSeries(stringBuilder, weatherYear, WeatherDataType.DirectSolarRadiation, "DN");

            using (MD5 md5 = MD5.Create())
            {
                return SAM.Analytical.SolarCalculator.ShadingAnalysisSignature.Hex(md5.ComputeHash(Encoding.UTF8.GetBytes(stringBuilder.ToString())));
            }
        }

        private static void AppendSeries(StringBuilder stringBuilder, WeatherYear weatherYear, WeatherDataType weatherDataType, string tag)
        {
            stringBuilder.Append('|');
            stringBuilder.Append(tag);
            stringBuilder.Append(':');

            List<double> values = null;
            try
            {
                // GetValues throws when the weather year carries no series for this data type
                // (synthetic weather, or a source file that omits a field) — the honest reading is
                // "series absent", which is what the digest says.
                values = weatherYear?.GetValues(weatherDataType);
            }
            catch (ArgumentNullException)
            {
                values = null;
            }

            if (values == null || values.Count == 0)
            {
                stringBuilder.Append("none");
                return;
            }

            foreach (double value in values)
            {
                stringBuilder.Append(value.ToString("R", CultureInfo.InvariantCulture));
                stringBuilder.Append(',');
            }
        }

        /// <summary>MD5 over the strategy's canonical JSON text — the brief's comparison-grade identity.</summary>
        private static string DesirabilityHash(IDesirabilityStrategy strategy)
        {
            string text = strategy?.ToJsonObject()?.ToJsonString() ?? "null";
            using (MD5 md5 = MD5.Create())
            {
                return SAM.Analytical.SolarCalculator.ShadingAnalysisSignature.Hex(md5.ComputeHash(Encoding.UTF8.GetBytes(text)));
            }
        }
    }
}
