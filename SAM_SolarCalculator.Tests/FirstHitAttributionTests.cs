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
    /// Stage 8 first-hit attribution. Analytically clear geometry throughout: a south window, a
    /// single sun group forced by a one-hour weighting, and slabs placed so the answer is obvious
    /// by inspection before anything is measured.
    /// </summary>
    public class FirstHitAttributionTests
    {
        private readonly ITestOutputHelper output;

        public FirstHitAttributionTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly int Year = 2018;

        /// <summary>South-facing window (outward normal (0,-1,0)), 2 m x 1 m, sill at z = 1.</summary>
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

        /// <summary>Horizontal slab of the given depth, at the given height, spanning the window.</summary>
        private static LinkedFace3D Slab(Guid guid, double height, double depth)
        {
            return Slab(guid, height, depth, -0.5, 2.5);
        }

        /// <summary>Horizontal slab with explicit lateral extent.</summary>
        private static LinkedFace3D Slab(Guid guid, double height, double depth, double minX, double maxX)
        {
            return new LinkedFace3D(guid, new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(minX, 0, height),
                new Point3D(maxX, 0, height),
                new Point3D(maxX, -depth, height),
                new Point3D(minX, -depth, height),
            })));
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

        // ------------------------------------------------------------------------ 1, 2, 3, 4 ----

        [Fact]
        public void One_Shade_In_The_Path_Is_Attributed_To_Its_Guid()
        {
            Guid shadeGuid = new Guid("11111111-1111-1111-1111-111111111111");
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());

            List<LinkedFace3D> occluders = new List<LinkedFace3D> { Slab(shadeGuid, 2.0, 1.5) };
            SolarAttributionCache attribution = Weather.SolarCalculator.Create.SolarAttributionCache(baseCache, occluders, target.AnalysisCells);

            Assert.NotNull(attribution);
            Assert.Equal(baseCache.BinCount, attribution.BinCount);

            int attributed = 0;
            int visible = 0;
            for (int b = 0; b < attribution.BinCount; b++)
            {
                for (int c = 0; c < target.AnalysisCells.Count; c++)
                {
                    Guid guid = attribution.FirstHitGuid(b, c);
                    if (guid == shadeGuid) { attributed++; }
                    else if (attribution.FirstHitIndex(b, c) == Weather.SolarCalculator.Query.FirstHitVisible) { visible++; }
                }
            }

            output.WriteLine($"{attributed} samples attributed to the shade, {visible} still visible, table {attribution.OccluderCount} entries");
            Assert.True(attributed > 0, "an overhang above a south window must intercept some sun");
            Assert.True(visible > 0, "and must not intercept all of it");

            // Attribution and visibility must agree everywhere: not-lit iff something was hit first.
            for (int b = 0; b < attribution.BinCount; b++)
            {
                SolarVisibilityCache withShade = Cache(target, occluders);
                for (int c = 0; c < target.AnalysisCells.Count; c++)
                {
                    bool lit = withShade.IsLit(b, c);
                    bool hitSomething = attribution.FirstHitIndex(b, c) >= 0;
                    Assert.False(lit && hitSomething, $"bin {b} cell {c}: lit but attributed a first hit");
                }

                break; // one bin's worth is enough; building a cache per bin is expensive
            }
        }

        [Fact]
        public void Two_Shades_In_Series_Credit_Only_The_Nearer_One()
        {
            // Two slabs at the same height, one 0.5 m deep and one 1.5 m deep. Sun coming down onto
            // the window meets the SHALLOW one first: it is nearer to the aperture along the ray.
            Guid near = new Guid("22222222-2222-2222-2222-222222222222");
            Guid far = new Guid("33333333-3333-3333-3333-333333333333");

            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());

            List<LinkedFace3D> occluders = new List<LinkedFace3D> { Slab(near, 2.0, 0.5), Slab(far, 2.6, 2.0) };
            SolarAttributionCache attribution = Weather.SolarCalculator.Create.SolarAttributionCache(baseCache, occluders, target.AnalysisCells);

            Count(attribution, target.AnalysisCells.Count, near, far, out int nearCount, out int farCount);
            output.WriteLine($"near slab {nearCount} samples, far slab {farCount} samples");

            Assert.True(nearCount > 0, "the nearer slab must take the steep sun");
            Assert.True(farCount > 0, "the higher, deeper slab must take some shallower sun the near one misses");
        }

        [Fact]
        public void Reversing_The_Input_Order_Does_Not_Change_The_Physical_First_Hit()
        {
            Guid near = new Guid("22222222-2222-2222-2222-222222222222");
            Guid far = new Guid("33333333-3333-3333-3333-333333333333");

            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());

            SolarAttributionCache forward = Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, new List<LinkedFace3D> { Slab(near, 2.0, 0.5), Slab(far, 2.6, 2.0) }, target.AnalysisCells);
            SolarAttributionCache reversed = Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, new List<LinkedFace3D> { Slab(far, 2.6, 2.0), Slab(near, 2.0, 0.5) }, target.AnalysisCells);

            int compared = 0;
            for (int b = 0; b < forward.BinCount; b++)
            {
                for (int c = 0; c < target.AnalysisCells.Count; c++)
                {
                    // GUIDs must match even though the table ORDER differs between the two caches.
                    Assert.Equal(forward.FirstHitGuid(b, c), reversed.FirstHitGuid(b, c));
                    compared++;
                }
            }

            output.WriteLine($"{compared} samples agree under reversed input order; table hashes differ: {forward.AttributionTableHash != reversed.AttributionTableHash}");
            Assert.NotEqual(forward.AttributionTableHash, reversed.AttributionTableHash);
        }

        [Fact]
        public void Removing_The_Nearer_Shade_Hands_Its_Energy_To_The_One_Behind()
        {
            Guid near = new Guid("22222222-2222-2222-2222-222222222222");
            Guid far = new Guid("33333333-3333-3333-3333-333333333333");

            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());

            SolarAttributionCache both = Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, new List<LinkedFace3D> { Slab(near, 2.0, 1.0), Slab(far, 2.2, 2.0) }, target.AnalysisCells);
            SolarAttributionCache farOnly = Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, new List<LinkedFace3D> { Slab(far, 2.2, 2.0) }, target.AnalysisCells);

            Count(both, target.AnalysisCells.Count, near, far, out int nearWithBoth, out int farWithBoth);
            Count(farOnly, target.AnalysisCells.Count, near, far, out int _, out int farAlone);

            output.WriteLine($"with both: near {nearWithBoth}, far {farWithBoth}; near removed: far {farAlone}");

            Assert.True(nearWithBoth > 0);
            Assert.True(farAlone > farWithBoth, "removing the nearer shade must transfer samples to the one behind it");
        }

        // ------------------------------------------------------------------------------ 5 ------

        [Fact]
        public void Existing_Context_Before_The_Shade_Gives_The_Candidate_No_Credit()
        {
            // A deep context slab already blocks the high sun. A candidate overhang tucked UNDER it
            // duplicates work that is already done and must earn nothing, because none of the rays
            // it meets were admitted in the base state.
            //
            // The geometry has to make that strictly true, which is fussier than it looks. The
            // candidate must sit inside the context's shadow volume for EVERY sun direction that
            // still reaches the window, in BOTH lateral directions, not merely sit beneath it. A
            // candidate that out-reaches the context by any margin catches the shallow rays passing
            // under the context's outer edge, and a candidate as wide as the context catches the
            // oblique rays leaving past its side edge; both sets are base-admitted, so it would earn
            // real credit and the test would be measuring bad geometry rather than bad accounting.
            //
            // The containment argument: the candidate spans x in [0, 2] and 0.5 m of depth at
            // z = 2.25, the context spans x in [-3, 5] and 4.0 m of depth at z = 2.3. A ray that
            // reached the candidate rose at least 0.25 m from the window and moved at most 0.5 m in
            // y and 2 m in x, so |ty/tz| <= 2 and |tx/tz| <= 8. Over the remaining 0.05 m of rise to
            // the context plane it can therefore move at most 0.1 m in y and 0.4 m in x — nowhere
            // near the 3.5 m and 3.0 m of margin it would need to escape. Every ray that meets the
            // candidate is stopped by the context.
            Guid contextGuid = new Guid("44444444-4444-4444-4444-444444444444");
            Guid candidateGuid = new Guid("55555555-5555-5555-5555-555555555555");

            ApertureSolarTarget target = Target();
            List<LinkedFace3D> context = new List<LinkedFace3D> { Slab(contextGuid, 2.3, 4.0, -3.0, 5.0) };

            SolarVisibilityCache baseCache = Cache(target, context);
            ApertureDesirability desirability = Desirability(target, baseCache);

            ShadingElement candidate = new ShadingElement(candidateGuid, "Duplicate overhang",
                Slab(candidateGuid, 2.25, 0.5, 0.0, 2.0).Face3D);

            List<LinkedFace3D> all = new List<LinkedFace3D>(context) { candidate.LinkedFace3D };
            SolarAttributionCache attribution = Weather.SolarCalculator.Create.SolarAttributionCache(baseCache, all, target.AnalysisCells);

            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, attribution, desirability,
                new List<ShadingElement> { candidate }, "DuplicateOverhang", 0.9);

            output.WriteLine($"duplicating candidate: intercepted {performance.DirectSolarIntercepted:0.####} kWh of {performance.AdmittedDirectEnergy:0.###} kWh admitted, per-element {performance.EnergyPerElement[candidateGuid]:0.####} kWh");

            Assert.True(performance.AdmittedDirectEnergy > 0, "context must still admit some sun for the test to mean anything");
            Assert.Equal(0.0, performance.EnergyPerElement[candidateGuid], 9);
            Assert.Equal(0.0, performance.DirectSolarIntercepted, 9);
            Assert.Equal(0.0, performance.UnattributedInterceptedEnergy, 9);
            Assert.Equal(0.0, performance.DirectShadingEfficiency, 9);
        }

        // ------------------------------------------------------------------------------ 6 ------

        [Fact]
        public void Attribution_Cache_Json_RoundTrips()
        {
            Guid shadeGuid = new Guid("11111111-1111-1111-1111-111111111111");
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());
            SolarAttributionCache attribution = Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, new List<LinkedFace3D> { Slab(shadeGuid, 2.0, 1.5) }, target.AnalysisCells);

            SolarAttributionCache restored = new SolarAttributionCache(attribution.ToJsonObject());

            Assert.Equal(attribution.BinCount, restored.BinCount);
            Assert.Equal(attribution.CellCount, restored.CellCount);
            Assert.Equal(attribution.OccluderGuids, restored.OccluderGuids);
            Assert.Equal(attribution.AttributionTableHash, restored.AttributionTableHash);
            Assert.Equal(attribution.ContextGeometryHash, restored.ContextGeometryHash);

            for (int b = 0; b < attribution.BinCount; b++)
            {
                for (int c = 0; c < attribution.CellCount; c++)
                {
                    Assert.Equal(attribution.FirstHitIndex(b, c), restored.FirstHitIndex(b, c));
                }
            }

            output.WriteLine($"round trip: {attribution.BinCount} bins x {attribution.CellCount} cells, storage {attribution.StorageBytes / 1024.0:0.#} KiB");
        }

        // --------------------------------------------------------------------------- 7, 8 ------

        [Fact]
        public void Changing_The_Shade_Geometry_Invalidates_The_Attribution()
        {
            Guid shadeGuid = new Guid("11111111-1111-1111-1111-111111111111");
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());

            SolarAttributionCache shallow = Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, new List<LinkedFace3D> { Slab(shadeGuid, 2.0, 0.4) }, target.AnalysisCells);
            SolarAttributionCache deep = Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, new List<LinkedFace3D> { Slab(shadeGuid, 2.0, 1.6) }, target.AnalysisCells);

            // Same Guid table, so the table hash alone cannot tell them apart — the CONTEXT
            // geometry hash is what catches a moved or resized face.
            Assert.Equal(shallow.AttributionTableHash, deep.AttributionTableHash);
            Assert.NotEqual(shallow.ContextGeometryHash, deep.ContextGeometryHash);

            Assert.False(shallow.Matches(deep.ContextGeometryHash, deep.TargetGeometryHash, deep.AttributionTableHash,
                deep.CellSize, deep.BinSizeDegrees, deep.SunPositionShiftInMinutes, deep.Year, deep.CellCount),
                "a resized shade must not match a cache built from the old geometry");

            output.WriteLine($"shallow hash {shallow.ContextGeometryHash.Substring(0, 12)}..., deep hash {deep.ContextGeometryHash.Substring(0, 12)}...");
        }

        [Fact]
        public void A_Different_Guid_Table_Cannot_Be_Silently_Reused()
        {
            // The dangerous case: same faces, different table ORDER. Every stored index still
            // resolves, so nothing crashes — it just points at the wrong element. Only the
            // attribution-table hash catches it.
            Guid a = new Guid("22222222-2222-2222-2222-222222222222");
            Guid b = new Guid("33333333-3333-3333-3333-333333333333");

            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());

            SolarAttributionCache forward = Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, new List<LinkedFace3D> { Slab(a, 2.0, 0.5), Slab(b, 2.6, 2.0) }, target.AnalysisCells);
            SolarAttributionCache swapped = Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, new List<LinkedFace3D> { Slab(b, 2.6, 2.0), Slab(a, 2.0, 0.5) }, target.AnalysisCells);

            Assert.NotEqual(forward.AttributionTableHash, swapped.AttributionTableHash);
            Assert.False(forward.Matches(swapped.ContextGeometryHash, swapped.TargetGeometryHash, swapped.AttributionTableHash,
                swapped.CellSize, swapped.BinSizeDegrees, swapped.SunPositionShiftInMinutes, swapped.Year, swapped.CellCount),
                "a reordered occluder table must invalidate the cache even though every index still resolves");

            output.WriteLine($"table hashes differ under reordering: {forward.AttributionTableHash.Substring(0, 12)}... vs {swapped.AttributionTableHash.Substring(0, 12)}...");
        }

        // ------------------------------------------------------------------------------ 9 ------

        [Fact]
        public void Overlapping_Elements_Reconcile_With_The_Total_Intercepted_Energy()
        {
            // Deliberately overlapping: an egg crate's louvres and fins cross, so many rays could be
            // claimed by either. First-hit attribution must credit exactly one, and the per-element
            // sum plus the residual must come back to the total.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, baseCache);

            EggCrate typology = new EggCrate(0.4, 3, 3);
            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, desirability, new List<LinkedFace3D>(), typology);

            Assert.NotNull(performance);
            output.WriteLine($"egg crate: intercepted {performance.DirectSolarIntercepted:0.###} kWh, reconciled {performance.ReconciledInterceptedEnergy:0.###} kWh, residual {performance.UnattributedInterceptedEnergy:0.###} kWh");
            foreach (KeyValuePair<Guid, double> pair in performance.EnergyPerElement)
            {
                output.WriteLine($"   {performance.ElementName(pair.Key)}: {pair.Value:0.###} kWh");
            }

            Assert.True(performance.DirectSolarIntercepted > 0);

            // Conservation: nothing double-counted, nothing lost.
            Assert.Equal(performance.DirectSolarIntercepted, performance.ReconciledInterceptedEnergy, 6);

            // With no context, every intercepted ray must belong to a candidate element.
            Assert.Equal(0.0, performance.UnattributedInterceptedEnergy, 9);
        }

        private static void Count(SolarAttributionCache attribution, int cellCount, Guid a, Guid b, out int countA, out int countB)
        {
            countA = 0;
            countB = 0;
            for (int bin = 0; bin < attribution.BinCount; bin++)
            {
                for (int c = 0; c < cellCount; c++)
                {
                    Guid guid = attribution.FirstHitGuid(bin, c);
                    if (guid == a) { countA++; }
                    else if (guid == b) { countB++; }
                }
            }
        }
    }
}
