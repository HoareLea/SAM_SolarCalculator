// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 10.2 Gate B: what the two penalties are allowed to be, and what happens at the edges.
    ///
    /// They are DIMENSIONLESS WEIGHTS, not physical quantities, so there is no measurement that
    /// hands down an upper bound and inventing one would be a UI convenience pretending to be
    /// physics. What CAN be pinned down is the lower bound — which is a matter of the objective's
    /// algebra rather than of taste — and the behaviour far out, which is a matter of measurement.
    /// </summary>
    public class PenaltyDomainTests
    {
        private readonly ITestOutputHelper output;

        public PenaltyDomainTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // ------------------------------------------------------------------- the domain ----

        [Fact]
        public void The_Valid_Domain_Is_Finite_And_Non_Negative_With_No_Upper_Bound()
        {
            foreach (double value in new double[] { 0.0, 0.1, 0.5, 1.0, 2.0, 5.0, 10.0 })
            {
                Assert.Equal(PenaltyValidity.Recommended, ShadingObjective.Validity(value));
            }

            // Above the extreme threshold: still VALID, still meaningful, just far from normal use.
            // Deliberately not refused — there is no physical ceiling to appeal to, and a hard
            // maximum would be an arbitrary one.
            foreach (double value in new double[] { 10.001, 100.0, 1e6 })
            {
                Assert.Equal(PenaltyValidity.Extreme, ShadingObjective.Validity(value));
            }

            // Negative inverts the meaning of the term it weights; NaN and infinity make every
            // candidate unscoreable, which would be reported as "build nothing" — a confident
            // answer the run never earned.
            foreach (double value in new double[] { -1e-9, -0.5, -1.0, -100.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Equal(PenaltyValidity.Invalid, ShadingObjective.Validity(value));
            }

            Assert.Equal(0.0, ShadingObjective.MinimumPenalty);
            Assert.Equal(10.0, ShadingObjective.ExtremePenalty);
        }

        [Fact]
        public void The_Defaults_Are_The_Documented_Ones()
        {
            ShadingObjective objective = new ShadingObjective();

            // 1.0 is an EVEN TRADE — the only value that assumes nothing about the brief.
            Assert.Equal(1.0, objective.WantedSolarPenalty);

            // 0.1 is a mild tie-break toward the leaner design, small enough not to override energy.
            Assert.Equal(0.1, objective.MaterialPenalty);

            Assert.Equal(PenaltyValidity.Recommended, objective.WantedSolarPenaltyValidity);
            Assert.Equal(PenaltyValidity.Recommended, objective.MaterialPenaltyValidity);
        }

        // ------------------------------------------------ why negative is refused, not clamped ----

        [Fact]
        public void A_Negative_Wanted_Penalty_Would_Pay_The_Search_To_Destroy_Wanted_Solar()
        {
            // The argument, demonstrated rather than asserted. Two devices: one that destroys wanted
            // solar and one that does not, identical otherwise. Under any valid penalty the harmless
            // one wins. Under a negative one the harmful one does — the objective has stopped
            // expressing any brief a person would write, which is why the value is refused at the
            // component rather than quietly read as zero.
            ShadingPerformance harmless = Performance(unwantedIntercepted: 100, wantedBlocked: 0);
            ShadingPerformance harmful = Performance(unwantedIntercepted: 100, wantedBlocked: 50);

            foreach (double penalty in new double[] { 0.0, 0.5, 1.0, 2.0 })
            {
                ShadingObjective objective = new ShadingObjective(penalty, 0.0);
                Assert.True(objective.Score(harmless) >= objective.Score(harmful));
            }

            ShadingObjective inverted = new ShadingObjective(-1.0, 0.0);
            Assert.True(inverted.Score(harmful) > inverted.Score(harmless),
                "this is the failure mode the domain rule exists to prevent");

            output.WriteLine($"lambda -1: destroying 50 kWh of wanted solar scores {inverted.Score(harmful):0.#} against {inverted.Score(harmless):0.#} for leaving it alone");
        }

        [Fact]
        public void A_Negative_Material_Penalty_Would_Pay_The_Search_To_Buy_Material()
        {
            ShadingPerformance lean = Performance(unwantedIntercepted: 100, wantedBlocked: 0, materialFraction: 0.1);
            ShadingPerformance heavy = Performance(unwantedIntercepted: 100, wantedBlocked: 0, materialFraction: 2.0);

            foreach (double penalty in new double[] { 0.0, 0.1, 1.0 })
            {
                ShadingObjective objective = new ShadingObjective(1.0, penalty);
                Assert.True(objective.Score(lean) >= objective.Score(heavy));
            }

            ShadingObjective inverted = new ShadingObjective(1.0, -0.5);
            Assert.True(inverted.Score(heavy) > inverted.Score(lean));

            output.WriteLine($"mu -0.5: the heavier device scores {inverted.Score(heavy):0.#} against {inverted.Score(lean):0.#} for the leaner one");
        }

        // ---------------------------------------------------------- what raising them does ----

        [Fact]
        public void Raising_The_Material_Penalty_Shrinks_The_Answer_And_Ends_At_No_Shading()
        {
            // The manual sweep, reproduced. The point is NOT that every metric moves monotonically —
            // the search is over discrete geometries and it does not — but that the sequence ends
            // where the manual test found it: at the null device, through the mechanism that already
            // exists. No minimum-kWh threshold is needed or wanted.
            OptimisationFixture.Scenario scenario = OptimisationFixture.NorthSeasonal();

            bool sawShading = false;
            bool sawNoShading = false;
            double previousScore = double.PositiveInfinity;

            foreach (double materialPenalty in new double[] { 0.1, 0.5, 1.0, 2.0, 5.0, 10.0, 100.0 })
            {
                OptimisedShadingResult result = Optimise.VerticalFins(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                    new ShadingObjective(1.0, materialPenalty), null, 40);

                Assert.NotNull(result);

                // Whatever the weighting, the run MEASURED something. "No shading" must never be
                // reachable by failing to evaluate.
                Assert.NotEqual(ShadingOptimisationTermination.EvaluationFailed, result.Termination);
                Assert.False(double.IsNaN(result.ObjectiveScore));

                if (result.RecommendsNoShading)
                {
                    sawNoShading = true;

                    // The null device is a SUCCESSFUL answer: it verifies like any other, and the
                    // losing candidate is still there to be inspected.
                    Assert.Equal(ShadingOptimisationTermination.NoBeneficialCandidate, result.Termination);
                    Assert.True(result.ObjectiveScore <= 0);
                }
                else
                {
                    sawShading = true;
                    Assert.True(result.ObjectiveScore > 0);
                }

                // Charging more for the same material can never raise the best achievable score.
                Assert.True(result.ObjectiveScore <= previousScore + 1e-9,
                    $"raising the material penalty to {materialPenalty} raised the best score");
                previousScore = result.ObjectiveScore;

                output.WriteLine($"mu {materialPenalty,6}: {(result.RecommendsNoShading ? "NO SHADE" : "device  "),-9} score {result.ObjectiveScore,9:0.##} kWh, " +
                    $"material {result.MaterialFraction:0.###}, {100 * result.UnwantedSolarBlocked:0.#}% unwanted blocked");
            }

            Assert.True(sawShading, "the sweep must start somewhere a device is worth building");
            Assert.True(sawNoShading, "and must reach the null device without needing a kWh threshold to get there");
        }

        [Fact]
        public void An_Extreme_Penalty_Still_Verifies_As_A_Real_Answer()
        {
            // materialPenalty 100 in the manual test returned successful = true with empty geometry.
            // That is the null device doing its job, and it must measure honestly rather than being
            // special-cased into 0 % / 100 %.
            OptimisationFixture.Scenario scenario = OptimisationFixture.NorthSeasonal();

            OptimisedShadingResult result = Optimise.VerticalFins(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                new ShadingObjective(1.0, 100.0), null, 40);

            Assert.True(result.RecommendsNoShading);

            ShadingPerformance verified = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, new NoShading());

            Assert.NotNull(verified);
            Assert.Equal("NoShading", verified.TypologyName);
            Assert.Equal(0.0, verified.DirectSolarIntercepted, 9);
            Assert.Equal(0.0, verified.UnwantedSolarIntercepted, 9);
            Assert.Equal(0.0, verified.WantedSolarBlocked, 9);

            // The baseline is still measured, so 0 % blocked is a measurement and not a placeholder.
            Assert.True(verified.AdmittedDirectEnergy > 0);
            Assert.Equal(0.0, verified.UnwantedSolarBlocked, 9);

            output.WriteLine(Analytical.SolarCalculator.Query.VerificationSummary(verified, scenario.Target.Azimuth));
        }

        [Fact]
        public void The_Wanted_Solar_Penalty_Is_What_Changes_The_Design_Not_The_Measurement()
        {
            // Gate C of the brief: verify that lambda really caused the selection, and that it did
            // not touch a measured energy. The same device measured under two objectives must report
            // identical physics and different scores.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();
            IShadingTypology device = new HorizontalLouvres(0.3, 3, 0.0);

            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, device);

            double half = new ShadingObjective(0.5, 0.1).Score(performance);
            double one = new ShadingObjective(1.0, 0.1).Score(performance);
            double two = new ShadingObjective(2.0, 0.1).Score(performance);

            // The physics is a property of the geometry and the weather, not of the brief.
            Assert.True(performance.WantedSolarBlocked > 0, "this scenario must actually cost some wanted solar, or it proves nothing");

            // Charging more per kWh of wanted solar lost lowers the score by exactly that much:
            // Score(lambda) = Benefit - lambda x Harm - mu x Cost, so the difference between two
            // lambdas is (delta lambda) x Harm and nothing else.
            Assert.Equal(one - 1.0 * performance.WantedSolarBlocked, two, 9);
            Assert.Equal(one + 0.5 * performance.WantedSolarBlocked, half, 9);

            output.WriteLine($"one device, {performance.UnwantedSolarIntercepted:0.#} kWh unwanted intercepted and {performance.WantedSolarBlocked:0.#} kWh wanted blocked, " +
                $"scores {half:0.#} / {one:0.#} / {two:0.#} kWh at lambda 0.5 / 1 / 2");
        }

        [Fact]
        public void A_Stronger_Wanted_Penalty_Buys_A_Design_That_Keeps_More_Wanted_Solar()
        {
            // The intended behaviour the manual south test demonstrated. Stated as the ONE thing
            // that must hold — lambda = 2 protects wanted solar better than lambda = 0.5 — and not
            // as monotonicity of every metric, which a discrete search over device families does not
            // and should not provide.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            Dictionary<double, OptimisedShadingResult> results = new Dictionary<double, OptimisedShadingResult>();
            foreach (double lambda in new double[] { 0.5, 1.0, 2.0 })
            {
                OptimisedShadingResult result = Optimise.HorizontalLouvres(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                    new ShadingObjective(lambda, 0.1), null, 60);

                Assert.NotNull(result);
                results[lambda] = result;

                output.WriteLine($"lambda {lambda}: {Analytical.SolarCalculator.Query.DesignSummary(result)}");
            }

            Assert.True(results[2.0].WantedSolarBlocked <= results[0.5].WantedSolarBlocked + 1e-9,
                "raising the wanted-solar penalty must not end up destroying MORE wanted solar");

            // Lambda changed the DESIGN, not just the number attached to it.
            Assert.NotEqual(results[0.5].GetParameter("Depth"), results[2.0].GetParameter("Depth"));

            // WHAT IS DELIBERATELY *NOT* ASSERTED HERE, and why. It is tempting to require that each
            // lambda's winner beats the other lambdas' winners under its own objective. It does not
            // always, and that is a property of the SEARCH rather than of the objective: Stage 9 runs
            // a deterministic derivative-free pattern search on a fixed evaluation budget, so it
            // finds a good point and not a provably global one, and changing lambda changes the
            // landscape it walks. Measured on this fixture at a 60-evaluation budget, the lambda=0.5
            // winner scores about 3 % higher under lambda=1 than the lambda=1 search's own winner.
            // Both scores are correct; one search simply landed better.
            //
            // The claim "lambda causes the selection" is therefore proved where it is actually true —
            // over a FIXED candidate set, in Lambda_Reorders_A_Fixed_Set_Of_Designs below — and the
            // search's own quality is a separate matter, reported rather than asserted away here.
        }

        [Fact]
        public void Lambda_Reorders_A_Fixed_Set_Of_Designs()
        {
            // The objective's half of the claim, isolated from the search entirely. Two designs: a
            // deep one that blocks a lot and costs wanted solar, and a shallow one that blocks less
            // and costs none. Which is better is EXACTLY the question lambda answers, and the
            // crossover is where the two scores meet.
            ShadingPerformance deep = Performance(unwantedIntercepted: 300, wantedBlocked: 100, materialFraction: 0.5);
            ShadingPerformance shallow = Performance(unwantedIntercepted: 180, wantedBlocked: 5, materialFraction: 0.5);

            // Cheap winter solar: the deep device wins outright.
            ShadingObjective permissive = new ShadingObjective(0.5, 0.1);
            Assert.True(permissive.Score(deep) > permissive.Score(shallow));

            // Dear winter solar: the shallow device wins. Same two designs, same measured physics,
            // opposite recommendation — which is what a weighting is FOR.
            ShadingObjective protective = new ShadingObjective(2.0, 0.1);
            Assert.True(protective.Score(shallow) > protective.Score(deep));

            // The crossover is at (300 - 180) / (100 - 5) = 1.263..., and it is a property of the
            // arithmetic, so it can be stated exactly rather than bracketed.
            double crossover = (300.0 - 180.0) / (100.0 - 5.0);
            Assert.Equal(new ShadingObjective(crossover, 0.1).Score(deep), new ShadingObjective(crossover, 0.1).Score(shallow), 9);

            output.WriteLine($"deep vs shallow crossover at lambda {crossover:0.####}");
        }

        [Fact]
        public void The_Same_Objective_Always_Returns_The_Same_Design()
        {
            // Determinism, which is what makes any of the comparisons above meaningful: two searches
            // with identical inputs must agree exactly, parameter for parameter, with no random seed
            // anywhere in the chain.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            OptimisedShadingResult first = Optimise.HorizontalLouvres(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                new ShadingObjective(1.0, 0.1), null, 60);

            OptimisedShadingResult second = Optimise.HorizontalLouvres(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                new ShadingObjective(1.0, 0.1), null, 60);

            Assert.Equal(first.TypologyName, second.TypologyName);
            Assert.Equal(first.Evaluations, second.Evaluations);
            Assert.Equal(first.ObjectiveScore, second.ObjectiveScore, 12);
            Assert.Equal(first.AttributionTableHash, second.AttributionTableHash);

            foreach (string name in first.ParameterNames)
            {
                Assert.Equal(first.GetParameter(name), second.GetParameter(name), 12);
            }

            output.WriteLine($"reproduced exactly: {Analytical.SolarCalculator.Query.DesignSummary(first)}");
        }

        [Fact]
        public void Negative_Louvre_Tilt_Is_A_Real_Buildable_Direction_And_Is_Bounded()
        {
            // The manual south test returned TiltDegrees -15 at lambda 2 and the brief asks what a
            // negative tilt means. It is not an error state: the parameter is symmetric about zero
            // and both signs build.
            //
            // SIGN CONVENTION, for horizontal louvres: POSITIVE tilt drops the OUTER edge below the
            // inner one — the ordinary brise-soleil, angled down and out to cut high sun. NEGATIVE
            // tilt raises the outer edge — the light-shelf direction, which lets low winter sun in
            // under the blade while still cutting high summer sun. Choosing that at lambda 2 is the
            // objective doing exactly what it was told: protect wanted solar harder.
            ApertureSolarTarget target = OptimisationFixture.SouthSeasonal().Target;

            Assert.True(new HorizontalLouvres().TryGetBounds("TiltDegrees", out double minimum, out double maximum));
            Assert.Equal(-60.0, minimum);
            Assert.Equal(60.0, maximum);

            // Both signs produce real geometry, and the two are genuinely different devices.
            List<ShadingElement> up = new HorizontalLouvres(0.3, 3, -15.0).ShadingElements(target);
            List<ShadingElement> down = new HorizontalLouvres(0.3, 3, 15.0).ShadingElements(target);

            Assert.Equal(3, up.Count);
            Assert.Equal(3, down.Count);

            // The outer edge is above the fixing line for negative tilt and below it for positive.
            Assert.True(OuterEdgeHeight(target, up) > OuterEdgeHeight(target, down));

            // Out-of-range values clamp to the declared bound rather than building nonsense.
            HorizontalLouvres clamped = new HorizontalLouvres(0.3, 3, -400.0);
            Assert.Equal(-60.0, clamped.GetParameter("TiltDegrees"));

            output.WriteLine($"tilt -15° outer edge at y {OuterEdgeHeight(target, up):0.###} m; tilt +15° at y {OuterEdgeHeight(target, down):0.###} m");
        }

        // ------------------------------------------------------------------- helpers ----

        /// <summary>The mean up-slope height of the blades' outer edges, in the aperture frame.</summary>
        private static double OuterEdgeHeight(ApertureSolarTarget target, List<ShadingElement> elements)
        {
            double total = 0;
            int count = 0;
            foreach (ShadingElement element in elements)
            {
                foreach (Geometry.Spatial.Point3D point3D in ((Geometry.Spatial.ISegmentable3D)element.Face3D.GetExternalEdge3D()).GetPoints())
                {
                    if (Analytical.SolarCalculator.Query.TryGetApertureLocal(target, point3D, out double _, out double y, out double z) && z > 1e-6)
                    {
                        total += y;
                        count++;
                    }
                }
            }

            return count == 0 ? double.NaN : total / count;
        }

        private static ShadingPerformance Measure(OptimisationFixture.Scenario scenario, IShadingTypology device)
        {
            return Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, device);
        }

        /// <summary>A synthetic performance, so the objective's algebra can be tested without a ray cast.</summary>
        private static ShadingPerformance Performance(double unwantedIntercepted, double wantedBlocked, double materialFraction = 0.5)
        {
            return new ShadingPerformance(
                Guid.Empty, "Synthetic",
                admittedDirectEnergy: 1000.0,
                admittedUnwantedEnergy: 400.0,
                admittedWantedEnergy: 300.0,
                directSolarIntercepted: unwantedIntercepted + wantedBlocked,
                unwantedSolarIntercepted: unwantedIntercepted,
                wantedSolarBlocked: wantedBlocked,
                unattributedInterceptedEnergy: 0.0,
                materialFraction: materialFraction,
                energyPerElement: null,
                namePerElement: null);
        }
    }
}
