// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 11 Gate 1: SAM against a genuinely INDEPENDENT solar implementation.
    ///
    /// THE REFERENCE. Ladybug Tools (ladybug.sunpath.Sunpath), run ONCE offline by
    /// Fixtures/Reference/generate_reference.py, producing the committed reference-results.json this
    /// test reads. Ladybug is not a build, test or runtime dependency — nothing here runs Python.
    /// The provenance (tool, version, site, procedure, date) is recorded inside the JSON.
    ///
    /// WHAT IS ACTUALLY INDEPENDENT, stated precisely because "validated against Ladybug" would
    /// otherwise claim more than was done:
    ///
    ///   INDEPENDENT — the solar position. Ladybug's Sunpath is a separate implementation with its
    ///     own declination, equation of time and hour angle. Agreement is real evidence.
    ///   INDEPENDENT — the transposition geometry and annual accumulation. cos(incidence) is
    ///     recomputed from Ladybug's sun vectors and the surface normals, and summed independently.
    ///   SHARED BY DESIGN — the GHI/DHI series, exported from the fixture so both sides read
    ///     byte-identical radiation. Two different weather files would measure the files.
    ///   SHARED BY DESIGN — the DNI decomposition rule, which is SAM's documented modelling choice.
    ///     Its low-sun clamp is MEASURED separately rather than validated against itself.
    ///   NOT COVERED — diffuse and ground-reflected transposition. Ladybug's Python API exposes
    ///     decomposition (DISC/DIRINT), not Perez transposition onto a tilted surface, so no
    ///     like-for-like comparison is available from this reference. That gap is recorded here and
    ///     in the assumptions register rather than papered over.
    /// </summary>
    public class IndependentReferenceTests
    {
        private readonly ITestOutputHelper output;

        public IndependentReferenceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static JsonNode Reference()
        {
            string path = Path.Combine(ReferenceExport.ReferenceDirectory, "reference-results.json");
            Assert.True(File.Exists(path),
                $"The independent reference is missing: {path}. Regenerate it with Fixtures/Reference/generate_reference.py under the Ladybug Tools Python.");

            return JsonNode.Parse(File.ReadAllText(path));
        }

        [Fact]
        public void The_Reference_Records_Its_Own_Provenance()
        {
            // A committed reference number with no provenance is folklore. Tool, procedure, site and
            // date must all be present, or a later reader cannot judge what the comparison proved.
            JsonNode reference = Reference();

            Assert.Equal("Ladybug Tools", reference["tool"].GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(reference["tool_python"].GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(reference["generated_utc"].GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(reference["procedure"].GetValue<string>()));
            Assert.Contains("Sunpath", reference["ladybug_core_sunpath"].GetValue<string>());

            // And what it does NOT cover has to be written down too.
            JsonArray notCovered = reference["not_covered"].AsArray();
            Assert.NotEmpty(notCovered);

            output.WriteLine($"tool {reference["tool"]} on Python {reference["tool_python"]}, generated {reference["generated_utc"]}");
            foreach (JsonNode node in notCovered)
            {
                output.WriteLine("NOT COVERED: " + node.GetValue<string>());
            }
        }

        [Fact]
        public void The_Reference_Describes_The_Same_Site_The_Fixture_Carries()
        {
            // If the reference drifted onto a different site or year, every number below would be
            // meaningless while still looking fine.
            JsonNode site = Reference()["site"];

            AnalyticalModel analyticalModel = ReferenceExport.LoadMultiAzimuth();
            WeatherData weatherData = ReferenceExport.Weather(analyticalModel);
            SAM.Core.Location location = weatherData.Location ?? analyticalModel.Location;

            Assert.Equal(location.Latitude, site["latitude"].GetValue<double>(), 6);
            Assert.Equal(location.Longitude, site["longitude"].GetValue<double>(), 6);
            Assert.Equal(weatherData.WeatherYears.First(x => x != null).Year, site["year"].GetValue<int>());
            Assert.Equal(8760, site["weather_hours"].GetValue<int>());

            output.WriteLine($"{site["name"]}  lat {site["latitude"]}  lon {site["longitude"]}  " +
                $"UTC{site["time_zone_hours"]}  year {site["year"]}  {site["weather_hours"]} hours");
        }

        // ------------------------------------------------------------- sun position ----

        [Fact]
        public void Sun_Position_Agrees_With_An_Independent_Implementation()
        {
            // The foundation: if the sun is in the wrong place, nothing downstream can be right.
            // Compared over all 8760 hours of the fixture year.
            //
            // TOLERANCE, AND WHY IT IS WHAT IT IS. Both are approximate closed-form algorithms, not
            // the NREL SPA reference. Published spreads between such algorithms are of order a few
            // tenths of a degree, and a tenth of a degree of sun position moves the beam on a
            // vertical surface by about 0.2 %. A degree is therefore a real bound on "the two
            // implementations agree", and it is set BEFORE looking: it is not the measured number
            // rounded up.
            JsonNode error = Reference()["sun_position_error"];

            JsonNode altitude = error["altitude_deg"];
            JsonNode azimuth = error["azimuth_deg_sun_above_horizon"];

            output.WriteLine($"altitude: MAE {altitude["mae"].GetValue<double>():0.####}°, " +
                $"RMSE {altitude["rmse"].GetValue<double>():0.####}°, " +
                $"bias {altitude["bias"].GetValue<double>():+0.####;-0.####}°, " +
                $"max {altitude["max_abs"].GetValue<double>():0.####}° over {altitude["count"]} hours");

            output.WriteLine($"azimuth : MAE {azimuth["mae"].GetValue<double>():0.####}°, " +
                $"RMSE {azimuth["rmse"].GetValue<double>():0.####}°, " +
                $"bias {azimuth["bias"].GetValue<double>():+0.####;-0.####}°, " +
                $"max {azimuth["max_abs"].GetValue<double>():0.####}° (sun above the horizon)");

            Assert.Equal(8760, altitude["count"].GetValue<int>());

            Assert.True(altitude["mae"].GetValue<double>() < 0.25,
                $"mean altitude disagreement {altitude["mae"].GetValue<double>():0.####}° is larger than two independent solar algorithms should differ");
            Assert.True(altitude["max_abs"].GetValue<double>() < 1.0,
                $"worst altitude disagreement {altitude["max_abs"].GetValue<double>():0.####}°");

            Assert.True(azimuth["mae"].GetValue<double>() < 0.25);
            Assert.True(azimuth["max_abs"].GetValue<double>() < 1.0);
        }

        // ------------------------------------------------- direct beam on real apertures ----

        [Fact]
        public void Annual_Direct_Beam_Agrees_With_The_Independent_Reference_On_Every_Orientation()
        {
            // The end-to-end number an engineer reads, on four orientations of the controlled model,
            // against a reference that built its own sun vectors and did its own accumulation.
            //
            // TOLERANCE. 2 % on the absolute annual direct energy. The two implementations share the
            // radiation series and the decomposition rule but nothing else, and the residual is
            // dominated by the sun-position difference above plus SAM's 2° sun grouping. Set before
            // looking; the measured values come out far inside it.
            JsonArray apertures = Reference()["apertures"].AsArray();
            Assert.Equal(10, apertures.Count);

            Dictionary<double, List<double>> errorPerAzimuth = new Dictionary<double, List<double>>();

            output.WriteLine("azimuth   SAM [kWh]   reference [kWh]   absolute    relative");
            foreach (JsonNode node in apertures)
            {
                double azimuth = node["azimuth_deg"].GetValue<double>();
                double sam = node["sam_direct_kwh"].GetValue<double>();
                double expected = node["reference_direct_kwh"].GetValue<double>();
                double absolute = node["absolute_error_kwh"].GetValue<double>();
                double relative = node["relative_error_percent"].GetValue<double>();

                output.WriteLine($"{azimuth,7:0}   {sam,9:0.00}   {expected,15:0.00}   {absolute,8:+0.00;-0.00}   {relative,7:+0.000;-0.000} %");

                Assert.True(Math.Abs(relative) < 2.0,
                    $"azimuth {azimuth:0}: SAM {sam:0.00} kWh against an independent {expected:0.00} kWh is {relative:+0.###;-0.###} %");

                if (!errorPerAzimuth.TryGetValue(azimuth, out List<double> list))
                {
                    list = new List<double>();
                    errorPerAzimuth[azimuth] = list;
                }

                list.Add(relative);
            }

            // Four orientations present, and apertures of the same orientation agree with each other
            // — which they must, since they see the same sun.
            Assert.Equal(4, errorPerAzimuth.Count);
            foreach (KeyValuePair<double, List<double>> pair in errorPerAzimuth)
            {
                Assert.True(pair.Value.Max() - pair.Value.Min() < 1e-9);
            }

            // The SOUTH facade carries the largest absolute energy and is the case a design decision
            // usually turns on, so it is held tighter than the blanket bound.
            double south = errorPerAzimuth[180.0][0];
            Assert.True(Math.Abs(south) < 0.5,
                $"the south facade — the highest-energy orientation — differs by {south:+0.###;-0.###} %");
        }

        [Fact]
        public void The_Beam_Decomposition_Closes_On_A_Horizontal_Plane()
        {
            // A closure check, and labelled as one rather than as a second opinion: reconstructing
            // the horizontal beam from the derived DNI must return the beam-horizontal series it came
            // from. It cannot validate the decomposition — it shares it — but it does prove the
            // geometry is self-consistent, and it isolates exactly what the low-sun clamp removes.
            JsonNode closure = Reference()["horizontal_beam_closure"];

            double measured = closure["measured_beam_horizontal_kwh_m2"].GetValue<double>();
            double reconstructed = closure["reconstructed_from_dni_kwh_m2"].GetValue<double>();
            double relative = closure["relative_error_percent"].GetValue<double>();

            output.WriteLine($"beam on the horizontal: measured {measured:0.###} kWh/m², " +
                $"reconstructed from DNI {reconstructed:0.###} kWh/m², {relative:+0.####;-0.####} %");

            // The only thing that can break the identity is the low-sun clamp, which by construction
            // removes energy at grazing sun. So the reconstruction must be slightly LOW, never high.
            Assert.True(relative <= 1e-9, "reconstructing more beam than was measured would mean the geometry is adding energy");
            Assert.True(Math.Abs(relative) < 0.5);
        }

        // --------------------------------------------------- the low-sun clamp, measured ----

        [Fact]
        public void The_Low_Sun_Clamp_Is_A_Necessary_Guard_And_Its_Cost_Is_Measured()
        {
            // SAM DERIVES the beam-normal irradiance as (GHI - DHI) / sin(altitude) rather than
            // reading it from the weather file, because the file's own direct field is ambiguous.
            // That division is exact only if the numerator is exact, and as the sun approaches the
            // horizon it amplifies ordinary weather noise without limit — so the denominator is
            // clamped at sin(5°).
            //
            // THIS TEST MAKES BOTH HALVES OF THAT HONEST. The clamp is not free — it is worth
            // several per cent of the annual direct beam, and up to a third of it on a west facade
            // that lives on grazing evening sun. It is also not optional: without it this very
            // weather file produces beam-normal irradiances tens of times the solar constant, which
            // are not physics but arithmetic on a vanishing denominator.
            JsonNode reference = Reference();
            JsonNode clamp = reference["low_sun_clamp"];

            double maxUnclamped = clamp["max_unclamped_dni_wm2"].GetValue<double>();
            double maxClamped = clamp["max_clamped_dni_wm2"].GetValue<double>();
            double solarConstant = clamp["solar_constant_wm2"].GetValue<double>();
            int exceeding = clamp["hours_where_unclamped_dni_exceeds_solar_constant"].GetValue<int>();

            output.WriteLine($"rule: {clamp["rule"]}");
            output.WriteLine($"unclamped beam-normal peaks at {maxUnclamped:0} W/m² — {maxUnclamped / solarConstant:0.#}x the solar constant ({solarConstant:0} W/m²), in {exceeding} hour(s)");
            output.WriteLine($"clamped, it peaks at {maxClamped:0} W/m²");

            // The guard is earning its keep: the unclamped derivation is physically impossible here.
            Assert.True(maxUnclamped > solarConstant,
                "if the unclamped derivation never exceeded the solar constant, the clamp would be doing nothing and should be questioned");

            // And the clamped one is physically admissible.
            Assert.True(maxClamped <= solarConstant,
                $"the clamped beam-normal irradiance still reaches {maxClamped:0} W/m², above the {solarConstant:0} W/m² solar constant");

            // The COST of the guard, per orientation — a real number for the assumptions register,
            // not a hand-wave. Asserted only as "present and material", because the value is a
            // property of the weather file and the site, not something to pin.
            output.WriteLine("");
            output.WriteLine("what the clamp removes, by orientation:");

            HashSet<double> seen = new HashSet<double>();
            double worst = 0;
            foreach (JsonNode node in reference["apertures"].AsArray())
            {
                double azimuth = node["azimuth_deg"].GetValue<double>();
                if (!seen.Add(azimuth))
                {
                    continue;
                }

                double effect = node["low_sun_clamp_effect_percent"].GetValue<double>();
                worst = Math.Max(worst, Math.Abs(effect));
                output.WriteLine($"  azimuth {azimuth,3:0}: {effect,+8:0.###} % of the unclamped annual direct beam");

                // Always negative: the clamp can only ever reduce the derived beam.
                Assert.True(effect <= 0.0, "the clamp must never increase the derived beam");
            }

            Assert.True(worst > 1.0,
                "the clamp's effect is material and must stay visible in the register; if it has become negligible, the register entry needs revisiting");

            output.WriteLine("");
            output.WriteLine($"worst-orientation effect {worst:0.#} % — recorded in the assumptions register as a MEASURED limitation");
        }

        // ------------------------------------------------------- SAM still says the same ----

        [Fact]
        public void SAM_Still_Produces_The_Numbers_The_Reference_Was_Taken_Against()
        {
            // The reference is a committed snapshot. If SAM's own answer drifts, the comparison above
            // silently stops describing the current code. This re-runs SAM and holds it to the
            // figures the reference was generated from.
            AnalyticalModel analyticalModel = ReferenceExport.LoadMultiAzimuth();
            WeatherData weatherData = ReferenceExport.Weather(analyticalModel);
            int year = weatherData.WeatherYears.First(x => x != null).Year;

            List<ApertureIrradianceResult> results = analyticalModel.SimulateApertures(
                new AnalysisPeriod(year), out bool _, weatherData, null, 0.5,
                SAM.Geometry.SolarCalculator.SkyModel.PerezAnisotropic, 2.0, false, 0.2, SunTimeConvention.IntervalStart);

            Assert.NotNull(results);

            Dictionary<string, double> expected = new Dictionary<string, double>();
            foreach (JsonNode node in Reference()["apertures"].AsArray())
            {
                expected[node["guid"].GetValue<string>()] = node["sam_direct_kwh"].GetValue<double>();
            }

            int checkedApertures = 0;
            foreach (ApertureIrradianceResult result in results)
            {
                if (!expected.TryGetValue(result.Reference, out double snapshot))
                {
                    continue;
                }

                Assert.Equal(snapshot, result.DirectEnergy, 6);
                checkedApertures++;
            }

            Assert.Equal(10, checkedApertures);
            output.WriteLine($"{checkedApertures} apertures still reproduce the direct energy the independent reference was taken against");
        }
    }
}
