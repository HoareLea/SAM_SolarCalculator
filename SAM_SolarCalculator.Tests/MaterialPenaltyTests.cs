// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Gate 0 Review C. Whether scaling the material cost by an ENERGY is the right way to make it
    /// commensurable with the benefit and harm terms, and which energy it should be.
    ///
    ///   Score = UnwantedSolarIntercepted - lambda x WantedSolarBlocked - mu x MaterialFraction x E
    ///
    /// UnwantedSolarIntercepted and WantedSolarBlocked are kWh; MaterialFraction is dimensionless;
    /// so E must be an energy for the three terms to add. The question is which one, and the answer
    /// is decided here by measurement, not by preference.
    /// </summary>
    public class MaterialPenaltyTests
    {
        private readonly ITestOutputHelper output;

        public MaterialPenaltyTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly int Year = 2018;

        private static Face3D SouthWindowFace()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1),
                new Point3D(2, 0, 1),
                new Point3D(2, 0, 2),
                new Point3D(0, 0, 2),
            }));
        }

        private static ApertureSolarTarget Target(double gridSize = 0.25)
        {
            Face3D face = SouthWindowFace();
            return new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), face, Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize));
        }

        private static SolarVisibilityCache Cache(ApertureSolarTarget target, double gridSize = 0.25)
        {
            return Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, 2.0, new List<LinkedFace3D>(), target.AnalysisCells,
                cellSize: gridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);
        }

        private static ApertureDesirability Desirability(ApertureSolarTarget target, SolarVisibilityCache cache, IDesirabilityStrategy strategy, double peakGlobal)
        {
            return Analytical.SolarCalculator.Create.ApertureDesirability(
                target, cache, strategy,
                TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, peakGlobal, 0.2));
        }

        private static IDesirabilityStrategy SummerUnwantedWinterWanted()
        {
            return new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28));
        }

        /// <summary>Score with the material cost scaled by an arbitrary reference energy.</summary>
        private static double Score(ShadingPerformance performance, double lambda, double mu, double referenceEnergy)
        {
            double material = performance.MaterialFraction;
            if (double.IsNaN(material))
            {
                material = 0;
            }

            return performance.UnwantedSolarIntercepted
                 - lambda * performance.WantedSolarBlocked
                 - mu * material * referenceEnergy;
        }

        [Fact]
        public void The_Material_Term_Is_Dimensionally_An_Energy_And_Scale_Invariant()
        {
            // Two geometrically IDENTICAL problems differing only in overall solar magnitude: the
            // same window, the same weighting, weather scaled by 3x. A material term scaled by an
            // admitted ENERGY makes every term scale together, so the winning depth is the same
            // physical answer in both. A material term in bare fraction units does not: it would be
            // negligible in the bright case and dominant in the dim one, so the same building would
            // get a different device for no physical reason.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache cache = Cache(target);

            // A wide magnitude ratio, so a dimensionally inconsistent cost term cannot hide behind
            // being numerically negligible in both cases.
            const double ratio = 300.0;
            ApertureDesirability bright = Desirability(target, cache, SummerUnwantedWinterWanted(), 900.0);
            ApertureDesirability dim = Desirability(target, cache, SummerUnwantedWinterWanted(), 900.0 / ratio);

            double[] depths = new double[] { 0.1, 0.2, 0.3, 0.4, 0.5, 0.7, 0.9, 1.2, 1.6, 2.0 };

            double bestBrightEnergy = double.NaN, bestDimEnergy = double.NaN;
            double bestBrightBare = double.NaN, bestDimBare = double.NaN;
            double bestBrightScoreEnergy = double.NegativeInfinity, bestDimScoreEnergy = double.NegativeInfinity;
            double bestBrightScoreBare = double.NegativeInfinity, bestDimScoreBare = double.NegativeInfinity;

            output.WriteLine("depth |  bright: unwanted kWh  wanted kWh  material | dim: unwanted kWh  wanted kWh");
            foreach (double depth in depths)
            {
                ShadingPerformance b = Analytical.SolarCalculator.Create.ShadingPerformance(
                    target, cache, bright, new List<LinkedFace3D>(), new Overhang(depth));
                ShadingPerformance d = Analytical.SolarCalculator.Create.ShadingPerformance(
                    target, cache, dim, new List<LinkedFace3D>(), new Overhang(depth));

                output.WriteLine($"{depth,5:0.0} | {b.UnwantedSolarIntercepted,20:0.###} {b.WantedSolarBlocked,11:0.###} {b.MaterialFraction,9:0.###} | {d.UnwantedSolarIntercepted,17:0.###} {d.WantedSolarBlocked,11:0.###}");

                // (a) material scaled by the admitted direct energy — an energy, so commensurable
                double sbEnergy = Score(b, 1.0, 0.1, b.AdmittedDirectEnergy);
                double sdEnergy = Score(d, 1.0, 0.1, d.AdmittedDirectEnergy);
                if (sbEnergy > bestBrightScoreEnergy) { bestBrightScoreEnergy = sbEnergy; bestBrightEnergy = depth; }
                if (sdEnergy > bestDimScoreEnergy) { bestDimScoreEnergy = sdEnergy; bestDimEnergy = depth; }

                // (b) material as a bare fraction — dimensionally inconsistent
                double sbBare = Score(b, 1.0, 0.1, 1.0);
                double sdBare = Score(d, 1.0, 0.1, 1.0);
                if (sbBare > bestBrightScoreBare) { bestBrightScoreBare = sbBare; bestBrightBare = depth; }
                if (sdBare > bestDimScoreBare) { bestDimScoreBare = sdBare; bestDimBare = depth; }
            }

            output.WriteLine($"radiation ratio {ratio:0}x");
            output.WriteLine($"material x admitted energy : bright picks {bestBrightEnergy:0.0} m, dim picks {bestDimEnergy:0.0} m");
            output.WriteLine($"material as a bare fraction: bright picks {bestBrightBare:0.0} m, dim picks {bestDimBare:0.0} m");

            // The energy-scaled cost gives the same physical answer under a pure change of units.
            Assert.Equal(bestBrightEnergy, bestDimEnergy);

            // The whole objective scales by the radiation ratio, so it is a pure rescaling.
            Assert.Equal(ratio, bestBrightScoreEnergy / bestDimScoreEnergy, 6);

            // The bare-fraction cost does NOT survive the rescaling. Its argmax happens to hold at
            // this ratio and this mu, so the discriminating measurement is the one that cannot
            // coincide: how heavily the cost term WEIGHS against the benefit term. With an energy
            // reference that weight is a property of the geometry and the brief; with a bare
            // fraction it is a property of how sunny the site is, and moves by the full ratio.
            ShadingPerformance probeBright = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, cache, bright, new List<LinkedFace3D>(), new Overhang(0.5));
            ShadingPerformance probeDim = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, cache, dim, new List<LinkedFace3D>(), new Overhang(0.5));

            double energyWeightBright = 0.1 * probeBright.MaterialFraction * probeBright.AdmittedDirectEnergy / probeBright.UnwantedSolarIntercepted;
            double energyWeightDim = 0.1 * probeDim.MaterialFraction * probeDim.AdmittedDirectEnergy / probeDim.UnwantedSolarIntercepted;
            double bareWeightBright = 0.1 * probeBright.MaterialFraction / probeBright.UnwantedSolarIntercepted;
            double bareWeightDim = 0.1 * probeDim.MaterialFraction / probeDim.UnwantedSolarIntercepted;

            output.WriteLine($"cost / benefit at 0.5 m, energy reference: bright {energyWeightBright:0.#####}, dim {energyWeightDim:0.#####}");
            output.WriteLine($"cost / benefit at 0.5 m, bare fraction  : bright {bareWeightBright:0.########}, dim {bareWeightDim:0.########}  (moved {bareWeightDim / bareWeightBright:0.#}x)");

            Assert.Equal(energyWeightBright, energyWeightDim, 6);
            Assert.Equal(ratio, bareWeightDim / bareWeightBright, 4);
        }

        [Fact]
        public void Scaling_The_Material_Cost_By_Unwanted_Energy_Collapses_When_There_Is_No_Unwanted_Solar()
        {
            // The behavioural defect in the Stage 8 choice of reference energy.
            //
            // Stage 8 scales the material cost by AdmittedUnwantedEnergy. On an aperture with no
            // unwanted solar at all — a north facade, or a brief where every hour of sun is welcome
            // — that reference is ZERO, so material becomes free. Nothing then distinguishes a
            // minimal device from an enormous one among candidates that happen to block the same
            // wanted energy, and Stage 9 needs exactly that distinction to answer "no shading is
            // worth building here" instead of returning arbitrary geometry.
            //
            // Scaling by AdmittedDirectEnergy fixes it: the reference is the beam the aperture
            // actually receives, which is non-zero whenever any device could do anything at all.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache cache = Cache(target);

            // Everything wanted, nothing unwanted.
            ApertureDesirability noUnwanted = Desirability(target, cache, new SeasonalDesirability(null, new AnalysisPeriod(Year)), 900.0);

            ShadingPerformance small = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, cache, noUnwanted, new List<LinkedFace3D>(), new Overhang(0.05));
            ShadingPerformance large = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, cache, noUnwanted, new List<LinkedFace3D>(), new Overhang(0.05, 0.0, 1.0));

            output.WriteLine($"admitted: direct {small.AdmittedDirectEnergy:0.###} kWh, unwanted {small.AdmittedUnwantedEnergy:0.###} kWh, wanted {small.AdmittedWantedEnergy:0.###} kWh");
            output.WriteLine($"small  overhang: material {small.MaterialFraction:0.####}, wanted blocked {small.WantedSolarBlocked:0.######} kWh");
            output.WriteLine($"spread overhang: material {large.MaterialFraction:0.####}, wanted blocked {large.WantedSolarBlocked:0.######} kWh");

            Assert.Equal(0.0, small.AdmittedUnwantedEnergy);
            Assert.True(large.MaterialFraction > small.MaterialFraction, "the spread overhang must use more material");

            // Stage 8's reference energy. The claim under test is precise: the material COST TERM
            // itself — mu x MaterialFraction x reference — is identically zero for every candidate,
            // so material has no influence on the ranking at all. (The spread overhang still loses
            // here, but only through the wanted-solar term; that is the harm term doing the work,
            // not the cost term.)
            const double mu = 0.1;
            double costSmallUnwanted = mu * small.MaterialFraction * small.AdmittedUnwantedEnergy;
            double costLargeUnwanted = mu * large.MaterialFraction * large.AdmittedUnwantedEnergy;
            output.WriteLine($"cost term, reference = AdmittedUnwantedEnergy : small {costSmallUnwanted:0.######} kWh, spread {costLargeUnwanted:0.######} kWh");

            Assert.Equal(0.0, costSmallUnwanted);
            Assert.Equal(0.0, costLargeUnwanted);

            // The corrected reference energy: material costs something, and costs MORE when there
            // is more of it, which is what lets Stage 9 prefer the minimum device when nothing is
            // to be gained by shading.
            double costSmallDirect = mu * small.MaterialFraction * small.AdmittedDirectEnergy;
            double costLargeDirect = mu * large.MaterialFraction * large.AdmittedDirectEnergy;
            output.WriteLine($"cost term, reference = AdmittedDirectEnergy   : small {costSmallDirect:0.######} kWh, spread {costLargeDirect:0.######} kWh");

            Assert.True(costSmallDirect > 0, "a real device must carry a real cost");
            Assert.True(costLargeDirect > costSmallDirect, "more material must cost more");

            // And the resulting ranking still prefers the smaller device.
            double directSmall = Score(small, 1.0, mu, small.AdmittedDirectEnergy);
            double directLarge = Score(large, 1.0, mu, large.AdmittedDirectEnergy);
            output.WriteLine($"score, reference = AdmittedDirectEnergy       : small {directSmall:0.######}, spread {directLarge:0.######}");
            Assert.True(directLarge < directSmall);
        }

        [Fact]
        public void The_Reference_Energy_Is_Nonzero_Wherever_A_Device_Could_Do_Anything()
        {
            // The remaining degenerate case for the corrected reference: an aperture that receives
            // no direct beam at all. There the reference is zero — but so are the benefit and the
            // harm, because there is no beam to intercept or preserve. No device can change
            // anything, so the objective being flat is the correct answer rather than a gap.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache cache = Cache(target);

            // Weather with no radiation whatsoever.
            WeatherData dark = TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, 0.0, 0.2);
            ApertureDesirability desirability = Analytical.SolarCalculator.Create.ApertureDesirability(
                target, cache, SummerUnwantedWinterWanted(), dark);

            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, cache, desirability, new List<LinkedFace3D>(), new Overhang(0.5));

            output.WriteLine($"no beam at all: admitted direct {performance.AdmittedDirectEnergy:0.###} kWh, intercepted {performance.DirectSolarIntercepted:0.###} kWh");
            output.WriteLine($"   efficiency {performance.DirectShadingEfficiency}, unwanted blocked {performance.UnwantedSolarBlocked}, wanted retained {performance.WantedSolarRetained}");

            Assert.Equal(0.0, performance.AdmittedDirectEnergy);
            Assert.Equal(0.0, performance.UnwantedSolarIntercepted);
            Assert.Equal(0.0, performance.WantedSolarBlocked);

            // Every ratio is explicitly unavailable rather than a flattering number.
            Assert.True(double.IsNaN(performance.DirectShadingEfficiency));
            Assert.True(double.IsNaN(performance.UnwantedSolarBlocked));
            Assert.True(double.IsNaN(performance.WantedSolarRetained));
        }
    }
}
