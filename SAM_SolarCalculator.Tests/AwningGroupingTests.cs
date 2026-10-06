// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The deterministic aperture grouping behind one-physical-awning-spans-several-windows.
    ///
    /// Grouping is pure geometry — no weather, no ray tracing — so these tests are fast. The
    /// contract they pin: input order never changes the answer, group identity is deterministic,
    /// eligibility is the product of wall / plane / normal / head / gap / width rules, and splits
    /// never invent a modular connection between units.
    /// </summary>
    public class AwningGroupingTests
    {
        private readonly ITestOutputHelper output;

        public AwningGroupingTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly Guid PanelA = new Guid("aaaa1111-0000-0000-0000-000000000001");
        private static readonly Guid PanelB = new Guid("bbbb2222-0000-0000-0000-000000000002");

        /// <summary>
        /// A south-facing aperture on the y = 0 plane, x across [x0, x0 + width], z up
        /// [sill, sill + height]. The winding gives the outward +Y normal.
        /// </summary>
        private static ApertureSolarTarget Aperture(Guid panelGuid, Guid apertureGuid, double x0, double width, double sill, double height, bool reversedWinding = false)
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(x0, 0, sill),
                new Point3D(x0 + width, 0, sill),
                new Point3D(x0 + width, 0, sill + height),
                new Point3D(x0, 0, sill + height),
            };

            if (reversedWinding)
            {
                points.Reverse();
            }

            return new ApertureSolarTarget(apertureGuid, panelGuid, new Face3D(new Polygon3D(points)), null);
        }

        private static Guid G(int ordinal)
        {
            return new Guid($"cccc3333-0000-0000-0000-000000000{ordinal:D3}");
        }

        // ------------------------------------------------------------------- eligibility ----

        [Fact]
        public void Adjacent_Same_Panel_Same_Head_Apertures_Form_One_Group()
        {
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 0.9, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 0.9, 1.2, 1.0, 2.25);
            ApertureSolarTarget c = Aperture(PanelA, G(3), 2.1, 0.6, 1.0, 2.25);

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b, c }.ApertureShadingGroups();

            Assert.Single(groups);
            Assert.Equal(new List<Guid> { G(1), G(2), G(3) }, groups[0].ApertureGuids);
            Assert.Equal(PanelA, groups[0].PanelGuid);
            Assert.Equal(2.7, groups[0].Width, 9);
        }

        [Fact]
        public void Input_Order_Does_Not_Change_Membership_Order_Or_Group_Guid()
        {
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 0.9, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 0.9, 1.2, 1.0, 2.25);
            ApertureSolarTarget c = Aperture(PanelA, G(3), 2.1, 0.6, 1.0, 2.25);

            List<ApertureShadingGroup> forward = new List<ApertureSolarTarget> { a, b, c }.ApertureShadingGroups(AwningSpecification.Dakar, 0.15);
            List<ApertureShadingGroup> scrambled = new List<ApertureSolarTarget> { c, a, b }.ApertureShadingGroups(AwningSpecification.Dakar, 0.15);
            List<ApertureShadingGroup> reversed = new List<ApertureSolarTarget> { c, b, a }.ApertureShadingGroups(AwningSpecification.Dakar, 0.15);

            Assert.Single(forward);
            Assert.Single(scrambled);
            Assert.Single(reversed);

            Assert.Equal(forward[0].GroupGuid, scrambled[0].GroupGuid);
            Assert.Equal(forward[0].GroupGuid, reversed[0].GroupGuid);
            Assert.Equal(forward[0].ApertureGuids, scrambled[0].ApertureGuids);
            Assert.Equal(forward[0].ApertureGuids, reversed[0].ApertureGuids);
            Assert.Equal(new List<Guid> { G(1), G(2), G(3) }, forward[0].ApertureGuids);
        }

        [Fact]
        public void Different_Panels_Do_Not_Group_Automatically()
        {
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 0.9, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelB, G(2), 0.9, 1.2, 1.0, 2.25);

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b }.ApertureShadingGroups();

            Assert.Equal(2, groups.Count);
            Assert.Single(groups[0].ApertureGuids);
            Assert.Single(groups[1].ApertureGuids);
            Assert.Equal(PanelA, groups[0].PanelGuid);
            Assert.Equal(PanelB, groups[1].PanelGuid);
        }

        [Fact]
        public void Opposite_Normals_Do_Not_Group()
        {
            // Physically coplanar and adjacent, but one aperture's winding faces the other way:
            // the resolved outward normals disagree, so one awning cannot serve both.
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 0.9, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 0.9, 1.2, 1.0, 2.25, reversedWinding: true);

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b }.ApertureShadingGroups();

            Assert.Equal(2, groups.Count);
            Assert.Single(groups[0].ApertureGuids);
            Assert.Single(groups[1].ApertureGuids);
        }

        [Fact]
        public void Head_Difference_Above_Tolerance_Splits_Groups()
        {
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 0.9, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 0.9, 1.2, 0.8, 2.25); // head 0.2 m lower

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b }.ApertureShadingGroups(AwningSpecification.Dakar, 0.0, 0.20, 0.02);

            Assert.Equal(2, groups.Count);
        }

        [Fact]
        public void Head_Difference_Within_Tolerance_Stays_One_Group()
        {
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 0.9, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 0.9, 1.2, 0.99, 2.26); // head ~0.01 m different

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b }.ApertureShadingGroups(AwningSpecification.Dakar, 0.0, 0.20, 0.02);

            Assert.Single(groups);
        }

        [Fact]
        public void Gap_Above_Tolerance_Splits_Groups()
        {
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 0.9, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 1.4, 1.2, 1.0, 2.25); // 0.5 m gap

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b }.ApertureShadingGroups(AwningSpecification.Dakar, 0.0, 0.20, 0.02);

            Assert.Equal(2, groups.Count);
        }

        [Fact]
        public void Width_Above_The_Product_Maximum_Splits_At_The_Largest_Gap()
        {
            // 2.5 + 2.0 + 2.5 m of aperture with a 0.1 m gap then a 0.18 m gap: one awning is
            // impossible (7.18 m total), and the deterministic split prefers the LARGEST gap —
            // between the second and third window — leaving [2.5, 2.0] + [2.5].
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 2.5, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 2.6, 2.0, 1.0, 2.25);
            ApertureSolarTarget c = Aperture(PanelA, G(3), 4.78, 2.5, 1.0, 2.25);

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b, c }.ApertureShadingGroups(AwningSpecification.Dakar, 0.0);

            Assert.Equal(2, groups.Count);

            // Output order is by deterministic group GUID, not geometry: assert membership sets.
            List<List<Guid>> memberships = groups.ConvertAll(x => x.ApertureGuids);
            foreach (List<Guid> membership in memberships)
            {
                output.WriteLine($"membership: {string.Join(", ", membership)}");
            }

            Assert.True(memberships.Any(x => x.SequenceEqual(new List<Guid> { G(1), G(2) })), "the split must prefer the largest gap: the two left windows share one unit");
            Assert.True(memberships.Any(x => x.SequenceEqual(new List<Guid> { G(3) })), "the right window becomes its own unit");

            foreach (ApertureShadingGroup group in groups)
            {
                Assert.True(group.Width <= 6.0 + 1e-9, $"group width {group.Width} must respect the product maximum");
            }
        }

        [Fact]
        public void Width_Split_Falls_Back_To_A_Stable_Greedy_Cut_When_The_Largest_Gap_Would_Break_A_Unit()
        {
            // [4.5, 4.5, 4.5] with a 0.2 m gap before the last: the largest-gap split would leave
            // [4.5, 4.5] = 9.0 m on the left — over the maximum — so the greedy widest-valid cut
            // applies: three single-aperture units.
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 4.5, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 4.5, 4.5, 1.0, 2.25);
            ApertureSolarTarget c = Aperture(PanelA, G(3), 9.2, 4.5, 1.0, 2.25);

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b, c }.ApertureShadingGroups(AwningSpecification.Dakar, 0.0);

            Assert.Equal(3, groups.Count);
            foreach (ApertureShadingGroup group in groups)
            {
                Assert.Single(group.ApertureGuids);
                Assert.Contains(group.ApertureGuids[0], new List<Guid> { G(1), G(2), G(3) });
            }
        }

        [Fact]
        public void A_Single_Aperture_Is_A_Valid_One_Member_Group()
        {
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 0.9, 1.0, 2.25);

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a }.ApertureShadingGroups();

            Assert.Single(groups);
            Assert.Equal(new List<Guid> { G(1) }, groups[0].ApertureGuids);
            Assert.Equal(0.9, groups[0].Width, 9);
        }

        // ------------------------------------------------------------------ shared geometry ----

        [Fact]
        public void One_Group_Builds_One_Shared_Canopy_Not_One_Canopy_Per_Window()
        {
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 0.9, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 0.9, 1.2, 1.0, 2.25);
            ApertureSolarTarget c = Aperture(PanelA, G(3), 2.1, 0.6, 1.0, 2.25);

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b, c }.ApertureShadingGroups(AwningSpecification.Dakar, 0.15);
            ApertureShadingGroup group = groups[0];

            RetractableAwning awning = new RetractableAwning(2.6, 15.0, 0.0, 0.15, 0.0);
            List<ShadingElement> elements = awning.ShadingElements(group.Plane, group.MinX, group.MaxX, group.MaxY);

            // ONE canopy face for the WHOLE group — never one centred on each window.
            Assert.Single(elements);
            Assert.Equal("RetractableAwning_Canopy", elements[0].Name);

            // The canopy spans the full group width plus both extensions, in the group frame.
            Geometry.Planar.BoundingBox2D boundingBox2D = group.Plane.Convert(elements[0].Face3D)?.GetBoundingBox();
            Assert.NotNull(boundingBox2D);
            Assert.Equal(group.MinX - 0.15, boundingBox2D.Min.X, 9);
            Assert.Equal(group.MaxX + 0.15, boundingBox2D.Max.X, 9);

            // And the face contains its four corners at the facade (z = 0) and at the projection (z = 2.6).
            double projection = 2.6;
            Face3D canopy = elements[0].Face3D;
            Assert.True(canopy.InRange(Point(group.Plane, boundingBox2D.Min.X, boundingBox2D.Max.Y, 0.0), 1e-6));
            Assert.True(canopy.InRange(Point(group.Plane, boundingBox2D.Max.X, boundingBox2D.Max.Y, 0.0), 1e-6));
            Assert.True(canopy.InRange(Point(group.Plane, boundingBox2D.Max.X, boundingBox2D.Min.Y, projection), 1e-6));
            Assert.True(canopy.InRange(Point(group.Plane, boundingBox2D.Min.X, boundingBox2D.Min.Y, projection), 1e-6));
        }

        private static Point3D Point(Plane plane, double x, double y, double z)
        {
            return new Point3D(
                plane.Origin.X + x * plane.AxisX.X + y * plane.AxisY.X + z * plane.Normal.X,
                plane.Origin.Y + x * plane.AxisX.Y + y * plane.AxisY.Y + z * plane.Normal.Y,
                plane.Origin.Z + x * plane.AxisX.Z + y * plane.AxisY.Z + z * plane.Normal.Z);
        }

        [Fact]
        public void Group_Json_Round_Trip_Preserves_Identity_And_Members()
        {
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 0.9, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 0.9, 1.2, 1.0, 2.25);

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b }.ApertureShadingGroups();
            ApertureShadingGroup group = groups[0];

            ApertureShadingGroup reloaded = Core.Create.IJSAMObject<ApertureShadingGroup>(group.ToJsonObject().ToJsonString());

            Assert.NotNull(reloaded);
            Assert.Equal(group.GroupGuid, reloaded.GroupGuid);
            Assert.Equal(group.PanelGuid, reloaded.PanelGuid);
            Assert.Equal(group.ApertureGuids, reloaded.ApertureGuids);
            Assert.Equal(group.Width, reloaded.Width, 9);
            Assert.Equal(group.TotalGrossArea, reloaded.TotalGrossArea, 9);
        }

        // ------------------------------------------- envelopes measured over every member ----

        /// <summary>
        /// The width of a run is the envelope of ALL its members, not the span from the first
        /// member's left edge to the last one's right edge.
        ///
        /// Members are sorted by LEFT edge, and that does not make the last one the rightmost: a
        /// wide aperture followed by a narrow one that sits within its span puts the widest member
        /// first. Reading the width off the ends of the list then understates it — the one
        /// direction that matters, because an understated width is what lets a unit the product
        /// cannot supply pass the maximum-width check and be reported as buildable.
        /// </summary>
        [Fact]
        public void Group_Width_Is_The_Envelope_Of_Every_Member_Not_The_Span_Of_The_End_Ones()
        {
            // A spans 0.0-4.0 m; B sits inside it at 1.0-2.0 m. Sorted by left edge, A is first and
            // B is last, so "last.MaxX - first.MinX" would report 2.0 m for a 4.0 m envelope.
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 4.0, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 1.0, 1.0, 1.0, 2.25);

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b }.ApertureShadingGroups();

            Assert.Single(groups);
            Assert.Equal(4.0, groups[0].Width, 9);
        }

        /// <summary>
        /// The width test that decides whether a run must be SPLIT is measured over every member
        /// too, and this is where getting it wrong actually escapes: the group's own envelope is
        /// computed correctly, so a run wrongly judged narrow enough is returned as one group whose
        /// reported width is beyond anything the product can supply.
        ///
        /// Nothing stops a model handing over apertures whose boundaries overlap — duplicated or
        /// mis-modelled openings, curtain-wall panels carrying nested aperture boundaries — and the
        /// ordering only has to be unlucky once: sorted by left edge, the member reaching furthest
        /// right need not be last.
        /// </summary>
        [Fact]
        public void An_Over_Wide_Run_Is_Split_Even_When_Its_Last_Member_Is_Not_Its_Rightmost()
        {
            // A spans 0.0-3.0 m, B spans 2.9-6.5 m, C sits inside B at 3.0-3.5 m. Sorted by left
            // edge the order is A, B, C — so the LAST member reaches only 3.5 m while the run truly
            // reaches 6.5 m. Reading the ends gives 3.5 m, comfortably inside the 6.0 m product, and
            // the whole 6.5 m run is returned as one buildable unit. No single member is oversized
            // on its own, so the split is genuinely required rather than merely reported.
            ApertureSolarTarget a = Aperture(PanelA, G(1), 0.0, 3.0, 1.0, 2.25);
            ApertureSolarTarget b = Aperture(PanelA, G(2), 2.9, 3.6, 1.0, 2.25);
            ApertureSolarTarget c = Aperture(PanelA, G(3), 3.0, 0.5, 1.0, 2.25);

            List<ApertureShadingGroup> groups = new List<ApertureSolarTarget> { a, b, c }.ApertureShadingGroups();

            Assert.True(groups.Count > 1, "a 6.5 m envelope cannot be supplied as one 6.0 m unit");
            foreach (ApertureShadingGroup group in groups)
            {
                Assert.True(group.Width <= AwningSpecification.Dakar.MaximumWidth + 1e-9,
                    $"group {group.GroupGuid} is {group.Width:0.###} m wide, beyond the {AwningSpecification.Dakar.MaximumWidth:0.#} m product maximum");
            }

            // Every aperture still belongs to exactly one unit: a split must not drop a window.
            List<Guid> placed = groups.SelectMany(x => x.ApertureGuids).ToList();
            Assert.Equal(3, placed.Count);
            Assert.Equal(new HashSet<Guid> { G(1), G(2), G(3) }, new HashSet<Guid>(placed));
        }
    }
}
