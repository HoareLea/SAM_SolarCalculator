// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SolarCreate = SAM.Analytical.SolarCalculator.Create;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The core methods the new Grasshopper components call (§15.8): assembly, comparison, the
    /// rebuilt leader geometry, and the VerifyShading scheme dispatch. Following the established
    /// pattern of testing the component LOGIC, not the components.
    /// </summary>
    public class SchemeComponentLogicTests
    {
        private const double GridSize = 0.5;

        private static AnalyticalModel Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            return SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
        }

        [Fact]
        public void AssembleShadingSchemes_Core_Returns_The_Declared_Object_Types()
        {
            AnalyticalModel model = Load();
            WeatherData weatherData = model.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;

            List<ApertureSolarTarget> southTargets = model.ApertureSolarTargets(null, GridSize)
                .Where(x => Math.Abs(x.Azimuth - 180.0) < 1.0)
                .Take(2)
                .ToList();

            List<OptimisedShadingResult> results = new List<OptimisedShadingResult>();
            foreach (ApertureSolarTarget target in southTargets)
            {
                OptimisedShadingResult result = new OptimisedShadingResult();
                result.ApertureGuid = target.ApertureGuid;
                result.TypologyName = "Overhang";
                result.SetParameters(new List<string> { "Depth" }, new Dictionary<string, double> { { "Depth", 0.8 } }, new Dictionary<string, double>(), new List<ShadingParameter> { new ShadingParameter("Depth", 0.05, 3.0, 0.01) });
                result.Objective = new ShadingObjective(1.0, 0.1);
                result.SetRun(10, 2, 1.0, ShadingOptimisationTermination.StepBelowGranularity, false, 0.0, 2, 2);
                result.SetProvenance(target.ApertureGuid, "SeasonalDesirability", GridSize, 2.0, 30.0, year, "ctx", "tgt", "tbl", new List<Guid>());
                results.Add(result);
            }

            List<ShadingScheme> schemes = SolarCreate.ShadingSchemes(southTargets, results, null, true, out string message);
            Assert.Null(message);
            Assert.All(schemes, x => Assert.IsType<ShadingScheme>(x));
            Assert.Contains(schemes, x => x.Name == "Overhang");
            Assert.Contains(schemes, x => x.Name == "No Shade");
        }

        [Fact]
        public void CompareShading_Core_TopRankedScheme_Is_RankedVerifiedSchemes_First()
        {
            AnalyticalModel model = Load();
            WeatherData weatherData = model.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;

            List<ApertureSolarTarget> southTargets = model.ApertureSolarTargets(null, GridSize)
                .Where(x => Math.Abs(x.Azimuth - 180.0) < 1.0)
                .Take(2)
                .ToList();

            ShadingScheme Shade(string name, IShadingTypology typology)
            {
                return new ShadingScheme(name, "RationaliseShading", southTargets.Select(x => x.ApertureGuid), new List<Guid> { southTargets[0].PanelGuid },
                    southTargets.Select(t => new ShadingDevice(t.ApertureGuid, typology)),
                    new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                    ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
            }

            List<ShadingScheme> schemes = new List<ShadingScheme>
            {
                Shade("Overhang", new Overhang(1.0)),
                Shade("VerticalFins", new VerticalFins(0.5, 2)),
            };

            ShadingComparisonResult result = SolarCreate.ShadingComparison(
                model, southTargets, schemes, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string message,
                weatherData, null, null, null, GridSize, 2.0, false);

            Assert.Null(message);
            Assert.NotNull(result.TopRankedVerifiedScheme);
            Assert.Equal(result.TopRankedVerifiedScheme.Scheme.SchemeGuid, result.Rows[0].SchemeGuid);

            // The rebuilt leader geometry carries the same element GUIDs the verified performance
            // was attributed by — the geometry is what produced the numbers.
            ShadingScheme leader = result.TopRankedVerifiedScheme.Scheme;
            List<ShadingElement> elements = leader.SchemeElements(southTargets);
            HashSet<Guid> elementGuids = new HashSet<Guid>(elements.Select(x => x.Guid));
            Assert.Equal(elementGuids, new HashSet<Guid>(result.TopRankedVerifiedScheme.Performance.EnergyPerElement.Keys));
        }

        [Fact]
        public void Legacy_VerifyShading_Dispatch_Keeps_The_Old_Path_For_Devices()
        {
            // The VerifyShading component keeps its pre-scheme single-device path for a wire that
            // carries a device rather than a scheme. Exercised here at the core level exactly as the
            // component calls it: ApertureShadingSetup, then ShadingPerformance on the device's own
            // aperture. The point is that the new scheme path did not disturb this older one.
            AnalyticalModel model = Load();
            WeatherData weatherData = model.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;

            ApertureSolarTarget target = model.ApertureSolarTargets(null, GridSize)
                .First(x => Math.Abs(x.Azimuth - 180.0) < 1.0);

            ApertureShadingSetup setup = SolarCreate.ApertureShadingSetup(
                model, target.ApertureGuid, year, out string setupMessage,
                weatherData, null, null, null, null, GridSize, 2.0, false);

            Assert.Null(setupMessage);
            Assert.NotNull(setup);

            ShadingPerformance performance = SolarCreate.ShadingPerformance(
                setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability, setup.Context.ContextOccluders, new Overhang(0.5), setup.CellIndexOffset);

            Assert.NotNull(performance);
            Assert.True(performance.AdmittedDirectEnergy > 0, "the unshaded baseline must be measurable");
            Assert.True(performance.DirectSolarIntercepted > 0, "an overhang on a south window must intercept direct solar");
        }

        [Fact]
        public void SelectShadingScheme_Core_Produces_The_Selection_Outputs()
        {
            // A genuine call through the selection workflow the component wraps: a real comparison
            // is ranked, then the engineer selects the leader (no reason needed) and a lower-ranked
            // option (reason mandatory), and the decision carries the scheme identity, rank,
            // alignment and the marked comparison snapshot.
            AnalyticalModel model = Load();
            WeatherData weatherData = model.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);

            List<ApertureSolarTarget> southTargets = model.ApertureSolarTargets(null, GridSize)
                .Where(x => Math.Abs(x.Azimuth - 180.0) < 1.0)
                .Take(2)
                .ToList();

            ShadingScheme Shade(string name, IShadingTypology typology)
            {
                return new ShadingScheme(name, "RationaliseShading", southTargets.Select(x => x.ApertureGuid), new List<Guid> { southTargets[0].PanelGuid },
                    southTargets.Select(t => new ShadingDevice(t.ApertureGuid, typology)),
                    new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                    ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
            }

            List<ShadingScheme> schemes = new List<ShadingScheme>
            {
                new ShadingScheme("No Shade", "Baseline", southTargets.Select(x => x.ApertureGuid), new List<Guid> { southTargets[0].PanelGuid },
                    new List<ShadingDevice>(), new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                    ShadingDesignStatus.NoShading, new List<string>(), null, new List<string>()),
                Shade("Overhang", new Overhang(1.0)),
                Shade("VerticalFins", new VerticalFins(0.5, 2)),
            };

            ShadingComparisonResult comparison = SolarCreate.ShadingComparison(
                model, southTargets, schemes, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string message,
                weatherData, null, null, null, GridSize, 2.0, false);

            Assert.Null(message);
            Assert.True(comparison.RankedCount >= 2);

            ShadingComparisonRow leader = comparison.TopRankedRow;
            Assert.NotNull(leader);
            ShadingScheme leaderScheme = schemes.First(x => x.SchemeGuid == leader.SchemeGuid);

            // Rank 1 needs no reason, agrees with the leader, and the snapshot marks the chosen row.
            ShadingSelectionDecision leaderDecision = comparison.SelectShadingScheme(leaderScheme, null);
            Assert.True(leaderDecision.Successful);
            Assert.Null(leaderDecision.Message);
            Assert.Equal(leader.SchemeGuid, leaderDecision.SelectedSchemeGuid);
            Assert.Equal(1, leaderDecision.SelectedRank);
            Assert.Equal(ShadingSelectionAlignment.AgreesWithLeader, leaderDecision.SelectionAlignment);
            Assert.Contains("agrees with the analytical ranking", leaderDecision.DecisionSummary);
            Assert.True(leaderDecision.ComparisonResult.Rows.First(x => x.SchemeGuid == leader.SchemeGuid).EngineerSelected);

            // A lower-ranked option needs a recorded reason; without one it is refused, with one it
            // is recorded as a departure from the analytical leader.
            ShadingComparisonRow runnerUp = comparison.Rows.First(x => x.Rank == 2);
            ShadingScheme runnerUpScheme = schemes.First(x => x.SchemeGuid == runnerUp.SchemeGuid);

            ShadingSelectionDecision refused = comparison.SelectShadingScheme(runnerUpScheme, null);
            Assert.False(refused.Successful);
            Assert.Contains("reason", refused.Message);

            ShadingSelectionDecision departs = comparison.SelectShadingScheme(runnerUpScheme, "Lower material quantity and simpler maintenance.");
            Assert.True(departs.Successful);
            Assert.Equal(2, departs.SelectedRank);
            Assert.Equal(ShadingSelectionAlignment.DepartsFromLeader, departs.SelectionAlignment);
            Assert.Contains("departs from the analytical ranking", departs.DecisionSummary);
        }
    }
}
