// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Writes the inputs an INDEPENDENT solar implementation needs in order to answer the same
    /// question SAM was asked, plus SAM's own answer, so the two can be compared outside this
    /// process.
    ///
    /// WHY AN EXPORT RATHER THAN A DIRECT CALL. Ladybug Tools is a Python/Radiance stack. Calling it
    /// from the test process would make it a RUNTIME DEPENDENCY of SAM's test suite, which is
    /// exactly what the validation plan forbids — a validation reference has to be usable once,
    /// offline, and then be a committed number. So this test writes CSV, a Python script consumes
    /// it, and the resulting reference JSON is committed and asserted against by
    /// IndependentReferenceTests. Regenerating it is a deliberate act, not something CI does.
    ///
    /// WHY THE WEATHER IS EXPORTED RATHER THAN AN EPW BEING SHARED. The fixture's weather is
    /// embedded in the .sam file. Exporting the exact GHI/DHI series SAM read means both sides work
    /// from byte-identical radiation data, so any difference is solar geometry and transposition
    /// rather than a weather-parsing mismatch. Comparing unlike inputs is the classic way to make a
    /// validation say nothing.
    ///
    /// This test is SKIPPED by default: it writes files into the repository and is a tool, not a
    /// check. Remove the Skip to regenerate.
    /// </summary>
    public class ReferenceExport
    {
        private readonly ITestOutputHelper output;

        public ReferenceExport(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>Where the reference material lives, relative to the repository root.</summary>
        internal const string ReferenceDirectoryName = "Reference";

        internal static string ReferenceDirectory
        {
            get
            {
                return Path.Combine(AppContext.BaseDirectory, "Fixtures", ReferenceDirectoryName);
            }
        }

        internal static AnalyticalModel LoadMultiAzimuth()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            Assert.True(File.Exists(path), $"Test fixture missing: {path}");

            AnalyticalModel analyticalModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            Assert.NotNull(analyticalModel);
            return analyticalModel;
        }

        internal static WeatherData Weather(AnalyticalModel analyticalModel)
        {
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            Assert.NotNull(weatherData);
            return weatherData;
        }

        [Fact(Skip = "Regeneration tool. Writes reference inputs into the repository; run deliberately.")]
        public void Write_Reference_Inputs()
        {
            AnalyticalModel analyticalModel = LoadMultiAzimuth();
            WeatherData weatherData = Weather(analyticalModel);
            WeatherYear weatherYear = weatherData.WeatherYears.First(x => x != null);
            int year = weatherYear.Year;

            SAM.Core.Location location = weatherData.Location ?? analyticalModel.Location;
            Assert.NotNull(location);

            double timeZoneOffset = SAM.Geometry.SolarCalculator.Query.TimeZoneOffset(location);

            Directory.CreateDirectory(ReferenceDirectory);

            // ---- the site, and the sampling conventions the reference must reproduce exactly ----
            StringBuilder site = new StringBuilder();
            site.AppendLine("key,value");
            site.AppendLine(FormattableString.Invariant($"name,{location.Name}"));
            site.AppendLine(FormattableString.Invariant($"latitude,{location.Latitude:R}"));
            site.AppendLine(FormattableString.Invariant($"longitude,{location.Longitude:R}"));
            site.AppendLine(FormattableString.Invariant($"elevation,{location.Elevation:R}"));
            site.AppendLine(FormattableString.Invariant($"timeZoneOffsetHours,{timeZoneOffset:R}"));
            site.AppendLine(FormattableString.Invariant($"year,{year}"));

            // The sun is sampled at the hour PLUS this shift: SAM's timestamps are interval STARTS,
            // so the representative sun position is the middle of the hour.
            site.AppendLine(FormattableString.Invariant($"sunPositionShiftMinutes,{30.0:R}"));
            site.AppendLine(FormattableString.Invariant($"minimumSinElevationDegrees,{5.0:R}"));
            File.WriteAllText(Path.Combine(ReferenceDirectory, "site.csv"), site.ToString());

            // ---- the radiation series and SAM's sun position for every hour of the year ----
            StringBuilder hours = new StringBuilder();
            hours.AppendLine("hourOfYear,globalHorizontal_Wm2,diffuseHorizontal_Wm2,samAltitude_deg,samAzimuth_deg");

            DateTime yearStart = new DateTime(year, 1, 1);
            int written = 0;
            for (int hourOfYear = 0; hourOfYear < 8760; hourOfYear++)
            {
                DateTime dateTime = yearStart.AddHours(hourOfYear);
                WeatherHour weatherHour = weatherData.GetWeatherHour(dateTime);
                if (weatherHour == null)
                {
                    continue;
                }

                DateTime sunTime = dateTime.AddMinutes(30.0);
                if (!SAM.Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double altitude, out double azimuth))
                {
                    altitude = double.NaN;
                    azimuth = double.NaN;
                }

                hours.AppendLine(FormattableString.Invariant(
                    $"{hourOfYear},{weatherHour.GlobalSolarRadiation:R},{weatherHour.DiffuseSolarRadiation:R},{altitude:R},{azimuth:R}"));

                written++;
            }

            File.WriteAllText(Path.Combine(ReferenceDirectory, "hours.csv"), hours.ToString());
            output.WriteLine($"wrote {written} weather hours for {location.Name} ({location.Latitude:0.####}, {location.Longitude:0.####}), UTC{timeZoneOffset:+0.##;-0.##}, year {year}");

            // ---- SAM's own answer, per aperture orientation ----
            List<ApertureIrradianceResult> results = analyticalModel.SimulateApertures(
                new AnalysisPeriod(year), out bool _, weatherData, null, 0.5,
                SAM.Geometry.SolarCalculator.SkyModel.PerezAnisotropic, 2.0, false, 0.2, SunTimeConvention.IntervalStart);

            Assert.NotNull(results);

            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, 0.5);

            StringBuilder apertures = new StringBuilder();
            apertures.AppendLine("apertureGuid,azimuth_deg,tilt_deg,grossArea_m2,sampledArea_m2,directEnergy_kWh,diffuseEnergy_kWh,groundReflectedEnergy_kWh,totalEnergy_kWh");

            foreach (ApertureIrradianceResult result in results)
            {
                Guid.TryParse(result.Reference, out Guid apertureGuid);
                ApertureSolarTarget target = targets.Find(x => x.ApertureGuid == apertureGuid);
                if (target == null)
                {
                    continue;
                }

                apertures.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R}",
                    apertureGuid, target.Azimuth, target.Tilt, target.GrossArea, target.SampledArea,
                    result.DirectEnergy, result.DiffuseEnergy, result.GroundReflectedEnergy, result.TotalEnergy));
            }

            File.WriteAllText(Path.Combine(ReferenceDirectory, "sam-apertures.csv"), apertures.ToString());
            output.WriteLine($"wrote {results.Count} aperture results to {ReferenceDirectory}");
        }
    }
}
