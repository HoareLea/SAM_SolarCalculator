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
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Gate 0 Review D. Element identity across the buildable typologies, with the composed
    /// EggCrate — whose elements are re-issued from the parent rather than inherited from the child
    /// louvre and fin arrays — under particular scrutiny, because first-hit attribution credits
    /// energy by Guid and nothing downstream can recover from two elements sharing one.
    /// </summary>
    public class TypologyIdentityTests
    {
        private readonly ITestOutputHelper output;

        public TypologyIdentityTests(ITestOutputHelper output)
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

        private static ApertureSolarTarget Target(double gridSize = 0.5)
        {
            Face3D face = SouthWindowFace();
            return new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), face, Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize));
        }

        private static SolarVisibilityCache Cache(ApertureSolarTarget target, List<LinkedFace3D> occluders, double gridSize = 0.5)
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
        public void Every_Element_Of_Every_Candidate_Carries_A_Distinct_Stable_Guid()
        {
            // A sweep wide enough that an ordinal-only or parameter-blind Guid would collide:
            // EggCrate is asked for many louvre/fin splits at the same depth, and the same splits
            // are also asked of the bare louvre and fin families.
            ApertureSolarTarget target = Target();

            Dictionary<Guid, string> owner = new Dictionary<Guid, string>();
            int elements = 0;
            int collisions = 0;

            List<IShadingTypology> candidates = new List<IShadingTypology>();
            foreach (double depth in new double[] { 0.2, 0.4, 0.6 })
            {
                candidates.Add(new Overhang(depth, 0.0, 0.0));
                candidates.Add(new Overhang(depth, 0.1, 0.0));
                for (int count = 1; count <= 6; count++)
                {
                    candidates.Add(new HorizontalLouvres(depth, count, 0.0));
                    candidates.Add(new VerticalFins(depth, count, 0.0));
                }

                for (int louvres = 1; louvres <= 5; louvres++)
                {
                    for (int fins = 1; fins <= 5; fins++)
                    {
                        candidates.Add(new EggCrate(depth, louvres, fins));
                    }
                }
            }

            foreach (IShadingTypology candidate in candidates)
            {
                string label = candidate.Name;
                foreach (string name in candidate.ParameterNames)
                {
                    label += $" {name}={candidate.GetParameter(name):0.###}";
                }

                List<ShadingElement> shadingElements = candidate.ShadingElements(target);
                Assert.NotNull(shadingElements);
                Assert.NotEmpty(shadingElements);

                foreach (ShadingElement element in shadingElements)
                {
                    elements++;
                    Assert.True(element.Area > 0, $"{label} produced a degenerate element");
                    if (owner.TryGetValue(element.Guid, out string previous))
                    {
                        collisions++;
                        output.WriteLine($"COLLISION {element.Guid} shared by [{previous}] and [{label} / {element.Name}]");
                    }
                    else
                    {
                        owner[element.Guid] = label + " / " + element.Name;
                    }
                }
            }

            output.WriteLine($"{candidates.Count} candidates, {elements} elements, {owner.Count} distinct Guids, {collisions} collisions");
            Assert.Equal(0, collisions);
            Assert.Equal(elements, owner.Count);
        }

        [Fact]
        public void Rebuilding_A_Logically_Unchanged_Device_Reproduces_Its_Identity()
        {
            ApertureSolarTarget target = Target();

            // Same parameters via a different route: constructed fresh, and reached by SetParameter
            // from a different starting point. Both must give the identical element table.
            EggCrate direct = new EggCrate(0.35, 4, 2);

            EggCrate mutated = new EggCrate(0.9, 1, 5);
            Assert.True(mutated.SetParameter("Depth", 0.35));
            Assert.True(mutated.SetParameter("LouvreCount", 4));
            Assert.True(mutated.SetParameter("FinCount", 2));

            List<ShadingElement> a = direct.ShadingElements(target);
            List<ShadingElement> b = mutated.ShadingElements(target);
            List<ShadingElement> c = direct.ShadingElements(target);

            Assert.Equal(a.Count, b.Count);
            Assert.Equal(a.Count, c.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Guid, b[i].Guid);
                Assert.Equal(a[i].Guid, c[i].Guid);
                Assert.Equal(a[i].Name, b[i].Name);
                Assert.Equal(a[i].Area, b[i].Area, 12);
            }

            output.WriteLine($"EggCrate(0.35, 4, 2): {a.Count} elements, identity reproduced from a fresh build and from a mutated one");

            // And a real parameter change MUST move the identity, or a stale attribution cache
            // would silently be reused against different geometry.
            EggCrate deeper = new EggCrate(0.36, 4, 2);
            List<ShadingElement> d = deeper.ShadingElements(target);
            Assert.NotEqual(a[0].Guid, d[0].Guid);

            string hashA = SolarAttributionCache.ComputeAttributionTableHash(Guids(a));
            string hashD = SolarAttributionCache.ComputeAttributionTableHash(Guids(d));
            output.WriteLine($"table hash 0.35 m {hashA.Substring(0, 16)}...  0.36 m {hashD.Substring(0, 16)}...");
            Assert.NotEqual(hashA, hashD);
        }

        private static List<Guid> Guids(List<ShadingElement> elements)
        {
            List<Guid> result = new List<Guid>();
            foreach (ShadingElement element in elements)
            {
                result.Add(element.Guid);
            }

            return result;
        }

        [Fact]
        public void Crossing_Louvres_And_Fins_Attribute_Without_Ambiguity_Or_Double_Counting()
        {
            // The EggCrate is the one family whose elements PHYSICALLY INTERSECT: every louvre
            // crosses every fin. First-hit attribution must still hand each intercepted ray to
            // exactly one element, so the per-element sum plus the residual reconciles exactly with
            // the total intercepted energy — no ray counted twice at a crossing.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, baseCache);

            EggCrate eggCrate = new EggCrate(0.5, 4, 4);
            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, desirability, new List<LinkedFace3D>(), eggCrate);

            Assert.NotNull(performance);

            Dictionary<Guid, double> perElement = performance.EnergyPerElement;
            double sum = 0;
            int credited = 0;
            foreach (KeyValuePair<Guid, double> pair in perElement)
            {
                sum += pair.Value;
                if (pair.Value > 0)
                {
                    credited++;
                }
            }

            output.WriteLine($"EggCrate(0.5, 4, 4): {perElement.Count} elements, {credited} credited with energy");
            output.WriteLine($"   intercepted {performance.DirectSolarIntercepted:0.######} kWh, per-element sum {sum:0.######} kWh, unattributed {performance.UnattributedInterceptedEnergy:0.######} kWh");

            // Conservation, to the numerical noise floor.
            Assert.Equal(performance.DirectSolarIntercepted, performance.ReconciledInterceptedEnergy, 9);

            // Nothing may leak to a non-candidate: there is no context here at all.
            Assert.Equal(0.0, performance.UnattributedInterceptedEnergy);

            // The crossings are real, so more than one element must actually be credited —
            // otherwise this proves nothing about overlap.
            Assert.True(credited > 1, "an egg crate must spread intercepted energy over several elements");

            // Every credited element is one of the device's own, and each appears exactly once.
            HashSet<Guid> deviceGuids = new HashSet<Guid>(Guids(eggCrate.ShadingElements(target)));
            Assert.Equal(deviceGuids.Count, perElement.Count);
            foreach (Guid guid in perElement.Keys)
            {
                Assert.Contains(guid, deviceGuids);
            }
        }

        [Fact]
        public void An_EggCrate_Intercepts_The_Union_Of_Its_Louvres_And_Fins()
        {
            // The composed device must behave as the union of its parts. Re-issuing Guids changes
            // IDENTITY, and it must not change PHYSICS: the same faces are in the ray path either
            // way, so the total intercepted energy has to match a hand-assembled element list built
            // from the bare louvre and fin arrays.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, baseCache);

            double depth = 0.45;
            EggCrate eggCrate = new EggCrate(depth, 3, 3);

            List<ShadingElement> union = new List<ShadingElement>();
            union.AddRange(new HorizontalLouvres(depth, 3, 0.0).ShadingElements(target));
            union.AddRange(new VerticalFins(depth, 3, 0.0).ShadingElements(target));

            List<LinkedFace3D> occluders = new List<LinkedFace3D>();
            foreach (ShadingElement element in union)
            {
                occluders.Add(element.LinkedFace3D);
            }

            SolarAttributionCache attribution = Weather.SolarCalculator.Create.SolarAttributionCache(baseCache, occluders, target.AnalysisCells);
            ShadingPerformance unionPerformance = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, attribution, desirability, union, "Union", double.NaN);

            ShadingPerformance composed = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, desirability, new List<LinkedFace3D>(), eggCrate);

            output.WriteLine($"union of parts: intercepted {unionPerformance.DirectSolarIntercepted:0.######} kWh, unwanted {unionPerformance.UnwantedSolarIntercepted:0.######} kWh");
            output.WriteLine($"composed crate: intercepted {composed.DirectSolarIntercepted:0.######} kWh, unwanted {composed.UnwantedSolarIntercepted:0.######} kWh");

            Assert.Equal(unionPerformance.DirectSolarIntercepted, composed.DirectSolarIntercepted, 9);
            Assert.Equal(unionPerformance.UnwantedSolarIntercepted, composed.UnwantedSolarIntercepted, 9);
            Assert.Equal(unionPerformance.WantedSolarBlocked, composed.WantedSolarBlocked, 9);

            // The identities differ by design, which is the whole point of re-issuing them.
            HashSet<Guid> unionGuids = new HashSet<Guid>(Guids(union));
            foreach (Guid guid in Guids(eggCrate.ShadingElements(target)))
            {
                Assert.DoesNotContain(guid, unionGuids);
            }
        }

        [Fact]
        public void EggCrate_Material_Fraction_Is_Additive_Because_The_Blades_Meet_In_Lines()
        {
            // The obvious suspicion about a composed typology is that its material measure
            // double-counts where the children overlap. It does not, and the reason is geometric
            // rather than lucky: the shading elements are PLATES, not solids. A horizontal louvre
            // occupies a fixed up-slope height across the full width and depth; a vertical fin
            // occupies a fixed across-facade position over the full height and depth. Two
            // perpendicular plates meet in a LINE, which has no area, so summing the element areas
            // counts every square metre exactly once.
            //
            // Verified below against the closed-form areas rather than asserted, because the
            // conclusion is what licenses Stage 9 to use MaterialFraction as a cost term across
            // families without an overlap correction.
            ApertureSolarTarget target = Target();

            double depth = 0.5;
            EggCrate crate = new EggCrate(depth, 3, 3);
            double crateFraction = crate.MaterialFraction(target);

            double louvreFraction = new HorizontalLouvres(depth, 3, 0.0).MaterialFraction(target);
            double finFraction = new VerticalFins(depth, 3, 0.0).MaterialFraction(target);

            output.WriteLine($"material fraction: louvres {louvreFraction:0.####}, fins {finFraction:0.####}, sum {louvreFraction + finFraction:0.####}, crate {crateFraction:0.####}");
            Assert.Equal(louvreFraction + finFraction, crateFraction, 9);

            // Closed form for this 2.0 m x 1.0 m window: 3 louvres of 2.0 x depth, 3 fins of
            // 1.0 x depth, over a 2.0 m2 aperture.
            double expected = (3 * 2.0 * depth + 3 * 1.0 * depth) / 2.0;
            output.WriteLine($"closed-form material fraction {expected:0.####} vs measured {crateFraction:0.####}");
            Assert.Equal(expected, crateFraction, 9);

            // The crossings are physically real — every louvre meets every fin — but they
            // contribute no area, which is the point.
            List<ShadingElement> louvres = new HorizontalLouvres(depth, 3, 0.0).ShadingElements(target);
            List<ShadingElement> fins = new VerticalFins(depth, 3, 0.0).ShadingElements(target);
            double louvreArea = 0, finArea = 0;
            foreach (ShadingElement element in louvres) { louvreArea += element.Area; }
            foreach (ShadingElement element in fins) { finArea += element.Area; }
            output.WriteLine($"{louvres.Count} louvres of {louvreArea / louvres.Count:0.###} m2 crossing {fins.Count} fins of {finArea / fins.Count:0.###} m2 — {louvres.Count * fins.Count} line crossings, 0 m2 of shared area");

            Assert.Equal(3 * 2.0 * depth, louvreArea, 9);
            Assert.Equal(3 * 1.0 * depth, finArea, 9);
        }
    }
}
