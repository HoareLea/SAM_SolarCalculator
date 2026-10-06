// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 10.2 Gate C: the candidate search does less work, and PROVES it did not change the
    /// answer while doing so.
    ///
    /// THE SAVING. Stage 8 consults attribution strictly inside
    /// `if (baseVisibilityCache.IsLit(b, cellIndexOffset + c))` — a sample that existing context
    /// already shades is not the candidate's to claim, so its first hit is never asked for. Tracing
    /// it produces a value nothing reads. The candidate path now traces only the readable samples.
    ///
    /// WHY THIS IS NOT A SPEED/ACCURACY TRADE. The skipped entries are not approximated, sampled or
    /// interpolated — they are the entries the accounting provably never touches. Physical
    /// verification is untouched: every candidate is still built as real geometry and ray-traced.
    /// These tests hold the two routes against each other to the last decimal place rather than
    /// leaving that argument to stand on its own.
    /// </summary>
    public class AttributionPruningTests
    {
        private readonly ITestOutputHelper output;

        public AttributionPruningTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static SolarAttributionCache Attribution(SolarVisibilityCache baseCache, List<ShadingElement> elements, List<AnalysisCell> cells, int cellIndexOffset, bool litOnly)
        {
            List<LinkedFace3D> occluders = new List<LinkedFace3D>();
            foreach (ShadingElement element in elements)
            {
                if (element?.LinkedFace3D != null)
                {
                    occluders.Add(element.LinkedFace3D);
                }
            }

            return Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, occluders, cells, cellIndexOffset,
                Core.Tolerance.MacroDistance, Core.Tolerance.MacroDistance, Core.Tolerance.Angle, Core.Tolerance.Distance,
                baseLitSamplesOnly: litOnly);
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void The_Pruned_And_Complete_Attributions_Give_Identical_Energy()
        {
            // The equivalence proof, on every scenario and every family that has one.
            foreach (Func<OptimisationFixture.Scenario> factory in new Func<OptimisationFixture.Scenario>[]
            {
                OptimisationFixture.SouthSeasonal,
                OptimisationFixture.NorthSeasonal,
                OptimisationFixture.EastSeasonal,
                OptimisationFixture.SouthBlocked,   // the one with real context, where the saving is largest
                OptimisationFixture.SouthAllWanted,
                OptimisationFixture.SouthAllUnwanted,
            })
            {
                OptimisationFixture.Scenario scenario = factory();

                foreach (IShadingTypology device in new IShadingTypology[]
                {
                    new Overhang(0.6, 0.1, 0.2),
                    new HorizontalLouvres(0.3, 3, 15.0),
                    new VerticalFins(0.25, 4, -20.0),
                    new EggCrate(0.3, 2, 3),
                    new NoShading(),
                })
                {
                    List<ShadingElement> elements = device.ShadingElements(scenario.Target);

                    SolarAttributionCache complete = Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, false);
                    SolarAttributionCache pruned = Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, true);

                    Assert.NotNull(complete);
                    Assert.NotNull(pruned);

                    // The two caches are NOT identical — that is the point, the pruned one skipped
                    // work — but the identity they carry, and therefore what they may be paired
                    // with, is the same.
                    Assert.Equal(complete.AttributionTableHash, pruned.AttributionTableHash);
                    Assert.Equal(complete.CellCount, pruned.CellCount);
                    Assert.Equal(complete.BinCount, pruned.BinCount);

                    ShadingPerformance fromComplete = Analytical.SolarCalculator.Create.ShadingPerformance(
                        scenario.Target, scenario.BaseCache, complete, scenario.Desirability, elements,
                        device.Name, device.MaterialFraction(scenario.Target));

                    ShadingPerformance fromPruned = Analytical.SolarCalculator.Create.ShadingPerformance(
                        scenario.Target, scenario.BaseCache, pruned, scenario.Desirability, elements,
                        device.Name, device.MaterialFraction(scenario.Target));

                    Assert.NotNull(fromComplete);
                    Assert.NotNull(fromPruned);

                    // Every reported energy, to the last decimal place a double carries.
                    Assert.Equal(fromComplete.AdmittedDirectEnergy, fromPruned.AdmittedDirectEnergy, 12);
                    Assert.Equal(fromComplete.AdmittedUnwantedEnergy, fromPruned.AdmittedUnwantedEnergy, 12);
                    Assert.Equal(fromComplete.AdmittedWantedEnergy, fromPruned.AdmittedWantedEnergy, 12);
                    Assert.Equal(fromComplete.DirectSolarIntercepted, fromPruned.DirectSolarIntercepted, 12);
                    Assert.Equal(fromComplete.UnwantedSolarIntercepted, fromPruned.UnwantedSolarIntercepted, 12);
                    Assert.Equal(fromComplete.WantedSolarBlocked, fromPruned.WantedSolarBlocked, 12);
                    Assert.Equal(fromComplete.NeutralSolarIntercepted, fromPruned.NeutralSolarIntercepted, 12);
                    Assert.Equal(fromComplete.UnattributedInterceptedEnergy, fromPruned.UnattributedInterceptedEnergy, 12);

                    // Including the PER-ELEMENT breakdown: the saving must not blur which blade
                    // stopped what, which is the finest-grained thing the accounting reports.
                    Dictionary<Guid, double> completeElements = fromComplete.EnergyPerElement;
                    Dictionary<Guid, double> prunedElements = fromPruned.EnergyPerElement;
                    Assert.Equal(completeElements.Count, prunedElements.Count);
                    foreach (KeyValuePair<Guid, double> pair in completeElements)
                    {
                        Assert.Equal(pair.Value, prunedElements[pair.Key], 12);
                    }
                }
            }
        }

        [Fact]
        public void The_Pruned_Attribution_Agrees_Everywhere_The_Baseline_Says_Lit()
        {
            // The mechanism, checked entry by entry rather than only through the totals. Wherever the
            // base cache reports LIT the two caches must give the same first hit; everywhere else the
            // pruned one says explicitly that no ray was cast, instead of quietly reporting "nothing
            // hit it" and looking like a measurement.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthBlocked();
            List<ShadingElement> elements = new HorizontalLouvres(0.3, 3, 0.0).ShadingElements(scenario.Target);

            SolarAttributionCache complete = Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, false);
            SolarAttributionCache pruned = Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, true);

            int lit = 0, skipped = 0;
            for (int b = 0; b < complete.BinCount; b++)
            {
                for (int c = 0; c < complete.CellCount; c++)
                {
                    if (scenario.BaseCache.IsLit(b, c))
                    {
                        Assert.Equal(complete.FirstHitIndex(b, c), pruned.FirstHitIndex(b, c));
                        Assert.Equal(complete.FirstHitGuid(b, c), pruned.FirstHitGuid(b, c));
                        lit++;
                    }
                    else
                    {
                        Assert.True(pruned.FirstHitIndex(b, c) < 0, "an unread sample must never carry an attribution");
                        skipped++;
                    }
                }
            }

            output.WriteLine($"{lit} readable samples agree exactly; {skipped} ({100.0 * skipped / (lit + skipped):0.#} %) were never traced");
            Assert.True(skipped > 0, "this scenario must actually have context-shaded samples, or it proves nothing");
        }

        [Fact]
        public void The_Whole_Optimisation_Returns_The_Same_Design_On_A_Real_Model()
        {
            // End to end on the controlled model, through the entry point Grasshopper uses: the
            // recommended device the optimiser returns must reproduce exactly when its performance is
            // re-measured by the verifier, which builds its attribution the same pruned way.
            //
            // This is the "fine-grid result equivalence" check: it runs at 0.1 m, the grid the manual
            // testing used and the one where the pruning does the most work.
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            Assert.True(File.Exists(path));

            AnalyticalModel model = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            WeatherData weatherData = model.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;

            Guid apertureGuid = model.ApertureSolarTargets(null, 0.5)
                .First(x => Math.Abs(x.Azimuth) < 1e-6).ApertureGuid;

            ApertureShadingSetup setup = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                model, apertureGuid, year, null, null, null, null, new List<Guid> { apertureGuid }, 0.1, 2.0);

            Assert.NotNull(setup);

            List<OptimisedShadingResult> results = Optimise.ShadingDevice(
                setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability, setup.Context.ContextOccluders,
                new ShadingObjective(1.0, 0.1), null, new string[] { "VerticalFins" }, 60, setup.CellIndexOffset);

            Assert.NotEmpty(results);
            OptimisedShadingResult best = results[0];
            Assert.False(best.RecommendsNoShading);

            IShadingTypology device = best.Typology();

            // Re-measure through the verifier, which is a different code path to the optimiser's
            // inner loop and must land on the same numbers.
            ShadingPerformance verified = Analytical.SolarCalculator.Create.ShadingPerformance(
                setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability,
                setup.Context.ContextOccluders, device, setup.CellIndexOffset);

            Assert.Equal(best.DirectSolarIntercepted, verified.DirectSolarIntercepted, 9);
            Assert.Equal(best.UnwantedSolarIntercepted, verified.UnwantedSolarIntercepted, 9);
            Assert.Equal(best.WantedSolarBlocked, verified.WantedSolarBlocked, 9);
            Assert.Equal(best.AdmittedDirectEnergy, verified.AdmittedDirectEnergy, 9);

            // And against the COMPLETE attribution, so the pruning is proved on the real model too.
            List<ShadingElement> elements = device.ShadingElements(setup.Target);
            SolarAttributionCache complete = Attribution(
                setup.Context.SolarVisibilityCache, elements, setup.Target.AnalysisCells, setup.CellIndexOffset, false);

            ShadingPerformance fromComplete = Analytical.SolarCalculator.Create.ShadingPerformance(
                setup.Target, setup.Context.SolarVisibilityCache, complete, setup.Desirability, elements,
                device.Name, device.MaterialFraction(setup.Target), setup.CellIndexOffset);

            Assert.Equal(fromComplete.DirectSolarIntercepted, verified.DirectSolarIntercepted, 12);
            Assert.Equal(fromComplete.UnwantedSolarIntercepted, verified.UnwantedSolarIntercepted, 12);
            Assert.Equal(fromComplete.WantedSolarBlocked, verified.WantedSolarBlocked, 12);

            output.WriteLine($"grid 0.1 m: {Analytical.SolarCalculator.Query.DesignSummary(best, setup.Target.Azimuth)}");
            output.WriteLine("pruned and complete attribution agree to 12 decimal places");
        }

        [Fact]
        public void Pruning_Actually_Saves_Work_Where_There_Is_Work_To_Save()
        {
            // A saving that is only argued for is not a saving. On a north window most sun groups
            // carry no beam onto the glass at all, so most of the table is unreadable and the pruned
            // build should be markedly quicker for exactly the same answer.
            //
            // Timed rather than counted because the point is wall clock, but asserted loosely: this
            // runs on shared CI hardware and a tight bound would be a flaky test rather than a
            // stronger one.
            OptimisationFixture.Scenario scenario = OptimisationFixture.NorthSeasonal();
            List<ShadingElement> elements = new VerticalFins(0.25, 4, 0.0).ShadingElements(scenario.Target);

            // Warm both paths so JIT is not being measured.
            Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, false);
            Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, true);

            Stopwatch completeWatch = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++) { Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, false); }
            completeWatch.Stop();

            Stopwatch prunedWatch = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++) { Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, true); }
            prunedWatch.Stop();

            // How much of the table is readable at all — the ceiling on what pruning could ever save.
            int readable = 0, total = 0;
            for (int b = 0; b < scenario.BaseCache.BinCount; b++)
            {
                for (int c = 0; c < scenario.Target.CellCount; c++)
                {
                    total++;
                    if (scenario.BaseCache.IsLit(b, c)) { readable++; }
                }
            }

            output.WriteLine($"north window: {readable} of {total} samples readable ({100.0 * readable / total:0.#} %)");
            output.WriteLine($"complete {completeWatch.Elapsed.TotalMilliseconds / 3.0:0.0} ms, pruned {prunedWatch.Elapsed.TotalMilliseconds / 3.0:0.0} ms per build");

            Assert.True(readable < total / 2, "a north window's table should be mostly unreadable, or this scenario is the wrong one for the test");
            Assert.True(prunedWatch.Elapsed < completeWatch.Elapsed, "the pruned build must be the quicker one");
        }

        [Fact]
        public void A_Candidate_Costs_What_Its_Element_Count_Says_It_Should()
        {
            // THE SCALING LAW, as a regression rather than as a note in a report — and the reason the
            // Stage 10.2 resolution cap is also the largest performance change in it.
            //
            // Measured on the controlled model at GridSize 0.1 m, one candidate evaluation costs
            // roughly a fixed setup plus a term LINEAR IN THE NUMBER OF ELEMENTS: every extra blade
            // is another projected face every sample's ray has to be tested against. Halving the
            // permitted element count therefore roughly halves the cost of the expensive candidates,
            // which is what the minimum-feature-size rule does as a side effect of being correct.
            //
            // Asserted as a RATIO, not as wall-clock milliseconds, so it means the same thing on any
            // machine. The bounds are deliberately loose: this is a canary for an accidental
            // per-element blow-up (a quadratic, a per-element re-projection of the whole context),
            // not a benchmark.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            // Wall-clock on a shared CI box is noisy, and a single mean of a handful of iterations is
            // not a measurement. Each count is sampled REPEATEDLY and reduced by MEDIAN, and the
            // counts are INTERLEAVED so that CPU frequency drift or a neighbouring process moves all
            // three together instead of biasing whichever ran first.
            double Sample(int count)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < 5; i++)
                {
                    Attribution(scenario.BaseCache, new HorizontalLouvres(0.3 + 0.001 * i, count, 0.0).ShadingElements(scenario.Target),
                        scenario.Target.AnalysisCells, 0, true);
                }

                stopwatch.Stop();
                return stopwatch.Elapsed.TotalMilliseconds / 5.0;
            }

            double Median(List<double> values)
            {
                List<double> sorted = new List<double>(values);
                sorted.Sort();
                return sorted.Count % 2 == 1
                    ? sorted[sorted.Count / 2]
                    : 0.5 * (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]);
            }

            int[] counts = new int[] { 1, 4, 16 };
            Dictionary<int, List<double>> samples = new Dictionary<int, List<double>>();
            foreach (int count in counts)
            {
                samples[count] = new List<double>();
                // Warm each count once so JIT and first-touch allocation are not charged to it.
                Attribution(scenario.BaseCache, new HorizontalLouvres(0.3, count, 0.0).ShadingElements(scenario.Target),
                    scenario.Target.AnalysisCells, 0, true);
            }

            for (int round = 0; round < 7; round++)
            {
                foreach (int count in counts)
                {
                    samples[count].Add(Sample(count));
                }
            }

            double one = Median(samples[1]);
            double four = Median(samples[4]);
            double sixteen = Median(samples[16]);

            output.WriteLine($"one candidate at GridSize {scenario.BaseCache.CellSize} m: 1 blade {one:0.00} ms, 4 blades {four:0.00} ms, 16 blades {sixteen:0.00} ms");

            // Sixteen blades must not cost sixteen times four blades' worth: growth in the element
            // count has to stay near-linear against a real fixed cost, never super-linear.
            Assert.True(sixteen < 8.0 * four,
                $"cost grew faster than linearly in the element count: 4 blades {four:0.00} ms, 16 blades {sixteen:0.00} ms");

            // And more elements genuinely do cost more, or the measurement above is not measuring
            // what it claims and the guard is worthless. Reported with the numbers, because the two
            // ways this can fail need different responses: if the medians are close but ordered
            // wrongly it is residual noise on a shared machine, and if sixteen blades genuinely cost
            // no more than one then the per-element term is being dominated by fixed setup at this
            // grid size and the scaling law above is not being exercised at all.
            Assert.True(sixteen > one,
                $"sixteen blades did not measure dearer than one: 1 blade {one:0.000} ms, 4 blades {four:0.000} ms, " +
                $"16 blades {sixteen:0.000} ms (medians of 7 interleaved rounds). Either the machine is too noisy to " +
                $"resolve the per-element term, or fixed setup dominates it entirely at GridSize {scenario.BaseCache.CellSize} m.");
        }
    }
}
