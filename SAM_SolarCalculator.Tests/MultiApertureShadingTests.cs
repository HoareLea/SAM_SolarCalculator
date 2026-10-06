// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Core;
using SAM.Geometry.Object.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 10.1: what happens when a model has MORE THAN ONE WINDOW.
    ///
    /// The defect these tests were written for. Create.ApertureSolarContext flattens every
    /// analysable aperture's cells into one visibility cache so the expensive geometric work is
    /// shared between irradiance and shading. The attribution side never learned that: it required
    /// the cells it was given to be the WHOLE of that cache, and it was given one aperture's cells.
    /// On a one-window model those are the same thing and everything worked; on a ten-window model
    /// the attribution cache came back null for every aperture, every candidate scored NaN, and —
    /// because NaN > 0 is false — the optimiser reported "no shading is worth building here" for
    /// windows it had never managed to measure, while VerifyShading failed outright.
    ///
    /// So the property under test is not an implementation detail. It is: ONE APERTURE OF MANY MUST
    /// GIVE THE SAME ANSWER AS THAT APERTURE ANALYSED ALONE. Everything here is an assertion of
    /// that, plus the two semantics that stopped a failure from looking like an answer.
    /// </summary>
    public class MultiApertureShadingTests
    {
        private readonly ITestOutputHelper output;

        public MultiApertureShadingTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // ------------------------------------------------- the shared cell space itself ----

        [Fact]
        public void The_Shared_Cell_Space_Is_Bigger_Than_Any_One_Aperture()
        {
            // If this ever stopped being true the rest of the file would be testing nothing.
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();

            Assert.Equal(3, scenario.Targets.Count);
            Assert.Equal(scenario.TotalCellCount, scenario.SharedCache.CellCount);

            int expectedOffset = 0;
            for (int i = 0; i < scenario.Targets.Count; i++)
            {
                Assert.Equal(expectedOffset, scenario.Offsets[i]);
                Assert.True(scenario.Targets[i].CellCount < scenario.SharedCache.CellCount,
                    "each aperture must be a strict subset of the shared cell space, or the offset is never exercised");
                expectedOffset += scenario.Targets[i].CellCount;
            }

            // Three genuinely different orientations, so a wrong pairing cannot pass unnoticed.
            Assert.Equal(180.0, scenario.Targets[0].Azimuth, 3);
            Assert.Equal(90.0, scenario.Targets[1].Azimuth, 3);
            Assert.Equal(0.0, scenario.Targets[2].Azimuth, 3);
        }

        [Fact]
        public void Attribution_Is_A_Window_Into_The_Shared_Cell_Space()
        {
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();

            for (int i = 0; i < scenario.Targets.Count; i++)
            {
                SolarAttributionCache attribution = Weather.SolarCalculator.Create.SolarAttributionCache(
                    scenario.SharedCache, scenario.Context, scenario.Targets[i].AnalysisCells, scenario.Offsets[i]);

                // This is the call that used to return null for every aperture of a multi-window model.
                Assert.NotNull(attribution);
                Assert.Equal(scenario.Targets[i].CellCount, attribution.CellCount);
                Assert.Equal(scenario.Offsets[i], attribution.CellIndexOffset);
                Assert.Equal(scenario.SharedCache.BinCount, attribution.BinCount);
            }

            // A window that would run off the end of the cache is refused rather than read.
            Assert.Null(Weather.SolarCalculator.Create.SolarAttributionCache(
                scenario.SharedCache, scenario.Context, scenario.Targets[0].AnalysisCells, scenario.SharedCache.CellCount));
            Assert.Null(Weather.SolarCalculator.Create.SolarAttributionCache(
                scenario.SharedCache, scenario.Context, scenario.Targets[0].AnalysisCells, -1));
        }

        // --------------------------------------- one of many == alone: the whole point ----

        [Fact]
        public void Every_Aperture_Of_Many_Measures_Exactly_As_It_Would_Alone()
        {
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();
            IShadingTypology device = new Overhang(0.5);

            for (int i = 0; i < scenario.Targets.Count; i++)
            {
                ShadingPerformance shared = Analytical.SolarCalculator.Create.ShadingPerformance(
                    scenario.Targets[i], scenario.SharedCache, scenario.Desirabilities[i], scenario.Context, device, scenario.Offsets[i]);

                ShadingPerformance alone = Analytical.SolarCalculator.Create.ShadingPerformance(
                    scenario.Targets[i], scenario.IsolatedCaches[i], scenario.IsolatedDesirabilities[i], scenario.Context, device);

                Assert.NotNull(shared);
                Assert.NotNull(alone);

                Assert.Equal(alone.AdmittedDirectEnergy, shared.AdmittedDirectEnergy, 9);
                Assert.Equal(alone.AdmittedUnwantedEnergy, shared.AdmittedUnwantedEnergy, 9);
                Assert.Equal(alone.AdmittedWantedEnergy, shared.AdmittedWantedEnergy, 9);
                Assert.Equal(alone.DirectSolarIntercepted, shared.DirectSolarIntercepted, 9);
                Assert.Equal(alone.UnwantedSolarIntercepted, shared.UnwantedSolarIntercepted, 9);
                Assert.Equal(alone.WantedSolarBlocked, shared.WantedSolarBlocked, 9);
                Assert.Equal(alone.MaterialFraction, shared.MaterialFraction, 9);

                // Whatever it measured, it says which aperture it measured.
                Assert.Equal(scenario.Targets[i].ApertureGuid, shared.ApertureGuid);

                output.WriteLine($"az {scenario.Targets[i].Azimuth:0} offset {scenario.Offsets[i]}: " +
                    $"admitted {shared.AdmittedDirectEnergy:0.#} kWh, intercepted {shared.DirectSolarIntercepted:0.#} kWh");
            }
        }

        [Fact]
        public void Reading_The_Shared_Cache_At_The_Wrong_Offset_Is_A_Different_Answer()
        {
            // The teeth of the regression. If the offset were cosmetic, the fix would be untested:
            // this asserts that addressing the shared cache at cell 0 for an aperture that starts
            // elsewhere really does score it against ANOTHER window's admitted beam.
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();
            IShadingTypology device = new Overhang(0.5);

            // Index 1 is the east window; index 0 is the south window it would be confused with.
            ShadingPerformance correct = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Targets[1], scenario.SharedCache, scenario.Desirabilities[1], scenario.Context, device, scenario.Offsets[1]);

            ShadingPerformance wrongOffset = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Targets[1], scenario.SharedCache, scenario.Desirabilities[1], scenario.Context, device, 0);

            Assert.NotNull(correct);
            Assert.NotNull(wrongOffset);
            Assert.NotEqual(correct.DirectSolarIntercepted, wrongOffset.DirectSolarIntercepted, 6);

            output.WriteLine($"east window, correct offset {scenario.Offsets[1]}: {correct.DirectSolarIntercepted:0.###} kWh intercepted");
            output.WriteLine($"east window, offset 0 (the old behaviour): {wrongOffset.DirectSolarIntercepted:0.###} kWh intercepted");
        }

        [Fact]
        public void The_Optimiser_Chooses_The_Same_Device_In_A_Batch_As_On_Its_Own()
        {
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();

            for (int i = 0; i < scenario.Targets.Count; i++)
            {
                OptimisedShadingResult shared = Optimise.Overhang(
                    scenario.Targets[i], scenario.SharedCache, scenario.Desirabilities[i], scenario.Context,
                    new ShadingObjective(1.0, 0.1), null, 24, scenario.Offsets[i]);

                OptimisedShadingResult alone = Optimise.Overhang(
                    scenario.Targets[i], scenario.IsolatedCaches[i], scenario.IsolatedDesirabilities[i], scenario.Context,
                    new ShadingObjective(1.0, 0.1), null, 24);

                Assert.NotNull(shared);
                Assert.NotNull(alone);

                Assert.Equal(alone.TypologyName, shared.TypologyName);
                Assert.Equal(alone.Termination, shared.Termination);
                Assert.Equal(alone.RecommendsNoShading, shared.RecommendsNoShading);
                foreach (string name in alone.ParameterNames)
                {
                    Assert.Equal(alone.GetParameter(name), shared.GetParameter(name), 9);
                }

                Assert.Equal(alone.ObjectiveScore, shared.ObjectiveScore, 9);
                Assert.Equal(alone.Benefit, shared.Benefit, 9);
                Assert.Equal(alone.Harm, shared.Harm, 9);

                // Provenance names the aperture, not the position in a list.
                Assert.Equal(scenario.Targets[i].ApertureGuid, shared.ApertureGuid);

                output.WriteLine($"az {scenario.Targets[i].Azimuth:0}: {shared.TypologyName} depth {shared.GetParameter("Depth"):0.###} m, " +
                    $"score {shared.ObjectiveScore:0.##} kWh, no shading {shared.RecommendsNoShading}");
            }
        }

        [Fact]
        public void No_Aperture_Is_Ever_Given_Another_Apertures_Device()
        {
            // Two windows facing opposite ways cannot want the same device. This pins the pairing:
            // each result's identity is its own aperture, and a device carries the aperture it was
            // designed for so a mismatch is detectable rather than merely unlikely.
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();

            OptimisedShadingResult south = Optimise.Overhang(
                scenario.Targets[0], scenario.SharedCache, scenario.Desirabilities[0], scenario.Context,
                new ShadingObjective(1.0, 0.1), null, 24, scenario.Offsets[0]);
            OptimisedShadingResult north = Optimise.Overhang(
                scenario.Targets[2], scenario.SharedCache, scenario.Desirabilities[2], scenario.Context,
                new ShadingObjective(1.0, 0.1), null, 24, scenario.Offsets[2]);

            Assert.Equal(scenario.Targets[0].ApertureGuid, south.ApertureGuid);
            Assert.Equal(scenario.Targets[2].ApertureGuid, north.ApertureGuid);
            Assert.NotEqual(south.ApertureGuid, north.ApertureGuid);

            ShadingDevice southDevice = new ShadingDevice(south.ApertureGuid, south.Typology());
            Assert.Equal(scenario.Targets[0].ApertureGuid, southDevice.ApertureGuid);
            Assert.NotEqual(scenario.Targets[2].ApertureGuid, southDevice.ApertureGuid);

            // A south device measured on the north window produces a number — which is exactly why
            // the identity has to be checked BEFORE measuring rather than inferred afterwards.
            ShadingPerformance misapplied = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Targets[2], scenario.SharedCache, scenario.Desirabilities[2], scenario.Context,
                southDevice.Typology, scenario.Offsets[2]);
            Assert.NotNull(misapplied);
            Assert.Equal(scenario.Targets[2].ApertureGuid, misapplied.ApertureGuid);
            Assert.NotEqual(southDevice.ApertureGuid, misapplied.ApertureGuid);
        }

        // ------------------------------------- not re-tracing context is EXACT, not close ----

        [Fact]
        public void Attributing_Against_The_Candidate_Alone_Equals_Attributing_Against_The_Whole_Model()
        {
            // Stage 10.1 stopped re-projecting every context face for every candidate device,
            // because the base visibility cache already knows which rays context blocks. The
            // argument is in Create.ShadingPerformance; this is the measurement of it.
            //
            // The east window is the one standing under a soffit, so context is doing real work
            // here — on an unobstructed window the two routes would agree trivially and prove
            // nothing.
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();

            foreach (IShadingTypology device in new IShadingTypology[]
            {
                new Overhang(0.6, 0.1, 0.2),
                new HorizontalLouvres(0.4, 3, 10.0),
                new VerticalFins(0.5, 3, 0.0),
                new NoShading(),
            })
            {
                for (int i = 0; i < scenario.Targets.Count; i++)
                {
                    List<ShadingElement> elements = device.ShadingElements(scenario.Targets[i]);

                    // The exhaustive route: context AND candidate, every face traced.
                    List<LinkedFace3D> everything = new List<LinkedFace3D>(scenario.Context);
                    foreach (ShadingElement element in elements)
                    {
                        everything.Add(element.LinkedFace3D);
                    }

                    SolarAttributionCache exhaustive = Weather.SolarCalculator.Create.SolarAttributionCache(
                        scenario.SharedCache, everything, scenario.Targets[i].AnalysisCells, scenario.Offsets[i]);

                    ShadingPerformance viaEverything = Analytical.SolarCalculator.Create.ShadingPerformance(
                        scenario.Targets[i], scenario.SharedCache, exhaustive, scenario.Desirabilities[i],
                        elements, device.Name, device.MaterialFraction(scenario.Targets[i]), scenario.Offsets[i]);

                    // The route the components now take: the candidate's faces only.
                    ShadingPerformance viaCandidate = Analytical.SolarCalculator.Create.ShadingPerformance(
                        scenario.Targets[i], scenario.SharedCache, scenario.Desirabilities[i], scenario.Context, device, scenario.Offsets[i]);

                    Assert.NotNull(viaEverything);
                    Assert.NotNull(viaCandidate);

                    Assert.Equal(viaEverything.AdmittedDirectEnergy, viaCandidate.AdmittedDirectEnergy, 9);
                    Assert.Equal(viaEverything.AdmittedUnwantedEnergy, viaCandidate.AdmittedUnwantedEnergy, 9);
                    Assert.Equal(viaEverything.AdmittedWantedEnergy, viaCandidate.AdmittedWantedEnergy, 9);
                    Assert.Equal(viaEverything.DirectSolarIntercepted, viaCandidate.DirectSolarIntercepted, 9);
                    Assert.Equal(viaEverything.UnwantedSolarIntercepted, viaCandidate.UnwantedSolarIntercepted, 9);
                    Assert.Equal(viaEverything.WantedSolarBlocked, viaCandidate.WantedSolarBlocked, 9);
                    Assert.Equal(viaEverything.UnattributedInterceptedEnergy, viaCandidate.UnattributedInterceptedEnergy, 9);

                    // And per element, not merely in total: the same blade is credited the same kWh.
                    Dictionary<Guid, double> a = viaEverything.EnergyPerElement;
                    Dictionary<Guid, double> b = viaCandidate.EnergyPerElement;
                    Assert.Equal(a.Count, b.Count);
                    foreach (KeyValuePair<Guid, double> pair in a)
                    {
                        Assert.True(b.ContainsKey(pair.Key), "an element credited by one route must exist in the other");
                        Assert.Equal(pair.Value, b[pair.Key], 9);
                    }
                }

                output.WriteLine($"{device.Name}: candidate-only attribution matches whole-model attribution on all three apertures");
            }
        }

        // ------------------------------------------------------- no-shading semantics ----

        [Fact]
        public void A_Run_That_Could_Not_Be_Measured_Is_Not_Reported_As_No_Shading_Needed()
        {
            // The second half of the original defect: with nothing measurable, every score is NaN,
            // and NaN > 0 is false. Testing the score alone therefore announced a confident
            // engineering recommendation the run had never earned.
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();

            OptimisedShadingResult broken = Optimise.Overhang(
                scenario.Targets[0], scenario.SharedCache, scenario.Desirabilities[0], scenario.Context,
                new ShadingObjective(1.0, 0.1), null, 8, scenario.SharedCache.CellCount); // an impossible window

            Assert.NotNull(broken);
            Assert.Equal(ShadingOptimisationTermination.EvaluationFailed, broken.Termination);
            Assert.False(broken.RecommendsNoShading, "an unmeasurable run must never read as 'no shading is worth building'");
            Assert.Equal(ShadingDesignStatus.NotEvaluated, Analytical.SolarCalculator.Query.DesignStatus(broken));
            Assert.Contains("Not evaluated", Analytical.SolarCalculator.Query.DesignSummary(broken, scenario.Targets[0].Azimuth));
        }

        [Fact]
        public void The_Null_Device_Is_A_Measurable_Answer_Not_A_Failure()
        {
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();

            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Targets[0], scenario.SharedCache, scenario.Desirabilities[0], scenario.Context,
                new NoShading(), scenario.Offsets[0]);

            Assert.NotNull(performance);
            Assert.Equal("NoShading", performance.TypologyName);
            Assert.Equal(0.0, performance.DirectSolarIntercepted, 9);
            Assert.Equal(0.0, performance.UnattributedInterceptedEnergy, 9);
            Assert.Equal(0.0, performance.MaterialFraction, 9);

            // The baseline is real, so the percentages are the true ones rather than invented.
            Assert.True(performance.AdmittedDirectEnergy > 0);
            Assert.Equal(0.0, performance.UnwantedSolarBlocked, 9);
            Assert.Equal(1.0, performance.WantedSolarRetained, 9);

            // And it reads as an answer.
            string summary = Analytical.SolarCalculator.Query.VerificationSummary(performance, scenario.Targets[0].Azimuth);
            Assert.Contains("No shading", summary);
            output.WriteLine(summary);
        }

        [Fact]
        public void A_Percentage_Without_A_Denominator_Stays_Unavailable_Even_For_The_Null_Device()
        {
            // Everything wanted, nothing unwanted: 'unwanted solar blocked' has no denominator. The
            // null device must not report 0 % of nothing as though it meant something.
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();

            ApertureDesirability allWanted = Analytical.SolarCalculator.Create.ApertureDesirability(
                scenario.Targets[0], scenario.SharedCache,
                new SeasonalDesirability(null, new Core.SolarCalculator.AnalysisPeriod(MultiApertureFixture.Year)),
                TestHelpers.SolarSymmetricWeatherData(MultiApertureFixture.Year, TestHelpers.London(), 30.0));

            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Targets[0], scenario.SharedCache, allWanted, scenario.Context, new NoShading(), scenario.Offsets[0]);

            Assert.NotNull(performance);
            Assert.True(double.IsNaN(performance.UnwantedSolarBlocked), "no unwanted solar means the metric is unavailable, not 0 %");
            Assert.Equal(1.0, performance.WantedSolarRetained, 9);

            Assert.Contains("n/a unwanted blocked", Analytical.SolarCalculator.Query.VerificationSummary(performance, scenario.Targets[0].Azimuth));
        }

        // ---------------------------------------------------- identity and reporting ----

        [Fact]
        public void A_Device_Carries_The_Aperture_It_Was_Designed_For_Through_A_Save_And_Reload()
        {
            // A Grasshopper file stores every wired object as SAM JSON. If identity did not survive
            // that, a reopened script would silently lose the check it depends on.
            ShadingDevice device = new ShadingDevice(
                new Guid("cccccccc-0000-0000-0000-000000000001"), new HorizontalLouvres(0.35, 3, 15.0));

            ShadingDevice reloaded = Core.Create.IJSAMObject<ShadingDevice>(device.ToJsonObject().ToJsonString());

            Assert.NotNull(reloaded);
            Assert.Equal(device.ApertureGuid, reloaded.ApertureGuid);
            Assert.Equal("HorizontalLouvres", reloaded.TypologyName);
            Assert.False(reloaded.IsNoShading);
            foreach (string name in device.Typology.ParameterNames)
            {
                Assert.Equal(device.Typology.GetParameter(name), reloaded.Typology.GetParameter(name), 9);
            }

            ShadingDevice none = Core.Create.IJSAMObject<ShadingDevice>(
                new ShadingDevice(device.ApertureGuid, new NoShading()).ToJsonObject().ToJsonString());
            Assert.True(none.IsNoShading);
        }

        [Fact]
        public void A_Design_Summary_Says_Which_Window_And_Never_Invents_A_Number()
        {
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();

            OptimisedShadingResult result = Optimise.Overhang(
                scenario.Targets[0], scenario.SharedCache, scenario.Desirabilities[0], scenario.Context,
                new ShadingObjective(1.0, 0.1), null, 24, scenario.Offsets[0]);

            string summary = Analytical.SolarCalculator.Query.DesignSummary(result, scenario.Targets[0].Azimuth);
            output.WriteLine(summary);

            Assert.StartsWith("180°", summary);
            Assert.Contains("unwanted blocked", summary);
            Assert.Contains("wanted retained", summary);
            Assert.DoesNotContain("NaN", summary);

            // The energies are named for the physical quantity, not for their role in the objective.
            // "46 kWh benefit" was read during manual testing as a saving; it is the unwanted beam
            // this device intercepts, which is a narrower claim and the only one measured.
            Assert.Contains("kWh unwanted solar intercepted", summary);
            Assert.Contains("kWh wanted solar blocked", summary);
            Assert.DoesNotContain("benefit", summary);

            // Depths read in metres, not as bare numbers.
            if (!result.RecommendsNoShading)
            {
                Assert.Contains("Depth", summary);
                Assert.Contains(" m", summary);
            }

            // The status words are the ones a batch table is read by.
            Assert.Equal("OK", Analytical.SolarCalculator.Query.StatusText(ShadingDesignStatus.Ok));
            Assert.Equal("NO SHADE", Analytical.SolarCalculator.Query.StatusText(ShadingDesignStatus.NoShading));
            Assert.Equal("WARNING", Analytical.SolarCalculator.Query.StatusText(ShadingDesignStatus.Warning));
            Assert.Equal("NOT EVALUATED", Analytical.SolarCalculator.Query.StatusText(ShadingDesignStatus.NotEvaluated));
        }

        [Fact]
        public void A_Device_Too_Fine_For_The_Grid_Is_Reported_As_A_Warning_Not_As_Success()
        {
            MultiApertureFixture.Scenario scenario = MultiApertureFixture.ThreeOrientations();

            OptimisedShadingResult result = Optimise.Overhang(
                scenario.Targets[0], scenario.SharedCache, scenario.Desirabilities[0], scenario.Context,
                new ShadingObjective(1.0, 0.1), null, 24, scenario.Offsets[0]);

            Assert.Equal(ShadingDesignStatus.Ok, Analytical.SolarCalculator.Query.DesignStatus(result, ShadingResolutionState.Resolved));
            Assert.Equal(ShadingDesignStatus.Warning, Analytical.SolarCalculator.Query.DesignStatus(result, ShadingResolutionState.NearResolutionLimit));
            Assert.Equal(ShadingDesignStatus.Warning, Analytical.SolarCalculator.Query.DesignStatus(result, ShadingResolutionState.BelowResolutionLimit));
        }
    }
}
