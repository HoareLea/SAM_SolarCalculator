// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 8: typologies, analytical seeding, the fit score, the four performance metrics, and
    /// the ideal-versus-rationalised comparison.
    /// </summary>
    public class RationalisedShadingTests
    {
        private readonly ITestOutputHelper output;

        public RationalisedShadingTests(ITestOutputHelper output)
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

        private static SolarVisibilityCache Cache(ApertureSolarTarget target, List<LinkedFace3D> occluders, double gridSize = 0.25)
        {
            return Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, 2.0, occluders, target.AnalysisCells,
                cellSize: gridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);
        }

        private static ApertureDesirability Desirability(ApertureSolarTarget target, SolarVisibilityCache cache)
        {
            return Analytical.SolarCalculator.Create.ApertureDesirability(
                target, cache,
                new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28)),
                TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, 900.0, 0.2));
        }

        [Fact]
        public void Typologies_Generate_Deterministic_Bounded_Geometry()
        {
            ApertureSolarTarget target = Target();

            IShadingTypology[] typologies = new IShadingTypology[]
            {
                new Overhang(0.5, 0.05, 0.1),
                new HorizontalLouvres(0.3, 4, 15.0),
                new VerticalFins(0.4, 3, 0.0),
                new EggCrate(0.3, 3, 3),
            };

            HashSet<Guid> allGuids = new HashSet<Guid>();
            foreach (IShadingTypology typology in typologies)
            {
                List<ShadingElement> elements = typology.ShadingElements(target);
                Assert.NotNull(elements);
                Assert.NotEmpty(elements);

                double area = 0;
                foreach (ShadingElement element in elements)
                {
                    Assert.NotNull(element.Face3D);
                    Assert.True(element.Area > 0, $"{typology.Name} produced a degenerate face");
                    area += element.Area;

                    // Guids must be unique across the whole family set, not just within one device.
                    Assert.True(allGuids.Add(element.Guid), $"duplicate element Guid in {typology.Name}");
                }

                output.WriteLine($"{typology.Name}: {elements.Count} elements, {area:0.###} m2, material fraction {typology.MaterialFraction(target):0.###}");

                // Regenerating must reproduce the same identities, or nothing downstream can compare
                // two candidates or reuse an attribution cache.
                List<ShadingElement> again = typology.ShadingElements(target);
                for (int i = 0; i < elements.Count; i++)
                {
                    Assert.Equal(elements[i].Guid, again[i].Guid);
                }

                // Parameters clamp into their declared bounds rather than producing silly geometry.
                Assert.True(typology.TryGetBounds("Depth", out double minimum, out double maximum));
                Assert.True(typology.SetParameter("Depth", maximum * 100));
                Assert.Equal(maximum, typology.GetParameter("Depth"));
                Assert.True(typology.SetParameter("Depth", -5.0));
                Assert.Equal(minimum, typology.GetParameter("Depth"));
                Assert.False(typology.SetParameter("NoSuchParameter", 1.0));
            }
        }

        [Fact]
        public void Deeper_Overhangs_Block_More_Unwanted_And_Retain_Less_Wanted()
        {
            // The monotonic sanity of the four metrics. A deeper overhang must block more unwanted
            // solar and retain less wanted solar; if the metrics did not move that way, they would
            // not be measuring what their names claim.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, baseCache);

            double previousBlocked = -1;
            double previousRetained = 2;
            output.WriteLine("depth |  intercepted kWh | efficiency | unwanted blocked | wanted retained");
            foreach (double depth in new double[] { 0.2, 0.4, 0.6, 1.0 })
            {
                ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                    target, baseCache, desirability, new List<LinkedFace3D>(), new Overhang(depth));

                Assert.NotNull(performance);
                output.WriteLine($"{depth:0.0} m | {performance.DirectSolarIntercepted,10:0.###} | {performance.DirectShadingEfficiency * 100,7:0.#} % | {performance.UnwantedSolarBlocked * 100,9:0.#} % | {performance.WantedSolarRetained * 100,9:0.#} %");

                Assert.InRange(performance.DirectShadingEfficiency, 0.0, 1.0);
                Assert.InRange(performance.UnwantedSolarBlocked, 0.0, 1.0);
                Assert.InRange(performance.WantedSolarRetained, 0.0, 1.0);

                Assert.True(performance.UnwantedSolarBlocked > previousBlocked, "a deeper overhang must block more unwanted solar");
                Assert.True(performance.WantedSolarRetained < previousRetained, "a deeper overhang must retain less wanted solar");

                previousBlocked = performance.UnwantedSolarBlocked;
                previousRetained = performance.WantedSolarRetained;

                // Conservation, on every candidate.
                Assert.Equal(performance.DirectSolarIntercepted, performance.ReconciledInterceptedEnergy, 6);
            }
        }

        [Fact]
        public void Zero_Denominators_Report_NaN_Rather_Than_A_Flattering_Percentage()
        {
            // No wanted solar in the weighting at all. "Wanted Solar Retained" then has nothing to
            // divide by, and 100 % would read as "this device preserves all the useful sun" when the
            // honest answer is that the question does not apply.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability noWanted = Analytical.SolarCalculator.Create.ApertureDesirability(
                target, baseCache, new SeasonalDesirability(new AnalysisPeriod(Year), null),
                TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, 900.0, 0.2));

            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, noWanted, new List<LinkedFace3D>(), new Overhang(0.5));

            output.WriteLine($"no wanted solar: admitted wanted {performance.AdmittedWantedEnergy:0.###} kWh, wanted retained {performance.WantedSolarRetained}");
            Assert.Equal(0.0, performance.AdmittedWantedEnergy);
            Assert.True(double.IsNaN(performance.WantedSolarRetained), "a zero denominator must be NaN, not 100 %");
            Assert.False(double.IsNaN(performance.UnwantedSolarBlocked));

            // And the mirror case: nothing unwanted.
            ApertureDesirability noUnwanted = Analytical.SolarCalculator.Create.ApertureDesirability(
                target, baseCache, new SeasonalDesirability(null, new AnalysisPeriod(Year)),
                TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, 900.0, 0.2));

            ShadingPerformance mirror = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, noUnwanted, new List<LinkedFace3D>(), new Overhang(0.5));

            Assert.Equal(0.0, mirror.AdmittedUnwantedEnergy);
            Assert.True(double.IsNaN(mirror.UnwantedSolarBlocked), "a zero denominator must be NaN, not 0 %");
            Assert.False(double.IsNaN(mirror.WantedSolarRetained));
        }

        [Fact]
        public void Fit_Score_Penalises_Blocking_Wanted_Solar()
        {
            // Guards the sign of the wanted-solar term. If WantedSolarBlocked were ADDED rather than
            // subtracted, raising the penalty would make a device that destroys winter sun look
            // BETTER, which is the exact error this score is written to avoid.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, baseCache);

            ShadingPerformance deep = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, desirability, new List<LinkedFace3D>(), new Overhang(1.5));

            double lenient = Analytical.SolarCalculator.Create.ShadingFitScore(deep, 0.5, 0.0);
            double strict = Analytical.SolarCalculator.Create.ShadingFitScore(deep, 2.0, 0.0);
            output.WriteLine($"deep overhang blocks {deep.WantedSolarBlocked:0.###} kWh of wanted solar; fit score at penalty 0.5 = {lenient:0.###}, at 2.0 = {strict:0.###}");

            Assert.True(deep.WantedSolarBlocked > 0, "a 1.5 m overhang on a south window must cost some winter sun");
            Assert.True(strict < lenient, "raising the wanted-solar penalty must LOWER the score of a device that blocks wanted solar");

            // And the material term is a cost too.
            double withMaterial = Analytical.SolarCalculator.Create.ShadingFitScore(deep, 0.5, 0.5);
            Assert.True(withMaterial < lenient, "material must be a penalty, not a reward");
        }

        [Fact]
        public void Rationalisation_Seeds_From_The_Field_And_Beats_An_Arbitrary_Device()
        {
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, baseCache);
            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(target, maxDepth: 1.5, up: 1.0, down: 0.2, left: 0.5, right: 0.5, voxelSize: 0.1);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, baseCache, desirability, volume);

            double seed = Analytical.SolarCalculator.Create.SeedOverhangDepth(field, target);
            output.WriteLine($"analytically seeded overhang depth from the field's zero crossing: {seed:0.###} m");
            Assert.False(double.IsNaN(seed), "the field must provide a seed depth for a south window");
            Assert.InRange(seed, 0.05, 1.5);

            IShadingTypology best = Analytical.SolarCalculator.Create.RationalisedShading(
                field, target, baseCache, desirability, new List<LinkedFace3D>(), "Overhang", out ShadingPerformance performance);

            Assert.NotNull(best);
            Assert.NotNull(performance);
            output.WriteLine($"chosen: {best.Name} depth {best.GetParameter("Depth"):0.###} m, extension {best.GetParameter("ExtensionBeyondJambs"):0.##} m");
            output.WriteLine($"   intercepted {performance.DirectSolarIntercepted:0.###} kWh, efficiency {performance.DirectShadingEfficiency * 100:0.#} %, unwanted blocked {performance.UnwantedSolarBlocked * 100:0.#} %, wanted retained {performance.WantedSolarRetained * 100:0.#} %");

            double bestScore = Analytical.SolarCalculator.Create.ShadingFitScore(performance);

            // The sweep must actually be choosing: an arbitrary very deep overhang scores worse.
            ShadingPerformance arbitrary = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, desirability, new List<LinkedFace3D>(), new Overhang(3.0));
            double arbitraryScore = Analytical.SolarCalculator.Create.ShadingFitScore(arbitrary);
            output.WriteLine($"   arbitrary 3.0 m overhang: unwanted blocked {arbitrary.UnwantedSolarBlocked * 100:0.#} %, wanted retained {arbitrary.WantedSolarRetained * 100:0.#} %, score {arbitraryScore:0.###} vs chosen {bestScore:0.###}");
            Assert.True(bestScore > arbitraryScore, "the seeded sweep must beat an arbitrary oversized overhang");

            // Rationalisation is deterministic.
            IShadingTypology again = Analytical.SolarCalculator.Create.RationalisedShading(
                field, target, baseCache, desirability, new List<LinkedFace3D>(), "Overhang", out ShadingPerformance _);
            Assert.Equal(best.GetParameter("Depth"), again.GetParameter("Depth"));
        }

        [Fact]
        public void Ideal_Versus_Rationalised_Is_Reported_Honestly()
        {
            // The headline Stage 8 comparison. The Stage 7 ideal shape is meshed from the field and
            // evaluated through exactly the same first-hit machinery as the practical device, so the
            // two numbers are comparable rather than one being a field statistic and the other a
            // ray-traced result.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, baseCache);
            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(target, maxDepth: 1.5, up: 1.0, down: 0.2, left: 0.5, right: 0.5, voxelSize: 0.1);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, baseCache, desirability, volume);

            // --- ideal: the BOUNDARY FACES of the selected voxel set.
            //
            // This used to be a thin horizontal plate at each voxel centre, described as the
            // closest ray-traceable stand-in. Gate 0 Review A measured that stand-in against the
            // voxel solid it stands in for and found it badly biased on this very case: 28 % less
            // direct solar intercepted and 21.5 points more wanted solar retained, because a
            // shallow ray crosses a voxel without ever meeting the single mid-height plate inside
            // it. The comparison below is the headline ideal-versus-rationalised number, so it now
            // uses the representation that actually means what the Stage 6 field means — the field
            // credits a voxel when a ray ENTERS it, and the boundary surface of the selected voxels
            // intercepts exactly those rays. See IdealRepresentationTests for the three-way
            // measurement.
            IdealShadingResult ideal = Analytical.SolarCalculator.Create.IdealShadingResult(
                field, ShadingThresholdMethod.CumulativeCapture, 0.9, 1.0, true, false);
            Assert.True(ideal.SelectedVoxelCount > 0);

            List<ShadingElement> idealElements = Analytical.SolarCalculator.Query.VoxelSurfaceShadingElements(volume, ideal.VoxelIndices);
            List<LinkedFace3D> idealOccluders = new List<LinkedFace3D>();
            foreach (ShadingElement element in idealElements)
            {
                idealOccluders.Add(element.LinkedFace3D);
            }

            SolarAttributionCache idealAttribution = Weather.SolarCalculator.Create.SolarAttributionCache(baseCache, idealOccluders, target.AnalysisCells);
            ShadingPerformance idealPerformance = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, idealAttribution, desirability, idealElements, "Ideal (Stage 7)", double.NaN);

            // --- rationalised
            IShadingTypology best = Analytical.SolarCalculator.Create.RationalisedShading(
                field, target, baseCache, desirability, new List<LinkedFace3D>(), "Overhang", out ShadingPerformance rationalised);

            output.WriteLine($"unshaded baseline: admitted direct {idealPerformance.AdmittedDirectEnergy:0.#} kWh, unwanted {idealPerformance.AdmittedUnwantedEnergy:0.#} kWh, wanted {idealPerformance.AdmittedWantedEnergy:0.#} kWh");
            output.WriteLine($"ideal (Stage 7, {idealElements.Count} boundary faces): intercepted {idealPerformance.DirectSolarIntercepted:0.#} kWh, efficiency {idealPerformance.DirectShadingEfficiency * 100:0.#} %, unwanted blocked {idealPerformance.UnwantedSolarBlocked * 100:0.#} %, wanted retained {idealPerformance.WantedSolarRetained * 100:0.#} %");
            output.WriteLine($"rationalised ({best.Name} {best.GetParameter("Depth"):0.##} m): intercepted {rationalised.DirectSolarIntercepted:0.#} kWh, efficiency {rationalised.DirectShadingEfficiency * 100:0.#} %, unwanted blocked {rationalised.UnwantedSolarBlocked * 100:0.#} %, wanted retained {rationalised.WantedSolarRetained * 100:0.#} %");
            output.WriteLine($"simplification cost: {(idealPerformance.UnwantedSolarBlocked - rationalised.UnwantedSolarBlocked) * 100:0.#} points of unwanted blocked, {(idealPerformance.WantedSolarRetained - rationalised.WantedSolarRetained) * 100:0.#} points of wanted retained");

            // Both must be real, bounded, conserving results. The comparison is REPORTED, not
            // asserted to favour rationalisation: a single overhang standing in for a free-form
            // solid can legitimately lose on one metric and win on the other, and bending the
            // measurement to make simplification look good would defeat the point of having it.
            Assert.InRange(idealPerformance.UnwantedSolarBlocked, 0.0, 1.0);
            Assert.InRange(rationalised.UnwantedSolarBlocked, 0.0, 1.0);
            Assert.Equal(idealPerformance.DirectSolarIntercepted, idealPerformance.ReconciledInterceptedEnergy, 6);
            Assert.Equal(rationalised.DirectSolarIntercepted, rationalised.ReconciledInterceptedEnergy, 6);

            // Both are shading the same aperture against the same sun, so both must block a
            // material share of the unwanted beam.
            Assert.True(idealPerformance.UnwantedSolarBlocked > 0.2);
            Assert.True(rationalised.UnwantedSolarBlocked > 0.2);
        }

    }
}
