// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 11 Gates 3 and 4: what the two discretisation settings actually cost, measured.
    ///
    /// SunAngleStep groups similar sun positions so the expensive geometric work is done once per
    /// group instead of once per hour. GridSize sets how finely each opening is sampled. Both are
    /// accepted defaults; neither had a published error budget. These tests produce one.
    ///
    /// ANNUAL TOTALS ALONE WOULD LIE. Positive and negative hourly errors cancel, so a grouping that
    /// is badly wrong in both directions can still produce a convincing annual number. Every study
    /// here therefore reports signed bias AND mean absolute error, and the per-orientation spread,
    /// so cancellation is visible rather than flattering.
    /// </summary>
    public class ConvergenceStudyTests
    {
        private readonly ITestOutputHelper output;

        public ConvergenceStudyTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static AnalyticalModel Load()
        {
            return ReferenceExport.LoadMultiAzimuth();
        }

        private static int Year(AnalyticalModel analyticalModel)
        {
            return ReferenceExport.Weather(analyticalModel).WeatherYears.First(x => x != null).Year;
        }

        private static Dictionary<double, double> DirectEnergyPerAzimuth(AnalyticalModel analyticalModel, double gridSize, double sunAngleStep)
        {
            int year = Year(analyticalModel);

            List<ApertureIrradianceResult> results = analyticalModel.SimulateApertures(
                new AnalysisPeriod(year), out bool _, null, null, gridSize,
                SAM.Geometry.SolarCalculator.SkyModel.PerezAnisotropic, sunAngleStep, true, 0.2, SunTimeConvention.IntervalStart);

            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, gridSize);

            Dictionary<double, double> result = new Dictionary<double, double>();
            foreach (ApertureIrradianceResult irradiance in results)
            {
                if (!Guid.TryParse(irradiance.Reference, out Guid apertureGuid))
                {
                    continue;
                }

                ApertureSolarTarget target = targets.Find(x => x.ApertureGuid == apertureGuid);
                if (target == null)
                {
                    continue;
                }

                double azimuth = Math.Round(target.Azimuth, 3);

                // Apertures of the same orientation are the same window as far as the sun is
                // concerned; keep one representative, normalised per square metre so different
                // aperture sizes are comparable.
                result[azimuth] = irradiance.DirectEnergy / target.SampledArea;
            }

            return result;
        }

        // ============================================== GATE 3: SUN-GROUPING BIAS ====

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Sun_Grouping_Has_No_Effect_At_All_On_An_Unobstructed_Aperture()
        {
            // A STRUCTURAL RESULT, and a better one than a small measured error would have been.
            //
            // SunAngleStep does NOT replace the sun position used for the energy. Query.CachedIrradiance
            // computes, per weather hour,
            //
            //     direct = DNI(h) x cos(thetaI(h)) x lit(cell, bin(h))
            //
            // where DNI and cos(thetaI) come from that HOUR'S OWN sun position and the group supplies
            // only the LIT BIT. The grouping approximation is therefore confined entirely to the
            // shadowing question — "was this sample in shadow at this hour" — and touches no energy
            // term. On an aperture nothing obstructs, every sample is lit at every hour whatever the
            // grouping, so the answer is not approximately but EXACTLY independent of SunAngleStep.
            //
            // This bounds the whole approximation: the error can only ever live near shadow edges,
            // which is where the next test looks for it.
            AnalyticalModel referenceModel = Load();
            Dictionary<double, double> reference = DirectEnergyPerAzimuth(referenceModel, 0.5, 0.5);

            output.WriteLine("Annual direct beam, kWh/m², unobstructed apertures");
            output.WriteLine("step     az 0        az 90       az 180      az 270");

            foreach (double sunAngleStep in new double[] { 0.5, 1.0, 2.0, 5.0, 10.0 })
            {
                Dictionary<double, double> grouped = DirectEnergyPerAzimuth(Load(), 0.5, sunAngleStep);

                output.WriteLine($"{sunAngleStep,-6:0.#}  " + string.Join("  ", reference.Keys.OrderBy(x => x)
                    .Select(a => $"{grouped[a],10:0.####}")));

                foreach (double azimuth in reference.Keys)
                {
                    // Bit-for-bit, not "within a tolerance". Anything else would mean the grouping
                    // had leaked into an energy term.
                    Assert.Equal(reference[azimuth], grouped[azimuth], 9);
                }
            }

            output.WriteLine("");
            output.WriteLine("Identical to nine decimal places at every step, including 10°: the grouping decides");
            output.WriteLine("only which samples are lit, never how much energy a lit sample receives.");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Sun_Grouping_Bias_Is_Measured_Where_It_Can_Exist_At_All()
        {
            // The bias study proper, on a CONTEXT-OBSTRUCTED aperture — the only place grouping can
            // do anything, per the structural result above. A wide slab 2.5 m above the head casts a
            // hard shadow edge that sweeps the glass through the day.
            //
            // Reported as signed bias AND mean absolute error, because annual totals alone let
            // positive and negative hourly errors cancel and can flatter a grouping that is wrong in
            // both directions.
            output.WriteLine("Context-obstructed south aperture, against a 0.5° reference grouping");
            output.WriteLine("");
            output.WriteLine("step    admitted kWh   signed error   relative    unwanted kWh   wanted kWh");

            double referenceAdmitted = double.NaN;
            Dictionary<double, double> relativeError = new Dictionary<double, double>();

            foreach (double sunAngleStep in new double[] { 0.5, 1.0, 2.0, 5.0 })
            {
                OptimisationFixture.Scenario scenario = OptimisationFixture.Get(
                    $"south-blocked-step-{sunAngleStep}", OptimisationFixture.SouthWindow, OptimisationFixture.HighSlabContext,
                    OptimisationFixture.SummerUnwantedWinterWanted, 0.25, 900.0, sunAngleStep);

                ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, new NoShading());

                double admitted = performance.AdmittedDirectEnergy;

                if (double.IsNaN(referenceAdmitted))
                {
                    referenceAdmitted = admitted;
                    output.WriteLine($"{sunAngleStep,-6:0.#}  {admitted,13:0.###}   (reference)                {performance.AdmittedUnwantedEnergy,12:0.###}   {performance.AdmittedWantedEnergy,10:0.###}");
                    continue;
                }

                double signed = admitted - referenceAdmitted;
                double relative = 100.0 * signed / referenceAdmitted;
                relativeError[sunAngleStep] = relative;

                output.WriteLine($"{sunAngleStep,-6:0.#}  {admitted,13:0.###}   {signed,+12:0.####}   {relative,+8:0.####} %  {performance.AdmittedUnwantedEnergy,12:0.###}   {performance.AdmittedWantedEnergy,10:0.###}");
            }

            output.WriteLine("");
            foreach (KeyValuePair<double, double> pair in relativeError.OrderBy(x => x.Key))
            {
                output.WriteLine($"step {pair.Key:0.#}°: {pair.Value:+0.####;-0.####} % on admitted direct beam through a hard shadow edge");
            }

            // THE ERROR IS A QUANTISATION EFFECT AND IS NOT MONOTONE IN THE STEP, which is worth
            // stating plainly because the obvious expectation is wrong. A group's representative
            // direction either falls on the lit side of a shadow edge or the shaded side; making the
            // groups finer changes WHERE those representatives land, not how big the error is in any
            // smooth way. Measured here, 5° happens to land closer to the reference than 2° does.
            //
            // The defensible conclusion is therefore a BOUND, not an ordering: across 1°, 2° and 5°
            // the whole effect stays a few tenths of a per cent on a hard-edged obstruction. That is
            // the number the recommended settings rest on, and it is why the accepted 2° default is
            // left alone — the evidence gives no reason to change it, and a finer step would buy
            // nothing measurable while costing sun groups.
            double worst = relativeError.Values.Max(x => Math.Abs(x));
            output.WriteLine($"worst departure across 1°, 2° and 5°: {worst:0.####} % — and NOT monotone in the step");

            Assert.True(worst < 1.0,
                $"sun grouping moves a context-obstructed aperture's admitted beam by {worst:0.###} % across 1°–5°");

            Assert.True(Math.Abs(relativeError[2.0]) < 1.0,
                $"the 2° default carries {relativeError[2.0]:+0.###;-0.###} % on a context-obstructed aperture");

            output.WriteLine("");
            output.WriteLine($"the accepted 2° default carries {relativeError[2.0]:+0.###;-0.###} % here; 1° gives {relativeError[1.0]:+0.###;-0.###} %, 5° gives {relativeError[5.0]:+0.###;-0.###} %");
            output.WriteLine("All three are far inside the precision a shading design is read at, so 2° stands.");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Sun_Grouping_Is_Stressed_By_A_Sharp_Shading_Edge()
        {
            // Annual irradiance is a forgiving test of grouping: it averages over the whole sky. A
            // SHADING EDGE is not — a device with a sharp cut-off turns a small angular error into a
            // lit/shaded flip, which is where grouping should hurt most. This is the case the plan
            // asked to be stressed.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            // A deep, shallow-set overhang: its shadow edge sweeps the glass quickly, so which side
            // of it an hour falls on is sensitive to the sun direction used.
            IShadingTypology device = new Overhang(0.8, 0.0, 0.5);

            output.WriteLine("A sharp shading edge, against a 0.5° reference grouping");
            output.WriteLine("step   unwanted blocked   wanted retained   intercepted kWh   d blocked   d intercepted");

            double referenceBlocked = double.NaN;
            double referenceIntercepted = double.NaN;
            double worstBlocked = 0;

            foreach (double sunAngleStep in new double[] { 0.5, 1.0, 2.0, 5.0 })
            {
                OptimisationFixture.Scenario stepped = OptimisationFixture.Get(
                    $"south-seasonal-step-{sunAngleStep}", OptimisationFixture.SouthWindow, null,
                    OptimisationFixture.SummerUnwantedWinterWanted, 0.25, 900.0, sunAngleStep);

                ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                    stepped.Target, stepped.BaseCache, stepped.Desirability, stepped.Context, device);

                double blocked = 100.0 * performance.UnwantedSolarBlocked;
                double retained = 100.0 * performance.WantedSolarRetained;
                double intercepted = performance.DirectSolarIntercepted;

                if (double.IsNaN(referenceBlocked))
                {
                    referenceBlocked = blocked;
                    referenceIntercepted = intercepted;
                    output.WriteLine($"{sunAngleStep,-5:0.#}  {blocked,16:0.###}   {retained,15:0.###}   {intercepted,15:0.##}   (reference)");
                    continue;
                }

                double dBlocked = blocked - referenceBlocked;
                double dIntercepted = 100.0 * (intercepted - referenceIntercepted) / referenceIntercepted;
                worstBlocked = Math.Max(worstBlocked, Math.Abs(dBlocked));

                output.WriteLine($"{sunAngleStep,-5:0.#}  {blocked,16:0.###}   {retained,15:0.###}   {intercepted,15:0.##}   {dBlocked,+9:0.###}   {dIntercepted,+13:0.###} %");

                if (Math.Abs(sunAngleStep - 2.0) < 1e-9)
                {
                    // The default, on the hardest case the fixture offers. A percentage point on a
                    // blocked-fraction is well inside the precision a shading design is read at.
                    Assert.True(Math.Abs(dBlocked) < 1.5,
                        $"at the 2° default a sharp shading edge moves the blocked fraction by {dBlocked:+0.###;-0.###} points against a 0.5° reference");
                }
            }

            output.WriteLine("");
            output.WriteLine($"worst departure from the 0.5° reference across 1°, 2° and 5°: {worstBlocked:0.###} percentage points of unwanted solar blocked");
        }

        // ==================================================== GATE 4: GRID CONVERGENCE ====

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Aperture_Irradiance_Converges_With_Grid_Size()
        {
            // Absolute energy per square metre, so aperture size and the sampled-area effect from
            // Gate 0 cannot masquerade as convergence. Grid sizes chosen to divide the fixture's
            // openings exactly (1.0 and 1.6667 m across, 2.5 m up), which is what the recommended
            // settings tell an engineer to do.
            AnalyticalModel referenceModel = Load();
            Dictionary<double, double> reference = DirectEnergyPerAzimuth(referenceModel, 0.1, 2.0);

            output.WriteLine("Direct beam, kWh/m², against a 0.1 m reference grid");
            output.WriteLine("");
            output.WriteLine("grid    azimuth   reference   measured    signed error   relative");

            Dictionary<double, List<double>> relativeErrors = new Dictionary<double, List<double>>();

            foreach (double gridSize in new double[] { 1.0, 0.5, 0.25, 0.2 })
            {
                AnalyticalModel model = Load();
                Dictionary<double, double> measured = DirectEnergyPerAzimuth(model, gridSize, 2.0);

                List<double> errors = new List<double>();
                foreach (double azimuth in reference.Keys.OrderBy(x => x))
                {
                    double expected = reference[azimuth];
                    double actual = measured[azimuth];
                    double relative = 100.0 * (actual - expected) / expected;

                    errors.Add(relative);
                    output.WriteLine($"{gridSize,-6:0.###}  {azimuth,7:0}   {expected,9:0.###}   {actual,9:0.###}   {actual - expected,+12:0.####}   {relative,+7:0.###} %");
                }

                relativeErrors[gridSize] = errors;
            }

            output.WriteLine("");
            output.WriteLine("grid    signed bias    MAE       worst orientation");
            foreach (double gridSize in new double[] { 1.0, 0.5, 0.25, 0.2 })
            {
                List<double> errors = relativeErrors[gridSize];
                output.WriteLine($"{gridSize,-6:0.###}  {errors.Average(),+11:0.####} %  {errors.Average(x => Math.Abs(x)),7:0.####} %  {errors.Max(x => Math.Abs(x)),7:0.####} %");
            }

            // Refining must converge: a finer grid must not be further from the finest.
            double mae05 = relativeErrors[0.5].Average(x => Math.Abs(x));
            double mae02 = relativeErrors[0.2].Average(x => Math.Abs(x));

            Assert.True(mae02 <= mae05 + 1e-9, "refining the grid moved the answer further from the reference");

            // THE SAME STRUCTURAL POINT AS THE SUN GROUPING. On an UNOBSTRUCTED aperture every sample
            // shares one outward normal and is lit at every hour, so the per-square-metre direct beam
            // is not approximately but EXACTLY grid-independent. GridSize buys nothing here.
            //
            // It earns its keep entirely on SHADOW EDGES — from context or from a device — which is
            // where the next test measures it, and that is the study the recommended settings are
            // actually built on.
            Assert.True(mae05 < 1e-6,
                $"unobstructed aperture irradiance should be exactly grid-independent, but the 0.5 m grid differs by {mae05:0.######} %");

            output.WriteLine("");
            output.WriteLine("Exactly grid-independent: every sample on an unobstructed opening shares one normal");
            output.WriteLine("and is lit at every hour. GridSize matters only where there is a shadow edge to resolve.");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Verified_Shading_Metrics_Converge_With_Grid_Size()
        {
            // Where the grid ACTUALLY matters. A shading device casts a moving shadow edge across
            // the opening, and how finely the opening is sampled decides how well that edge is
            // resolved. This is the convergence study an engineer needs in order to choose a grid.
            AnalyticalModel referenceModel = Load();
            int year = Year(referenceModel);

            Guid apertureGuid = referenceModel.ApertureSolarTargets(null, 0.5)
                .First(x => Math.Abs(x.Azimuth - 180.0) < 1e-6).ApertureGuid;

            // THE DEVICE MUST SATISFY THE MINIMUM FEATURE-SIZE RULE AT EVERY GRID TESTED, or this
            // measures the Stage 10.2 degeneracy rather than grid convergence. Three louvres over the
            // 2.5 m opening is a 1.25 m pitch — 2.5x the coarsest grid below and 12.5x the finest —
            // so every row here is a device Stage 9 would legitimately propose at that grid.
            //
            // The companion test measures what happens when that rule is BROKEN, which turns out to
            // be the larger effect by far.
            IShadingTypology device = new HorizontalLouvres(0.3, 3, 0.0);

            output.WriteLine("One fixed device (3 louvres at 1.25 m pitch, 0.3 m deep) on the south aperture, against a 0.1 m reference grid");
            output.WriteLine("Pitch / GridSize is 2.5 or more at every row, so the rule is satisfied throughout.");
            output.WriteLine("");
            output.WriteLine("grid    samples   blocked %   retained %   neutral kWh   intercepted kWh   d blocked   d retained");

            double referenceBlocked = double.NaN;
            double referenceRetained = double.NaN;
            Dictionary<double, double> blockedError = new Dictionary<double, double>();

            foreach (double gridSize in new double[] { 0.1, 0.2, 0.25, 0.5, 1.0 })
            {
                ApertureShadingSetup setup = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                    Load(), apertureGuid, year, null, null, null, null,
                    new List<Guid> { apertureGuid }, gridSize, 2.0);

                Assert.NotNull(setup);

                ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                    setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability,
                    setup.Context.ContextOccluders, device, setup.CellIndexOffset);

                double blocked = 100.0 * performance.UnwantedSolarBlocked;
                double retained = 100.0 * performance.WantedSolarRetained;

                if (double.IsNaN(referenceBlocked))
                {
                    referenceBlocked = blocked;
                    referenceRetained = retained;
                    output.WriteLine($"{gridSize,-6:0.###}  {setup.Target.CellCount,7}   {blocked,9:0.###}   {retained,10:0.###}   {performance.NeutralSolarIntercepted,11:0.##}   {performance.DirectSolarIntercepted,15:0.##}   (reference)");
                    continue;
                }

                double dBlocked = blocked - referenceBlocked;
                double dRetained = retained - referenceRetained;
                blockedError[gridSize] = Math.Abs(dBlocked);

                output.WriteLine($"{gridSize,-6:0.###}  {setup.Target.CellCount,7}   {blocked,9:0.###}   {retained,10:0.###}   {performance.NeutralSolarIntercepted,11:0.##}   {performance.DirectSolarIntercepted,15:0.##}   {dBlocked,+9:0.###}   {dRetained,+10:0.###}");

                // The accounting identity must hold at EVERY grid size, or a convergence number
                // would be describing an inconsistent calculation.
                Assert.Equal(performance.DirectSolarIntercepted,
                    performance.UnwantedSolarIntercepted + performance.WantedSolarBlocked + performance.NeutralSolarIntercepted, 9);
            }

            output.WriteLine("");
            foreach (KeyValuePair<double, double> pair in blockedError.OrderBy(x => x.Key))
            {
                output.WriteLine($"grid {pair.Key:0.###} m: {pair.Value:0.###} percentage points from the 0.1 m answer");
            }

            // THE HEADLINE FINDING OF GATE 4, and it changes the recommended settings.
            //
            // The 0.5 m DEFAULT IS NOT ADEQUATE FOR VERIFIED SHADING METRICS. On this aperture it
            // leaves only 20 samples, and the unwanted-blocked figure lands about 6 percentage points
            // from the 0.1 m answer — while wanted-solar-retained reads an exact 100 %, which is the
            // same "looks like a triumph" pattern Stage 9 and 10.2 chased: with few enough samples,
            // the winter beam simply misses all of them. At 1 m there are six samples and the figure
            // is 24 points out.
            //
            // This does NOT contradict the irradiance study above. Unobstructed irradiance is exactly
            // grid-independent because every sample sees the same sun; a DEVICE creates shadow edges,
            // and resolving those edges is the entire job GridSize does. The two results together are
            // the recommendation: 0.5 m is fine to look at solar on a facade, and too coarse to size
            // a shading device with.
            Assert.True(blockedError[1.0] > 10.0,
                "a 1 m grid should be visibly unusable for device verification; if it is not, this fixture is too forgiving to base advice on");

            // A DESIGN-GRADE GRID. This is the number the recommended settings table rests on.
            Assert.True(blockedError[0.25] < 3.0,
                $"a 0.25 m grid is {blockedError[0.25]:0.##} percentage points from a 0.1 m grid");

            // CONVERGENCE IS NOT MONOTONE, for the same reason it was not in Stage 10.2: where a
            // shadow edge falls relative to the sample lattice is a phase effect, so a slightly
            // coarser grid can land closer by luck. The trend across the range is what matters, and
            // it is unambiguous — the two finest grids are both far better than the two coarsest.
            double fine = Math.Max(blockedError[0.2], blockedError[0.25]);
            double coarse = Math.Min(blockedError[0.5], blockedError[1.0]);

            Assert.True(fine < coarse,
                $"the finer grids ({fine:0.##} points at worst) must beat the coarser ones ({coarse:0.##} points at best)");

            output.WriteLine("");
            output.WriteLine($"MEASURED: the 0.5 m default carries {blockedError[0.5]:0.#} percentage points on unwanted-solar-blocked");
            output.WriteLine($"for a device it can legitimately resolve. 0.25 m carries {blockedError[0.25]:0.#}. 1 m carries {blockedError[1.0]:0.#}.");
            output.WriteLine("Use 0.5 m to look at irradiance; use 0.25 m or finer to size and verify a device.");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Breaking_The_Minimum_Feature_Size_Rule_Costs_Far_More_Than_The_Grid_Itself()
        {
            // INDEPENDENT CONFIRMATION OF THE STAGE 10.2 RULE, found while measuring something else.
            //
            // The same study as above, run on a device whose pitch does NOT clear two analysis cells
            // at the coarse end. Six louvres over the 2.5 m opening is a 0.5 m pitch: at a 0.5 m grid
            // that is exactly one cell per stripe — the degenerate case Stage 10.2 identified, where
            // the measured shaded fraction is decided by lattice phase rather than by the device.
            //
            // Stage 10.2 argued this from the sampling theory and measured it on fins at a fixed
            // pitch. This arrives at it from the opposite direction — a grid-convergence study on
            // louvres — and finds the same thing, which is much stronger evidence than either alone.
            AnalyticalModel referenceModel = Load();
            int year = Year(referenceModel);

            Guid apertureGuid = referenceModel.ApertureSolarTargets(null, 0.5)
                .First(x => Math.Abs(x.Azimuth - 180.0) < 1e-6).ApertureGuid;

            IShadingTypology device = new HorizontalLouvres(0.3, 6, 0.0);

            output.WriteLine("Six louvres at 0.5 m pitch on the south aperture, against a 0.1 m reference grid");
            output.WriteLine("");
            output.WriteLine("grid    pitch/grid   satisfies the rule?   blocked %   d blocked");

            double referenceBlocked = double.NaN;
            Dictionary<double, double> error = new Dictionary<double, double>();

            foreach (double gridSize in new double[] { 0.1, 0.2, 0.25, 0.5, 1.0 })
            {
                ApertureShadingSetup setup = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                    Load(), apertureGuid, year, null, null, null, null,
                    new List<Guid> { apertureGuid }, gridSize, 2.0);

                ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                    setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability,
                    setup.Context.ContextOccluders, device, setup.CellIndexOffset);

                double blocked = 100.0 * performance.UnwantedSolarBlocked;

                ShadingResolutionState state = Analytical.SolarCalculator.Query.ShadingResolution(
                    device, setup.Target, gridSize, out string _, out double pitch);

                bool satisfies = state == ShadingResolutionState.Resolved;

                if (double.IsNaN(referenceBlocked))
                {
                    referenceBlocked = blocked;
                    output.WriteLine($"{gridSize,-6:0.###}  {pitch / gridSize,10:0.##}   {(satisfies ? "yes" : "NO "),19}   {blocked,9:0.###}   (reference)");
                    continue;
                }

                double d = Math.Abs(blocked - referenceBlocked);
                error[gridSize] = d;
                output.WriteLine($"{gridSize,-6:0.###}  {pitch / gridSize,10:0.##}   {(satisfies ? "yes" : "NO "),19}   {blocked,9:0.###}   {d,9:0.###}");
            }

            output.WriteLine("");

            // At 0.5 m the pitch/grid ratio is exactly 1 — the rule is broken — and the error is far
            // larger than at the coarser-but-still-legal 0.25 m.
            output.WriteLine($"0.25 m grid (ratio 2, rule satisfied): {error[0.25]:0.##} points");
            output.WriteLine($"0.50 m grid (ratio 1, rule BROKEN):    {error[0.5]:0.##} points");

            Assert.True(error[0.5] > 2.0 * error[0.25],
                $"the degenerate 0.5 m case ({error[0.5]:0.##} points) should be markedly worse than the legal 0.25 m case ({error[0.25]:0.##} points)");

            output.WriteLine("");
            output.WriteLine("A COARSER GRID GAVE A BETTER ANSWER THAN A FINER ONE HERE — 0.25 m beats 0.5 m by more");
            output.WriteLine("than a factor of two — because the 0.5 m case is sampling-degenerate rather than merely");
            output.WriteLine("coarse. This is the Stage 10.2 minimum-feature-size rule arrived at from the opposite");
            output.WriteLine("direction, and it is why that rule caps element counts rather than trusting refinement.");
        }

        [Fact]
        public void The_Shading_Potential_Field_Is_Spatially_Stable_Under_Voxel_Refinement()
        {
            // VoxelSize convergence. The field is a SPATIAL potential, and Stage 10.2 established
            // that its summed total is not an energy — so convergence is judged on what the field is
            // actually used for: WHERE the positive region is, and how the selected ideal region
            // behaves. Judging it on the sum would be measuring the thing that was renamed for being
            // misleading.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            output.WriteLine("VoxelSize convergence on the south window (1 m deep studied space)");
            output.WriteLine("");
            output.WriteLine("voxel   voxels   positive %   max voxel [kWh]   captured 90% region: voxels   projected area m²   depth m");

            double referenceMax = double.NaN;
            double referenceArea = double.NaN;
            double referenceDepth = double.NaN;
            double worstAreaError = 0;
            double worstMaxError = 0;
            List<double> positiveFractions = new List<double>();

            foreach (double voxelSize in new double[] { 0.05, 0.1, 0.2 })
            {
                ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(scenario.Target, 1.0, 0.5, 0.0, 0.3, 0.3, voxelSize);
                ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, volume);

                Assert.NotNull(field);

                int positive = 0;
                for (int i = 0; i < volume.VoxelCount; i++)
                {
                    if (field.Score(i) > 0)
                    {
                        positive++;
                    }
                }

                double positiveFraction = 100.0 * positive / volume.VoxelCount;

                IdealShadingResult ideal = Analytical.SolarCalculator.Create.IdealShadingResult(
                    field, ShadingThresholdMethod.CumulativeCapture, 0.9);

                output.WriteLine($"{voxelSize,-6:0.###}  {volume.VoxelCount,7}   {positiveFraction,10:0.##}   {field.MaxScore(),15:0.####}   " +
                    $"{ideal.SelectedVoxelCount,27}   {ideal.ProjectedArea,17:0.####}   {ideal.MaxProjectionDepth,7:0.###}");

                positiveFractions.Add(positiveFraction);

                if (double.IsNaN(referenceMax))
                {
                    referenceMax = field.MaxScore();
                    referenceArea = ideal.ProjectedArea;
                    referenceDepth = ideal.MaxProjectionDepth;
                    continue;
                }

                double maxError = 100.0 * Math.Abs(field.MaxScore() - referenceMax) / referenceMax;
                double areaError = 100.0 * Math.Abs(ideal.ProjectedArea - referenceArea) / referenceArea;
                double depthError = 100.0 * Math.Abs(ideal.MaxProjectionDepth - referenceDepth) / referenceDepth;

                output.WriteLine($"        vs the 0.05 m field: max-voxel value {maxError:0.#} %, region projected area {areaError:0.#} %, region depth {depthError:0.#} %");

                worstAreaError = Math.Max(worstAreaError, areaError);
                worstMaxError = Math.Max(worstMaxError, maxError);
            }

            output.WriteLine("");

            // WHAT IS STABLE. The SPATIAL PATTERN — the share of the studied space that is worth
            // filling at all — barely moves across a 60x change in voxel count. That is the property
            // the red/blue map is read for, and it holds.
            double spread = positiveFractions.Max() - positiveFractions.Min();
            output.WriteLine($"positive share of the studied space: {string.Join(" / ", positiveFractions.Select(x => $"{x:0.#} %"))} — spread {spread:0.#} points");

            Assert.True(spread < 6.0,
                $"the positive region's share of the studied space moved {spread:0.#} points across the voxel range; the map's spatial pattern is not stable");

            // WHAT IS NOT. Per-voxel VALUES and the extracted region's GEOMETRY both move a great
            // deal — a bigger voxel is a bigger target and intercepts more, so the field's absolute
            // values scale with voxel size, and the threshold that selects 90 % of the potential
            // therefore lands in a different place.
            //
            // MEASURED AND RECORDED RATHER THAN ASSERTED AWAY. This is not a defect to fix in
            // Stage 11 and it is not hidden either: it is the quantitative backing for the standing
            // statement that the Stage 7 shape is DESIGN INTENT AND NOT VERIFIED PERFORMANCE
            // GEOMETRY. An engineer must not read dimensions off it; VerifyShading on a real device
            // is the answer. The assumptions register carries these numbers.
            output.WriteLine($"per-voxel values move up to {worstMaxError:0.#} % and the selected region's projected area up to {worstAreaError:0.#} % across 0.05–0.2 m");
            output.WriteLine("");
            output.WriteLine("MEASURED LIMITATION: the field's absolute per-voxel values and the extracted ideal");
            output.WriteLine("region's geometry are strongly voxel-size dependent. Only the spatial PATTERN is");
            output.WriteLine("stable. This is why the ideal shape is design intent, not performance geometry, and");
            output.WriteLine("why no dimension should be taken off it.");

            // The instability is real and must stay visible; if it ever became small, the register
            // entry would be overstating the caution and should be revisited.
            Assert.True(worstAreaError > 5.0,
                "the ideal region's geometry has become voxel-independent; the assumptions register overstates this limitation and needs revisiting");

            // NOTE: the SUMMED field potential is deliberately not used as a convergence metric. It
            // scales with the voxel count by construction, which the next test proves.
        }

        [Fact]
        public void The_Summed_Field_Potential_Scales_With_Voxel_Count_And_Is_Therefore_Not_An_Energy()
        {
            // The positive proof of the statement above, so nobody is tempted to treat the sum as a
            // convergent physical quantity. Halving the voxel size roughly doubles the number of
            // voxels a ray passes through, so the sum grows — it does not settle.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            output.WriteLine("voxel   voxels    summed positive potential   ratio to the previous");

            double previous = double.NaN;
            foreach (double voxelSize in new double[] { 0.2, 0.1, 0.05 })
            {
                ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(scenario.Target, 1.0, 0.5, 0.0, 0.3, 0.3, voxelSize);
                ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, volume);

                double total = field.PositiveTotal();
                string ratio = double.IsNaN(previous) ? "" : $"{total / previous:0.##}x";
                output.WriteLine($"{voxelSize,-6:0.###}  {volume.VoxelCount,7}   {total,25:0.#}   {ratio,20}");

                if (!double.IsNaN(previous))
                {
                    // It grows. If it ever converged, the Stage 10.2 terminology change would have
                    // been wrong and this needs revisiting.
                    Assert.True(total > previous * 1.2,
                        "the summed potential did not grow with voxel refinement; the 'not an energy' argument needs re-checking");
                }

                previous = total;
            }

            output.WriteLine("");
            output.WriteLine("The sum grows without limit as the voxels shrink, which is exactly why it carries no");
            output.WriteLine("kWh label and why only VerifyShading reports what a device saves.");
        }
    }
}
