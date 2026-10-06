// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 9 validation. Every case is built so that the expected behaviour is arguable from the
    /// physics BEFORE anything is measured, and several are deliberately cases where the answer
    /// could go either way and the test reports what the energy says rather than asserting a
    /// preferred outcome.
    /// </summary>
    public class ShadingOptimisationTests
    {
        private readonly ITestOutputHelper output;

        public ShadingOptimisationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private void Report(string label, OptimisedShadingResult result)
        {
            if (result == null)
            {
                output.WriteLine($"{label}: no result");
                return;
            }

            string parameters = string.Empty;
            foreach (string name in result.ParameterNames)
            {
                parameters += $"{name}={result.GetParameter(name):0.###} ";
            }

            output.WriteLine($"{label}: {result.TypologyName} {parameters}");
            output.WriteLine($"   score {result.ObjectiveScore:0.###} kWh = benefit {result.Benefit:0.###} - {result.Objective.WantedSolarPenalty:0.##}x harm {result.Harm:0.###} - {result.Objective.MaterialPenalty:0.##}x cost {result.Cost:0.###}");
            output.WriteLine($"   intercepted {result.DirectSolarIntercepted:0.#} kWh, efficiency {Percent(result.DirectShadingEfficiency)}, unwanted blocked {Percent(result.UnwantedSolarBlocked)}, wanted retained {Percent(result.WantedSolarRetained)}, material {result.MaterialFraction:0.###}");
            output.WriteLine($"   {result.Evaluations} evaluations, {result.Iterations} iterations, {result.ElapsedMilliseconds:0} ms ({result.GeometryMilliseconds:0} geometry / {result.EvaluationMilliseconds:0} rays / {result.OptimiserOverheadMilliseconds:0} optimiser), {result.Termination}");
        }

        private static string Percent(double ratio)
        {
            return double.IsNaN(ratio) ? "n/a" : (ratio * 100).ToString("0.#") + " %";
        }

        // ------------------------------------------------------------------ case 1 ----

        [Fact]
        public void Case1_Summer_South_Aperture_Finds_A_Beneficial_Overhang()
        {
            // A south window under a summer-unwanted / winter-wanted brief. High summer sun is the
            // problem, so a horizontal device above the head is the physically indicated answer and
            // it must beat doing nothing.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            OptimisedShadingResult result = Optimise.Overhang(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);

            Assert.NotNull(result);
            Report("case 1 south overhang", result);

            // Beating the null device is the whole claim: the null device scores exactly zero.
            Assert.True(result.ObjectiveScore > 0, "an overhang on a summer-problem south window must beat building nothing");
            Assert.False(result.RecommendsNoShading);

            // It must be a real device, not the minimum permitted one.
            Assert.True(result.GetParameter("Depth") > 0.05, "the winner must not be the minimum-depth degenerate");

            // And it must actually block unwanted solar the unshaded aperture was admitting.
            Assert.True(result.UnwantedSolarBlocked > 0.1, $"only {result.UnwantedSolarBlocked * 100:0.#} % of unwanted solar blocked");
            Assert.InRange(result.UnwantedSolarBlocked, 0.0, 1.0);
            Assert.InRange(result.WantedSolarRetained, 0.0, 1.0);

            // Benefit and harm are both positive quantities; the score subtracts the harm.
            Assert.True(result.Benefit > 0);
            Assert.True(result.Harm >= 0);
            Assert.True(result.Cost > 0);
            Assert.Equal(result.Benefit - result.Objective.WantedSolarPenalty * result.Harm - result.Objective.MaterialPenalty * result.Cost, result.ObjectiveScore, 9);
        }

        // ------------------------------------------------------------------ case 2 ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Case2_A_Deeper_Device_Eventually_Loses_Because_It_Destroys_Wanted_Solar()
        {
            // The sign test, done on the objective rather than on the score's algebra. Sweeping the
            // depth must show the score rise and then FALL: past some depth the device is
            // destroying more winter sun (times lambda) plus material than the summer sun it gains.
            // If the harm term had the wrong sign the score would be monotonic in depth.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();
            ShadingObjective objective = new ShadingObjective();

            output.WriteLine("depth |   benefit |      harm |      cost |     score");
            double bestScore = double.NegativeInfinity;
            double bestDepth = double.NaN;
            double deepestScore = double.NaN;

            foreach (double depth in new double[] { 0.1, 0.2, 0.3, 0.4, 0.6, 0.8, 1.2, 1.8, 2.5, 3.0 })
            {
                ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, new Overhang(depth));

                double score = objective.Score(performance);
                output.WriteLine($"{depth,5:0.0} | {objective.Benefit(performance),9:0.##} | {objective.Harm(performance),9:0.##} | {objective.Cost(performance),9:0.##} | {score,9:0.##}");

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDepth = depth;
                }

                deepestScore = score;
            }

            output.WriteLine($"best sweep depth {bestDepth:0.0} m at {bestScore:0.##} kWh; deepest (3.0 m) scores {deepestScore:0.##} kWh");

            // The optimum is interior: growing the device without limit must lose.
            Assert.True(bestDepth < 3.0, "the objective must not be monotonically improved by depth");
            Assert.True(deepestScore < bestScore, "a 3 m overhang must score worse than the sweep optimum");

            // And the optimiser agrees with the sweep about not running to the bound.
            OptimisedShadingResult result = Optimise.Overhang(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);
            Report("case 2 optimised", result);
            Assert.True(result.GetParameter("Depth") < 3.0);
        }

        // ------------------------------------------------------------------ case 3 ----

        [Fact]
        public void Case3_An_East_Aperture_Is_Judged_On_Energy_Not_On_Assumption()
        {
            // East-facing glass takes low, oblique morning sun. The received wisdom is that fins
            // beat an overhang there, but the point of Stage 9 is that the energy decides, so this
            // test REPORTS the comparison and asserts only what must be true either way.
            //
            // The measurement is worth reading rather than skimming: on this aperture the two come
            // out within 0.2 % of each other, and the fin array gets there with roughly half the
            // material. A brief that weighted material more heavily would flip the winner, which is
            // exactly why the objective's terms are reported alongside its scalar.
            OptimisationFixture.Scenario scenario = OptimisationFixture.EastSeasonal();

            List<string> eligible = Analytical.SolarCalculator.Query.EligibleShadingTypologies(
                scenario.Target, scenario.BaseCache, scenario.Desirability, out double highSun, out double obliqueSun);

            output.WriteLine($"east aperture (azimuth {scenario.Target.Azimuth:0.#} deg): {highSun * 100:0.#} % of unwanted energy is high-sun, {obliqueSun * 100:0.#} % is oblique");
            output.WriteLine($"eligible families: {string.Join(", ", eligible)}");

            OptimisedShadingResult overhang = Optimise.Overhang(scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);
            OptimisedShadingResult fins = Optimise.VerticalFins(scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);

            Report("case 3 overhang", overhang);
            Report("case 3 vertical fins", fins);

            // The optimiser must be sensitive to lateral shading here: fins must find something
            // worth building on a facade that takes genuinely oblique sun, and must choose a
            // non-degenerate depth to do it.
            Assert.True(obliqueSun > 0.2, "an east facade must see a substantial share of its unwanted solar obliquely");
            Assert.Contains("VerticalFins", eligible);
            Assert.True(fins.ObjectiveScore > 0, "fins must beat building nothing on an east facade");
            Assert.True(fins.GetParameter("Depth") > 0.05, "the fin solution must not be the minimum-depth degenerate");
            Assert.True(fins.UnwantedSolarBlocked > 0.1, "fins must block a real share of the oblique unwanted beam");

            output.WriteLine($"winner on energy: {(fins.ObjectiveScore >= overhang.ObjectiveScore ? "VerticalFins" : "Overhang")} " +
                             $"({Math.Max(fins.ObjectiveScore, overhang.ObjectiveScore):0.##} vs {Math.Min(fins.ObjectiveScore, overhang.ObjectiveScore):0.##} kWh)");
        }

        // ------------------------------------------------------------------ case 4 ----

        [Fact]
        public void Case4_Context_Already_Blocking_The_Sun_Must_Not_Buy_More_Material()
        {
            // A wide soffit 2.5 m above the head already removes everything above roughly 27
            // degrees of profile angle — exactly the high summer sun an overhang exists to catch.
            // What is left is low-profile beam that no device above the head can reach.
            //
            // The requirement is precise: the optimiser must not grow material to intercept energy
            // that never arrives. So the test is not "less material than the open case" — material
            // is priced against the aperture's own admitted beam, and both sides of that ratio move
            // — but the sharper claim that the BENEFIT collapses with the resource, and that the
            // device shrinks towards nothing rather than chasing sun the context already took.
            OptimisationFixture.Scenario open = OptimisationFixture.SouthSeasonal();
            OptimisationFixture.Scenario blocked = OptimisationFixture.SouthBlocked();

            OptimisedShadingResult openResult = Optimise.Overhang(open.Target, open.BaseCache, open.Desirability, open.Context);
            OptimisedShadingResult blockedResult = Optimise.Overhang(blocked.Target, blocked.BaseCache, blocked.Desirability, blocked.Context);

            Report("case 4 open", openResult);
            Report("case 4 obstructed", blockedResult);

            output.WriteLine($"admitted unwanted: open {openResult.AdmittedUnwantedEnergy:0.##} kWh, obstructed {blockedResult.AdmittedUnwantedEnergy:0.##} kWh " +
                             $"({blockedResult.AdmittedUnwantedEnergy / openResult.AdmittedUnwantedEnergy * 100:0.#} % of the open case)");
            output.WriteLine($"benefit: open {openResult.Benefit:0.##} kWh, obstructed {blockedResult.Benefit:0.##} kWh " +
                             $"({blockedResult.Benefit / openResult.Benefit * 100:0.#} % of the open case)");
            output.WriteLine($"depth:   open {openResult.GetParameter("Depth"):0.###} m, obstructed {blockedResult.GetParameter("Depth"):0.###} m");

            // The context genuinely removes the high sun, or the case proves nothing.
            Assert.True(blockedResult.AdmittedUnwantedEnergy < 0.5 * openResult.AdmittedUnwantedEnergy,
                "the soffit must remove a substantial share of the unwanted beam before it arrives");

            // No credit may be taken for what the context already blocks. Two independent checks:
            // nothing lands on a non-candidate face, and the benefit cannot exceed what arrived.
            Assert.Equal(0.0, blockedResult.UnattributedInterceptedEnergy);
            Assert.True(blockedResult.Benefit <= blockedResult.AdmittedUnwantedEnergy + 1e-9,
                "a device cannot intercept more unwanted solar than reached the aperture");

            // The benefit collapses with the resource: the optimiser is not inventing energy.
            Assert.True(blockedResult.Benefit < 0.5 * openResult.Benefit,
                $"obstructed benefit {blockedResult.Benefit:0.##} kWh against {openResult.Benefit:0.##} kWh in the open case");

            // And the device shrinks rather than growing to chase sun that is not there.
            Assert.True(blockedResult.GetParameter("Depth") < openResult.GetParameter("Depth") + 1e-9,
                $"obstructed depth {blockedResult.GetParameter("Depth"):0.###} m against {openResult.GetParameter("Depth"):0.###} m in the open case");
        }

        // ------------------------------------------------------------------ case 5 ----

        [Fact]
        public void Case5_With_No_Unwanted_Solar_The_Answer_Is_To_Build_Nothing()
        {
            // Every hour of sun is welcome. Any device can only destroy wanted solar and cost
            // material, so no candidate can beat the null device and the result must SAY so rather
            // than hand back the least-bad geometry as a recommendation.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthAllWanted();

            OptimisedShadingResult result = Optimise.Overhang(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);

            Assert.NotNull(result);
            Report("case 5 no unwanted solar", result);

            Assert.Equal(0.0, result.AdmittedUnwantedEnergy);
            Assert.Equal(0.0, result.Benefit);
            Assert.True(double.IsNaN(result.UnwantedSolarBlocked), "a zero denominator must be NaN, not 0 %");

            Assert.True(result.RecommendsNoShading, "with nothing to gain, the recommendation must be to build nothing");
            Assert.Equal(ShadingOptimisationTermination.NoBeneficialCandidate, result.Termination);
            Assert.True(result.ObjectiveScore <= 0);

            // And the least-bad candidate it fell back on is the minimum device, because the cost
            // term is scaled by the admitted DIRECT beam and so survives the absence of unwanted
            // solar. This is the Gate 0 Review C finding in action.
            Assert.True(result.GetParameter("Depth") <= 0.06,
                $"the fallback candidate should be the minimum device, got depth {result.GetParameter("Depth"):0.###} m");
        }

        // ------------------------------------------------------------------ case 6 ----

        [Fact]
        public void Case6_With_No_Wanted_Solar_Blocking_More_Stays_Worth_It_Up_To_The_Material_Cost()
        {
            // The mirror case. There is no winter sun to protect, so the only brake on growing the
            // device is the material term — and it must still be a brake, or the optimiser would
            // run straight to the depth bound.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthAllUnwanted();

            OptimisedShadingResult result = Optimise.Overhang(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);

            Assert.NotNull(result);
            Report("case 6 no wanted solar", result);

            Assert.Equal(0.0, result.AdmittedWantedEnergy);
            Assert.Equal(0.0, result.Harm);
            Assert.True(double.IsNaN(result.WantedSolarRetained), "a zero denominator must be NaN, not 100 %");

            Assert.False(result.RecommendsNoShading);
            Assert.True(result.ObjectiveScore > 0);
            Assert.True(result.UnwantedSolarBlocked > 0.3, "with everything unwanted, a real device must block a lot of it");

            // The material term is the only thing stopping it, so it must have stopped it: an
            // unopposed benefit term would take the depth to its bound.
            result.Bounds.ForEach(x =>
            {
                if (x.Name == "Depth")
                {
                    output.WriteLine($"depth bound [{x.Minimum:0.##}, {x.Maximum:0.##}] m, chosen {result.GetParameter("Depth"):0.###} m");
                }
            });

            // Raising the material penalty must shrink the device: proof the cost term has teeth.
            OptimisedShadingResult expensive = Optimise.ShadingTypology(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, "Overhang",
                new ShadingObjective(1.0, 2.0));

            Report("case 6 with a 20x material penalty", expensive);
            Assert.True(expensive.MaterialFraction < result.MaterialFraction,
                "a heavier material penalty must buy less material");
        }

        // ------------------------------------------------------------------ case 7 ----

        [Fact]
        public void Case7_On_A_One_Dimensional_Problem_The_Optimiser_Finds_The_Enumerated_Optimum()
        {
            // The load-bearing test. Everything except Depth is pinned, and the depth range is
            // enumerated EXHAUSTIVELY at the search granularity, so the true optimum on the search
            // lattice is known by construction rather than argued. The optimiser must land on it,
            // or in the immediately adjacent step.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();
            ShadingObjective objective = new ShadingObjective();

            const double minimum = 0.05;
            const double maximum = 1.50;
            const double step = 0.05;

            List<ShadingParameter> parameters = new List<ShadingParameter>
            {
                new ShadingParameter("Depth", minimum, maximum, step),
                new ShadingParameter("RiseAboveHead", 0.0, 0.0, 0.01),
                new ShadingParameter("ExtensionBeyondJambs", 0.0, 0.0, 0.01),
            };

            // Exhaustive enumeration: the ground truth.
            double bestDepth = double.NaN;
            double bestScore = double.NegativeInfinity;
            output.WriteLine("depth |     score");
            for (double depth = minimum; depth <= maximum + 1e-9; depth += step)
            {
                ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, new Overhang(depth));

                double score = objective.Score(performance);
                output.WriteLine($"{depth,5:0.00} | {score,9:0.####}");
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDepth = depth;
                }
            }

            OptimisedShadingResult result = Optimise.ShadingTypology(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                "Overhang", objective, parameters);

            Report("case 7 optimised", result);
            output.WriteLine($"enumerated optimum: depth {bestDepth:0.00} m at {bestScore:0.####} kWh");
            output.WriteLine($"optimiser        : depth {result.GetParameter("Depth"):0.00} m at {result.ObjectiveScore:0.####} kWh, {result.Evaluations} of {(int)Math.Round((maximum - minimum) / step) + 1} lattice points evaluated");

            // Same neighbourhood: within one search step of the enumerated optimum.
            Assert.True(Math.Abs(result.GetParameter("Depth") - bestDepth) <= step + 1e-9,
                $"optimiser found {result.GetParameter("Depth"):0.00} m, enumeration found {bestDepth:0.00} m");

            // And no worse in objective value than the enumerated best, to numerical noise.
            Assert.True(result.ObjectiveScore >= bestScore - 1e-6 * Math.Max(1.0, Math.Abs(bestScore)),
                $"optimiser scored {result.ObjectiveScore:0.######}, enumeration found {bestScore:0.######}");

            // It must also be cheaper than brute force, or the search is not earning its place.
            int latticePoints = (int)Math.Round((maximum - minimum) / step) + 1;
            Assert.True(result.Evaluations < latticePoints, $"{result.Evaluations} evaluations against {latticePoints} lattice points");
        }

        // ------------------------------------------------------------------ case 8 ----

        [Fact]
        public void Case8_Repeated_Optimisation_Returns_Identical_Parameters_And_Score()
        {
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            OptimisedShadingResult first = Optimise.HorizontalLouvres(scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);
            OptimisedShadingResult second = Optimise.HorizontalLouvres(scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);

            Report("case 8 run 1", first);
            Report("case 8 run 2", second);

            Assert.Equal(first.TypologyName, second.TypologyName);
            Assert.Equal(first.ParameterNames, second.ParameterNames);
            foreach (string name in first.ParameterNames)
            {
                Assert.Equal(first.GetParameter(name), second.GetParameter(name));
            }

            // Bit-identical, not merely close: no reduction order or dictionary iteration may leak in.
            Assert.Equal(first.ObjectiveScore, second.ObjectiveScore);
            Assert.Equal(first.Benefit, second.Benefit);
            Assert.Equal(first.Harm, second.Harm);
            Assert.Equal(first.Cost, second.Cost);
            Assert.Equal(first.Evaluations, second.Evaluations);
            Assert.Equal(first.Iterations, second.Iterations);
            Assert.Equal(first.Termination, second.Termination);
            Assert.Equal(first.ElementGuids, second.ElementGuids);
            Assert.Equal(first.AttributionTableHash, second.AttributionTableHash);

            // The serialised form is identical too — timings excepted, which are wall clock.
            JsonObject a = first.ToJsonObject();
            JsonObject b = second.ToJsonObject();
            foreach (string key in new string[] { "ElapsedMilliseconds", "GeometryMilliseconds", "EvaluationMilliseconds" })
            {
                a.Remove(key);
                b.Remove(key);
            }

            Assert.Equal(a.ToJsonString(), b.ToJsonString());
        }

        [Fact]
        public void Result_Round_Trips_Through_Json()
        {
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();
            OptimisedShadingResult result = Optimise.Overhang(scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);

            JsonObject jObject = result.ToJsonObject();
            OptimisedShadingResult restored = new OptimisedShadingResult(jObject);

            Assert.Equal(result.ApertureGuid, restored.ApertureGuid);
            Assert.Equal(result.TypologyName, restored.TypologyName);
            Assert.Equal(result.ParameterNames, restored.ParameterNames);
            foreach (string name in result.ParameterNames)
            {
                Assert.Equal(result.GetParameter(name), restored.GetParameter(name));
                Assert.Equal(result.GetSeedParameter(name), restored.GetSeedParameter(name));
            }

            Assert.Equal(result.ObjectiveScore, restored.ObjectiveScore);
            Assert.Equal(result.Benefit, restored.Benefit);
            Assert.Equal(result.Harm, restored.Harm);
            Assert.Equal(result.Cost, restored.Cost);
            Assert.Equal(result.DirectSolarIntercepted, restored.DirectSolarIntercepted);
            Assert.Equal(result.MaterialFraction, restored.MaterialFraction);
            Assert.Equal(result.ElementGuids, restored.ElementGuids);
            Assert.Equal(result.Evaluations, restored.Evaluations);
            Assert.Equal(result.Termination, restored.Termination);
            Assert.Equal(result.RecommendsNoShading, restored.RecommendsNoShading);
            Assert.Equal(result.AttributionTableHash, restored.AttributionTableHash);
            Assert.Equal(result.Objective.WantedSolarPenalty, restored.Objective.WantedSolarPenalty);
            Assert.Equal(result.Objective.MaterialCostReference, restored.Objective.MaterialCostReference);
            Assert.Equal(result.Bounds.Count, restored.Bounds.Count);

            // A second round trip is byte-identical, which is what makes the form storable.
            Assert.Equal(jObject.ToJsonString(), restored.ToJsonObject().ToJsonString());

            // And the winning device rebuilds from the stored name and parameters alone.
            IShadingTypology typology = restored.Typology();
            Assert.NotNull(typology);
            Assert.Equal(result.GetParameter("Depth"), typology.GetParameter("Depth"));

            List<ShadingElement> elements = typology.ShadingElements(scenario.Target);
            List<Guid> guids = new List<Guid>();
            foreach (ShadingElement element in elements) { guids.Add(element.Guid); }
            Assert.Equal(result.ElementGuids, guids);
        }

        // ------------------------------------------------------------------ case 9 ----

        [Fact]
        public void Case9_Optimised_Improves_On_The_Stage8_Rationalised_Seed()
        {
            // The progression, measured end to end on one aperture and one objective:
            //
            //   Stage 7 ideal        the selected voxel solid — physically unconstrained, not
            //                        buildable, and NOT a target the optimiser has to beat
            //   Stage 8 rationalised the seeded candidate sweep of the same family
            //   Stage 9 optimised    the search over the same family's bounded parameter space
            //
            // The claim under test is only the one that must hold: Stage 9 searches a superset of
            // what Stage 8's fixed sweep can reach, under a declared objective, so it must equal or
            // beat the Stage 8 device ON THAT OBJECTIVE. Beating the unconstrained ideal is not
            // required and would be suspicious.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();
            ShadingObjective objective = new ShadingObjective();

            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(
                scenario.Target, maxDepth: 1.5, up: 1.0, down: 0.2, left: 0.5, right: 0.5, voxelSize: 0.1);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(
                scenario.Target, scenario.BaseCache, scenario.Desirability, volume);

            // --- Stage 7 ideal, as the voxel solid the field actually means.
            IdealShadingResult ideal = Analytical.SolarCalculator.Create.IdealShadingResult(
                field, ShadingThresholdMethod.CumulativeCapture, 0.9, objective.WantedSolarPenalty, true, false);

            List<ShadingElement> idealElements = Analytical.SolarCalculator.Query.VoxelSurfaceShadingElements(volume, ideal.VoxelIndices);
            List<LinkedFace3D> idealOccluders = new List<LinkedFace3D>();
            foreach (ShadingElement element in idealElements) { idealOccluders.Add(element.LinkedFace3D); }

            SolarAttributionCache idealAttribution = Weather.SolarCalculator.Create.SolarAttributionCache(
                scenario.BaseCache, idealOccluders, scenario.Target.AnalysisCells);
            ShadingPerformance idealPerformance = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, idealAttribution, scenario.Desirability, idealElements, "Ideal", double.NaN);

            // --- Stage 8 rationalised, same family.
            IShadingTypology rationalised = Analytical.SolarCalculator.Create.RationalisedShading(
                field, scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                "Overhang", out ShadingPerformance rationalisedPerformance, objective.WantedSolarPenalty);

            // --- Stage 9 optimised, same family, seeded from the same field.
            OptimisedShadingResult optimised = Optimise.Overhang(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, objective, field);

            double idealScore = objective.Score(idealPerformance);
            double rationalisedScore = objective.Score(rationalisedPerformance);

            output.WriteLine("stage                       | device                    | benefit | harm    | cost    | score   | unwanted blocked | wanted retained");
            output.WriteLine($"Stage 7 ideal (voxel solid) | {idealElements.Count,4} boundary faces      | {objective.Benefit(idealPerformance),7:0.#} | {objective.Harm(idealPerformance),7:0.#} | {objective.Cost(idealPerformance),7:0.#} | {idealScore,7:0.#} | {Percent(idealPerformance.UnwantedSolarBlocked),16} | {Percent(idealPerformance.WantedSolarRetained),15}");
            output.WriteLine($"Stage 8 rationalised        | Overhang {rationalised.GetParameter("Depth"),0:0.###} m ext {rationalised.GetParameter("ExtensionBeyondJambs"):0.##} | {objective.Benefit(rationalisedPerformance),7:0.#} | {objective.Harm(rationalisedPerformance),7:0.#} | {objective.Cost(rationalisedPerformance),7:0.#} | {rationalisedScore,7:0.#} | {Percent(rationalisedPerformance.UnwantedSolarBlocked),16} | {Percent(rationalisedPerformance.WantedSolarRetained),15}");
            output.WriteLine($"Stage 9 optimised           | Overhang {optimised.GetParameter("Depth"),0:0.###} m ext {optimised.GetParameter("ExtensionBeyondJambs"):0.##} | {optimised.Benefit,7:0.#} | {optimised.Harm,7:0.#} | {optimised.Cost,7:0.#} | {optimised.ObjectiveScore,7:0.#} | {Percent(optimised.UnwantedSolarBlocked),16} | {Percent(optimised.WantedSolarRetained),15}");
            output.WriteLine("");
            output.WriteLine($"Stage 9 improvement over the Stage 8 device: {optimised.ObjectiveScore - rationalisedScore:+0.###;-0.###} kWh");
            output.WriteLine($"Stage 9 seed score {optimised.SeedObjectiveScore:0.###} kWh -> final {optimised.ObjectiveScore:0.###} kWh ({optimised.ObjectiveImprovement:+0.###;-0.###} kWh from the search itself)");
            output.WriteLine($"{optimised.Evaluations} evaluations, {optimised.ElapsedMilliseconds:0} ms");

            // The requirement.
            Assert.True(optimised.ObjectiveScore >= rationalisedScore - 1e-9,
                $"Stage 9 scored {optimised.ObjectiveScore:0.####} against Stage 8's {rationalisedScore:0.####}");

            // Everything must still conserve and stay bounded.
            Assert.Equal(idealPerformance.DirectSolarIntercepted, idealPerformance.ReconciledInterceptedEnergy, 6);
            Assert.InRange(optimised.UnwantedSolarBlocked, 0.0, 1.0);
            Assert.InRange(optimised.WantedSolarRetained, 0.0, 1.0);

            // Stage 9 is NOT required to beat the physically unconstrained ideal, and this records
            // which way it actually went rather than asserting a preference.
            output.WriteLine(optimised.ObjectiveScore >= idealScore
                ? "the buildable device also beats the unconstrained ideal on this objective — the ideal over-shades, destroying wanted solar"
                : "the unconstrained ideal scores higher, as expected for a free-form solid");
        }

        // ------------------------------------------------------------------ case 10 ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Case10_Per_Element_Attribution_Reconciles_With_Total_Intercepted_Energy()
        {
            // Conservation on an optimised device with MANY overlapping elements, which is where a
            // double count would show. The egg crate's louvres and fins cross everywhere.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            OptimisedShadingResult result = Optimise.EggCrate(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);

            Assert.NotNull(result);
            Report("case 10 optimised egg crate", result);

            // Rebuild the winner and re-measure it independently of the optimiser.
            IShadingTypology typology = result.Typology();
            List<ShadingElement> elements = typology.ShadingElements(scenario.Target);
            List<LinkedFace3D> occluders = new List<LinkedFace3D>(scenario.Context);
            foreach (ShadingElement element in elements)
            {
                occluders.Add(element.LinkedFace3D);
            }

            SolarAttributionCache attribution = Weather.SolarCalculator.Create.SolarAttributionCache(
                scenario.BaseCache, occluders, scenario.Target.AnalysisCells);

            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, attribution, scenario.Desirability, elements,
                result.TypologyName, typology.MaterialFraction(scenario.Target));

            double sum = 0;
            int credited = 0;
            foreach (KeyValuePair<Guid, double> pair in performance.EnergyPerElement)
            {
                sum += pair.Value;
                if (pair.Value > 0) { credited++; }
            }

            output.WriteLine($"{elements.Count} elements, {credited} credited, per-element sum {sum:0.######} kWh + residual {performance.UnattributedInterceptedEnergy:0.######} kWh = {performance.ReconciledInterceptedEnergy:0.######} kWh");
            output.WriteLine($"total intercepted {performance.DirectSolarIntercepted:0.######} kWh");

            Assert.Equal(performance.DirectSolarIntercepted, performance.ReconciledInterceptedEnergy, 9);
            Assert.Equal(0.0, performance.UnattributedInterceptedEnergy);
            Assert.True(credited > 1, "an egg crate must spread intercepted energy over several elements");

            // The independently re-measured device reproduces the optimiser's own numbers.
            Assert.Equal(result.DirectSolarIntercepted, performance.DirectSolarIntercepted, 9);
            Assert.Equal(result.UnwantedSolarIntercepted, performance.UnwantedSolarIntercepted, 9);
            Assert.Equal(result.WantedSolarBlocked, performance.WantedSolarBlocked, 9);
        }

        [Fact]
        public void Blade_Counts_Are_Capped_By_The_Analysis_Resolution()
        {
            // Found while validating Stage 9, and worth its own test because the failure looks like
            // a triumph. On a 1 m tall aperture sampled at 0.5 m there are two rows of analysis
            // cells. An eleven-blade louvre array at minimum depth can sit so that both rows fall
            // just under a blade — every sample is shaded against high summer sun and none against
            // low winter sun — and the optimiser reports 100 % of unwanted solar blocked with 100 %
            // of wanted solar retained. Nothing in the energy accounting is wrong; the geometry is
            // simply finer than the analysis that measures it, so the number is about where the
            // samples fell.
            //
            // The cap ties blade pitch to the grid: a device may only be credited for shading the
            // analysis can resolve.
            //
            // STAGE 10.2 raised the cap from one grid spacing to Create.MinimumElementPitchInGridSizes
            // of them. At pitch = grid there is exactly ONE sample per lit/shaded stripe period and,
            // the lattices being commensurate, it sits at the same place in every period — so the
            // measured shaded fraction can only come out 0 or 1 and is decided by phase. See
            // ResolutionConvergenceTests for the measurement, and Create.MinimumElementPitchInGridSizes
            // for why two is the threshold rather than a rounder-looking number.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            Analytical.SolarCalculator.Query.TryGetApertureLocalBounds(scenario.Target, out double minX, out double maxX, out double minY, out double maxY);
            double gridSize = scenario.BaseCache.CellSize;

            List<ShadingParameter> louvreParameters = Analytical.SolarCalculator.Create.ShadingParameters(
                new HorizontalLouvres(), double.NaN, scenario.Target, gridSize);
            List<ShadingParameter> finParameters = Analytical.SolarCalculator.Create.ShadingParameters(
                new VerticalFins(), double.NaN, scenario.Target, gridSize);

            ShadingParameter louvreCount = louvreParameters.Find(x => x.Name == "Count");
            ShadingParameter finCount = finParameters.Find(x => x.Name == "Count");

            double minimumPitch = Analytical.SolarCalculator.Create.MinimumElementPitchInGridSizes * gridSize;

            output.WriteLine($"aperture {maxX - minX:0.##} m across x {maxY - minY:0.##} m up, analysis grid {gridSize:0.##} m, minimum feature size {minimumPitch:0.##} m");
            output.WriteLine($"louvre count capped at {louvreCount.Maximum:0} (declared bound 24), pitch >= {(maxY - minY) / (louvreCount.Maximum - 1):0.###} m");
            output.WriteLine($"fin count capped at {finCount.Maximum:0} (declared bound 24), pitch >= {(maxX - minX) / (finCount.Maximum - 1):0.###} m");

            // Blades run up the aperture, fins across it, so the caps come from different spans.
            Assert.Equal(Math.Floor((maxY - minY) / minimumPitch + 1.0 + 1e-9), louvreCount.Maximum);
            Assert.Equal(Math.Floor((maxX - minX) / minimumPitch + 1.0 + 1e-9), finCount.Maximum);
            Assert.True(louvreCount.Maximum < 24, "the cap must actually bite against the declared bound");

            // The resulting pitch is never finer than the minimum feature size — which is what makes
            // the device Stage 9 proposes come back WITHOUT a resolution warning.
            Assert.True((maxY - minY) / (louvreCount.Maximum - 1) >= minimumPitch - 1e-9);
            Assert.True((maxX - minX) / (finCount.Maximum - 1) >= minimumPitch - 1e-9);

            // With no resolution supplied the declared bounds stand, so the cap is opt-in and
            // cannot silently narrow a caller's own parameter set.
            List<ShadingParameter> uncapped = Analytical.SolarCalculator.Create.ShadingParameters(new HorizontalLouvres());
            Assert.Equal(24.0, uncapped.Find(x => x.Name == "Count").Maximum);

            // And the optimiser honours it.
            OptimisedShadingResult result = Optimise.HorizontalLouvres(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);
            Report("capped louvre optimisation", result);
            Assert.True(result.GetParameter("Count") <= louvreCount.Maximum);
        }

        // ------------------------------------------------- lattice reachability ----

        [Fact]
        public void Narrowing_A_Count_To_The_Analysis_Resolution_Must_Not_Put_It_Out_Of_The_Search_s_Reach()
        {
            // STAGE 11, GATE 7. The two caps above interact badly with the refinement's step
            // schedule, and the result is an optimisation variable that is free in name only.
            //
            // The compass refinement starts each axis at a FRACTION OF ITS RANGE
            // (0.5 * Range / (coarseLevels - 1)) and then only ever halves. The cap in the test
            // above narrows a count to what the grid can resolve — on this fixture, [1, 3] — so its
            // range is 2 and its first step is 0.5, HALF the whole element the lattice is made of.
            // Snap rounds that back onto the incumbent, the probe is discarded as a no-op, and since
            // steps never grow the axis is unreachable at every scale, from every multi-start.
            //
            // Measured consequence before the fix: EggCrate returned LouvreCount 1 at every
            // objective weight while exhaustive enumeration wanted 3, for a 41.3 % optimality gap
            // that multi-start and compound pairwise moves both failed to touch.
            //
            // This test is deliberately about the GENERAL property — every free axis of every
            // family must be able to reach its nearest lattice neighbour — not about EggCrate's
            // number, which is Gate 7's job.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();
            double gridSize = scenario.BaseCache.CellSize;

            int degenerateAxes = 0;
            int continuousAxes = 0;

            output.WriteLine("family              parameter        bounds         increment   range step   effective   reaches");

            foreach (string typologyName in new string[] { "Overhang", "HorizontalLouvres", "VerticalFins", "EggCrate" })
            {
                List<ShadingParameter> parameters = Analytical.SolarCalculator.Create.ShadingParameters(
                    Analytical.SolarCalculator.Create.ShadingTypology(typologyName), double.NaN, scenario.Target, gridSize);

                foreach (ShadingParameter parameter in parameters)
                {
                    if (parameter.IsFixed)
                    {
                        continue;
                    }

                    // The refinement's own schedule, at the product default of coarseLevels = 3,
                    // quoted here so this test fails if that rule ever stops respecting the lattice.
                    double rangeDerived = 0.5 * parameter.Range / 2.0;
                    double effective = Math.Max(parameter.MinimumIncrement, rangeDerived);

                    // From ANY value on the lattice, one effective step must land somewhere else.
                    // Taken at the minimum, where only the upward probe is available.
                    double from = parameter.Snap(parameter.Minimum);
                    double reached = parameter.Snap(from + effective);

                    output.WriteLine(
                        $"{typologyName,-18}  {parameter.Name,-15}  [{parameter.Minimum,4:0.##}, {parameter.Maximum,4:0.##}]  " +
                        $"{parameter.MinimumIncrement,9:0.###}   {rangeDerived,10:0.####}   {effective,9:0.####}   {from:0.##} -> {reached:0.##}");

                    Assert.True(reached != from,
                        $"{typologyName}.{parameter.Name} cannot be moved off {from:0.##} by one refinement step, so it is a free variable the search can never probe");

                    if (rangeDerived < parameter.MinimumIncrement)
                    {
                        // The defect case: the range-derived step is finer than the lattice itself.
                        degenerateAxes++;
                        Assert.Equal(parameter.MinimumIncrement, effective, 12);
                    }
                    else
                    {
                        // Everything else keeps EXACTLY the schedule it had before the floor was
                        // introduced — the correction is inert for continuous parameters.
                        continuousAxes++;
                        Assert.Equal(rangeDerived, effective, 12);
                    }
                }
            }

            output.WriteLine("");
            output.WriteLine($"{degenerateAxes} axes whose range-derived step is finer than their own lattice, {continuousAxes} unaffected");

            Assert.True(degenerateAxes > 0,
                "this fixture no longer reproduces the narrowed-count case the test exists to guard; widen it or the guard is vacuous");
        }

        /// <summary>
        /// Evaluation cap for the converged-descent test below. Deliberately far above the product
        /// default: this test is not measuring cost, it is measuring an invariant that only a
        /// COMPLETED descent has, so it must be able to buy completion. Completion is asserted, not
        /// assumed — if a descent cannot finish inside this, the test fails loudly rather than
        /// quietly checking a truncated result.
        /// </summary>
        private const int ConvergedDescentBudget = 5000;

        [Fact]
        public void A_Converged_Compass_Descent_Is_Locally_Best_Along_Every_Free_Axis()
        {
            // The behavioural half of the test above, and the one that actually caught the tilt bug.
            // A descent that HAS RUN TO COMPLETION must be at least as good as its immediate
            // neighbours on the lattice: step one increment either way along each free axis, rebuild,
            // re-measure, and nothing may score strictly better. A completed descent with a better
            // neighbour is not a local optimum at all — it is a descent that stopped because it could
            // not express the move, which is exactly the defect MinimumIncrement exists to prevent.
            //
            // ON THE CONTRACT, corrected. This test used to run the product configuration and assert
            // the property of whatever came back. That attributed to the search a guarantee it has
            // never made. A bounded search that stops on EvaluationBudgetExhausted returns "the best
            // point found so far" BY DEFINITION — the incumbent of a descent still in progress. Its
            // neighbours are unexamined, and asserting otherwise tests the budget, not the algorithm.
            //
            // Conditioning on the reported termination instead would be too weak in the other
            // direction: the budget bounds the MULTI-START LOOP, so the flag can read
            // EvaluationBudgetExhausted while the descent that produced the winner finished cleanly,
            // and at the product budget every family reports it — the assertion would never run.
            //
            // So the invariant is tested where it is actually guaranteed: ONE compass descent, given
            // enough budget to finish, with completion asserted first. That is the level the property
            // belongs to. It is narrower in configuration and STRICTER in what it demands — the old
            // test could silently pass on a truncated result, this one cannot pass without a
            // completed one. It still fails against the pre-MinimumIncrement optimiser, which
            // converged and returned TiltDegrees 30 with 35 strictly better.
            //
            // It is NOT a claim of global optimality: see Gate 7 for the measured gap against
            // exhaustive enumeration, and the wording rule that keeps the result "best found within a
            // bounded deterministic search".
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            // The weight that stressed the search hardest in Gate 7.
            ShadingObjective objective = new ShadingObjective(2.0, 0.1);
            int checkedNeighbours = 0;

            foreach (string typologyName in new string[] { "Overhang", "HorizontalLouvres", "VerticalFins", "EggCrate" })
            {
                // A SINGLE start, so the run ends when that descent converges rather than when the
                // multi-start loop runs out of ranked coarse points. Product behaviour is untouched:
                // both are ordinary caller arguments, and the defaults stay 400 and budget-bounded.
                OptimisedShadingResult result = Optimise.ShadingTypology(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                    typologyName, objective, null, null, ConvergedDescentBudget, 3, 0, 1);

                Assert.NotNull(result);

                Assert.NotEqual(ShadingOptimisationTermination.EvaluationBudgetExhausted, result.Termination);

                output.WriteLine($"{typologyName,-18}  converged after {result.Evaluations} evaluations, {result.Iterations} iterations, termination {result.Termination}");

                if (result.RecommendsNoShading)
                {
                    // NO SHADE is a verdict about the null device, not a point on the lattice, so
                    // there is no neighbourhood to examine.
                    output.WriteLine($"{typologyName,-18}  recommends no shading");
                    continue;
                }

                foreach (ShadingParameter parameter in result.Bounds)
                {
                    if (parameter.IsFixed)
                    {
                        continue;
                    }

                    double value = result.GetParameter(parameter.Name);

                    foreach (int sign in new int[] { 1, -1 })
                    {
                        double neighbour = parameter.Snap(value + sign * parameter.MinimumIncrement);
                        if (neighbour == value)
                        {
                            continue; // at a bound: there is no neighbour that way
                        }

                        IShadingTypology device = Analytical.SolarCalculator.Create.ShadingTypology(typologyName);
                        foreach (string name in result.ParameterNames)
                        {
                            device.SetParameter(name, name == parameter.Name ? neighbour : result.GetParameter(name));
                        }

                        ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                            scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, device);

                        double score = performance == null ? double.NaN : objective.Score(performance);
                        checkedNeighbours++;

                        if (double.IsNaN(score))
                        {
                            continue;
                        }

                        output.WriteLine(
                            $"{typologyName,-18}  {parameter.Name,-15}  {value,6:0.##} -> {neighbour,6:0.##}   " +
                            $"{result.ObjectiveScore,9:0.###} vs {score,9:0.###}");

                        Assert.True(score <= result.ObjectiveScore + 1e-9,
                            $"{typologyName}: moving {parameter.Name} from {value:0.##} to {neighbour:0.##} scores {score:0.###} against the returned {result.ObjectiveScore:0.###}, " +
                            "so a CONVERGED descent returned a point it could still have improved on by one step");
                    }
                }
            }

            Assert.True(checkedNeighbours > 0);
            output.WriteLine("");
            output.WriteLine($"{checkedNeighbours} lattice neighbours examined; none beats the returned point");
        }

        // -------------------------------------------------------------- eligibility ----

        [Fact]
        public void Typology_Eligibility_Follows_Where_The_Unwanted_Solar_Comes_From()
        {
            // Eligibility is a CAPABILITY gate, not a ranking. It answers "can this family act on
            // where this aperture's unwanted solar comes from at all", and it is deliberately
            // permissive: a south facade genuinely takes a large share of its summer beam
            // obliquely, so fins are not disqualified there even though an overhang usually wins.
            // The optimiser still has to prove any device is worth building.
            OptimisationFixture.Scenario south = OptimisationFixture.SouthSeasonal();
            OptimisationFixture.Scenario east = OptimisationFixture.EastSeasonal();
            OptimisationFixture.Scenario north = OptimisationFixture.NorthSeasonal();

            List<string> southEligible = Analytical.SolarCalculator.Query.EligibleShadingTypologies(
                south.Target, south.BaseCache, south.Desirability, out double southHigh, out double southOblique);
            List<string> eastEligible = Analytical.SolarCalculator.Query.EligibleShadingTypologies(
                east.Target, east.BaseCache, east.Desirability, out double eastHigh, out double eastOblique);
            List<string> northEligible = Analytical.SolarCalculator.Query.EligibleShadingTypologies(
                north.Target, north.BaseCache, north.Desirability, out double northHigh, out double northOblique);

            output.WriteLine($"south (azimuth {south.Target.Azimuth:0.#} deg): high-sun {southHigh * 100:0.#} %, oblique {southOblique * 100:0.#} % -> {string.Join(", ", southEligible)}");
            output.WriteLine($"east  (azimuth {east.Target.Azimuth:0.#} deg): high-sun {eastHigh * 100:0.#} %, oblique {eastOblique * 100:0.#} % -> {string.Join(", ", eastEligible)}");
            output.WriteLine($"north (azimuth {north.Target.Azimuth:0.#} deg): high-sun {northHigh * 100:0.#} %, oblique {northOblique * 100:0.#} % -> {string.Join(", ", northEligible)}");

            // A south facade's summer problem is overwhelmingly high sun.
            Assert.Contains("Overhang", southEligible);
            Assert.Contains("HorizontalLouvres", southEligible);
            Assert.True(southHigh > eastHigh, "a south facade must take more of its unwanted solar from high sun than an east one");

            // An east facade takes a real oblique share, so fins can act on it.
            Assert.Contains("VerticalFins", eastEligible);

            // The north facade does NOT get excluded, and the reason is worth stating because it
            // was the expected result before it was measured. Its summer sun arrives at the ends of
            // the day, far round the corner, and for such a direction the outward component goes to
            // zero — so the profile angle is LARGE even though the sun is low. Profile angle is
            // exactly the quantity an overhang cuts off, so the gate is right: a sufficiently wide
            // overhang would intercept that beam. What makes shading a north facade pointless is
            // not the angle, it is that there is almost no energy there at all.
            //
            // The angular gate cannot see that, and it is not asked to. The gate answers "could
            // this family act on this beam"; whether the beam is worth acting on is the objective's
            // question, and the objective answers it below.
            Assert.True(northHigh < southHigh, "a north facade must take less high sun than a south one");

            output.WriteLine($"admitted unwanted energy: south {south.Desirability.TotalUnwantedEnergy:0.###} kWh/m2, east {east.Desirability.TotalUnwantedEnergy:0.###} kWh/m2, north {north.Desirability.TotalUnwantedEnergy:0.###} kWh/m2");
            Assert.True(north.Desirability.TotalUnwantedEnergy < south.Desirability.TotalUnwantedEnergy,
                "a north facade must receive less unwanted solar than a south one");

            // And that is what decides it: the objective, not the gate. Whichever way this goes it
            // goes on measured energy, so the outcome is reported rather than assumed.
            OptimisedShadingResult northResult = Optimise.Overhang(
                north.Target, north.BaseCache, north.Desirability, north.Context);
            Report("north facade overhang", northResult);
            output.WriteLine(northResult.RecommendsNoShading
                ? "north facade: no candidate beats building nothing"
                : $"north facade: an overhang is still worth {northResult.ObjectiveScore:0.##} kWh — the low summer sun round the corner is real energy");

            // An aperture with no unwanted solar at all makes NOTHING eligible — reported as an
            // empty list rather than a free choice.
            OptimisationFixture.Scenario allWanted = OptimisationFixture.SouthAllWanted();
            List<string> none = Analytical.SolarCalculator.Query.EligibleShadingTypologies(
                allWanted.Target, allWanted.BaseCache, allWanted.Desirability, out double _, out double _);
            Assert.Empty(none);
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Multi_Typology_Comparison_Ranks_Eligible_Families_Deterministically()
        {
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            List<OptimisedShadingResult> results = Optimise.ShadingDevice(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);

            Assert.NotNull(results);
            Assert.NotEmpty(results);

            output.WriteLine("rank | typology            |    score |  benefit |     harm |     cost | material | evaluations");
            for (int i = 0; i < results.Count; i++)
            {
                OptimisedShadingResult result = results[i];
                output.WriteLine($"{i + 1,4} | {result.TypologyName,-19} | {result.ObjectiveScore,8:0.##} | {result.Benefit,8:0.##} | {result.Harm,8:0.##} | {result.Cost,8:0.##} | {result.MaterialFraction,8:0.###} | {result.Evaluations,11}");
            }

            // Ranked best first.
            for (int i = 1; i < results.Count; i++)
            {
                Assert.True(results[i - 1].ObjectiveScore >= results[i].ObjectiveScore - 1e-9,
                    "results must be ordered best first");
            }

            // The winner beats building nothing, and the ranking is reproducible.
            Assert.True(results[0].ObjectiveScore > 0);

            List<OptimisedShadingResult> again = Optimise.ShadingDevice(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context);

            Assert.Equal(results.Count, again.Count);
            for (int i = 0; i < results.Count; i++)
            {
                Assert.Equal(results[i].TypologyName, again[i].TypologyName);
                Assert.Equal(results[i].ObjectiveScore, again[i].ObjectiveScore);
            }
        }
    }
}
