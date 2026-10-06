// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The retractable folding-arm awning as a first-class shading family, and the Dakar product
    /// preset that constrains it. The family is pure plane-building mathematics — one deployed
    /// canopy quad plus an optional valance quad — and the preset is pure product policy; these
    /// tests pin both, and that the existing four families are untouched.
    /// </summary>
    public class RetractableAwningTests
    {
        private readonly ITestOutputHelper output;

        public RetractableAwningTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly int Year = 2018;

        private static Face3D SouthWindowFace()
        {
            // 2 m across (x), 1 m up (z), sill at z = 1, outward +Y — the repository's canonical
            // south-facing test window.
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

        private static SolarVisibilityCache Cache(ApertureSolarTarget target, double gridSize = 0.5)
        {
            return Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, 2.0, new List<SAM.Geometry.Object.Spatial.LinkedFace3D>(), target.AnalysisCells,
                cellSize: gridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);
        }

        private static ApertureDesirability Desirability(ApertureSolarTarget target, SolarVisibilityCache cache)
        {
            return Analytical.SolarCalculator.Create.ApertureDesirability(
                target, cache,
                new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28)),
                TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, 900.0, 0.2));
        }

        // ---------------------------------------------------------------- family wiring ----

        [Fact]
        public void Factory_Creates_RetractableAwning()
        {
            IShadingTypology typology = Analytical.SolarCalculator.Create.ShadingTypology("RetractableAwning");
            Assert.IsType<RetractableAwning>(typology);
            Assert.Equal("RetractableAwning", typology.Name);
        }

        [Fact]
        public void Name_List_Includes_The_Awning_And_Keeps_The_Existing_Families_Unchanged()
        {
            // The four existing families stay exactly where they were; the awning is appended as
            // the fifth, so every consumer that indexes the stable reporting order is untouched.
            string[] names = Analytical.SolarCalculator.Create.ShadingTypologyNames;
            Assert.Equal(5, names.Length);
            Assert.Equal(new string[] { "Overhang", "HorizontalLouvres", "VerticalFins", "EggCrate" },
                new string[] { names[0], names[1], names[2], names[3] });
            Assert.Equal("RetractableAwning", names[4]);
        }

        [Fact]
        public void The_Awning_Is_Eligible_As_A_Horizontal_Family()
        {
            // A south window's unwanted solar arrives high in the aperture frame, so every
            // horizontal family — overhang, louvres AND the awning — must be admitted.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache cache = Cache(target);
            ApertureDesirability desirability = Desirability(target, cache);

            List<string> eligible = Analytical.SolarCalculator.Query.EligibleShadingTypologies(target, cache, desirability, out double highSunFraction, out double obliqueSunFraction);

            Assert.NotNull(eligible);
            Assert.True(highSunFraction > 0.5, $"a south window must see mostly high sun, got {highSunFraction}");
            Assert.Contains("Overhang", eligible);
            Assert.Contains("HorizontalLouvres", eligible);
            Assert.Contains("RetractableAwning", eligible);

            output.WriteLine($"high-sun fraction {highSunFraction:0.###}, oblique {obliqueSunFraction:0.###} -> {string.Join(", ", eligible)}");
        }

        [Fact]
        public void Json_Round_Trip_Preserves_Every_Parameter()
        {
            RetractableAwning awning = new RetractableAwning(2.6, 22.0, 0.1, 0.15, 0.21);

            IShadingTypology reloaded = Core.Create.IJSAMObject<IShadingTypology>(awning.ToJsonObject().ToJsonString());
            Assert.IsType<RetractableAwning>(reloaded);

            foreach (string name in awning.ParameterNames)
            {
                Assert.Equal(awning.GetParameter(name), reloaded.GetParameter(name), 12);
            }
        }

        // ---------------------------------------------------------------------- geometry ----

        [Fact]
        public void Canopy_Is_One_Valid_Non_Degenerate_Quad()
        {
            ApertureSolarTarget target = Target();
            RetractableAwning awning = new RetractableAwning(2.6, 15.0, 0.0, 0.0, 0.0);

            List<ShadingElement> elements = awning.ShadingElements(target);

            Assert.NotNull(elements);
            Assert.Single(elements);
            Assert.Equal("RetractableAwning_Canopy", elements[0].Name);
            Assert.True(elements[0].Area > 0);
        }

        [Fact]
        public void Valance_Adds_Exactly_One_Second_Valid_Quad()
        {
            ApertureSolarTarget target = Target();
            RetractableAwning with = new RetractableAwning(2.6, 15.0, 0.0, 0.0, 0.21);

            List<ShadingElement> elements = with.ShadingElements(target);

            Assert.NotNull(elements);
            Assert.Equal(2, elements.Count);
            Assert.Equal("RetractableAwning_Canopy", elements[0].Name);
            Assert.Equal("RetractableAwning_Valance", elements[1].Name);
            Assert.True(elements[0].Area > 0);
            Assert.True(elements[1].Area > 0);
        }

        [Fact]
        public void Outward_Reach_And_Vertical_Drop_Follow_The_Product_Convention()
        {
            // Projection is the HORIZONTAL reach in plan; the deployed canopy drops by
            // projection x tan(tilt) at the front bar. The sloping fabric length must never be
            // substituted for the projection.
            ApertureSolarTarget target = Target();
            double projection = 2.6;
            double tilt = 20.0;

            RetractableAwning awning = new RetractableAwning(projection, tilt, 0.0, 0.0, 0.0);
            List<ShadingElement> elements = awning.ShadingElements(target);
            Face3D canopy = elements[0].Face3D;

            // The canopy is axis-aligned in the aperture frame, so the planar bounding box IS the
            // quad: back corners at (minX/maxX, Max.Y, z = 0), front corners at
            // (minX/maxX, Min.Y, z = projection), dropped by projection x tan(tilt).
            Geometry.Planar.BoundingBox2D boundingBox2D = target.Plane.Convert(canopy)?.GetBoundingBox();
            Assert.NotNull(boundingBox2D);

            double drop = boundingBox2D.Max.Y - boundingBox2D.Min.Y;
            Assert.Equal(projection * Math.Tan(tilt * Math.PI / 180.0), drop, 9);

            Assert.True(canopy.InRange(Point(target.Plane, boundingBox2D.Min.X, boundingBox2D.Max.Y, 0.0), 1e-6), "back-left corner at the facade");
            Assert.True(canopy.InRange(Point(target.Plane, boundingBox2D.Max.X, boundingBox2D.Max.Y, 0.0), 1e-6), "back-right corner at the facade");
            Assert.True(canopy.InRange(Point(target.Plane, boundingBox2D.Max.X, boundingBox2D.Min.Y, projection), 1e-6), "front-right corner at the horizontal projection");
            Assert.True(canopy.InRange(Point(target.Plane, boundingBox2D.Min.X, boundingBox2D.Min.Y, projection), 1e-6), "front-left corner at the horizontal projection");

            // The head it hangs from is the aperture head; the front bar sits projection below it
            // along the tilt line — the canopy area is width x projection / cos(tilt), never
            // width x (some other sloping length).
            Assert.True(Analytical.SolarCalculator.Query.TryGetApertureLocalBounds(target, out double _, out double _, out double _, out double maxY));
            Assert.Equal(maxY, boundingBox2D.Max.Y, 9);
        }

        private static Point3D Point(Plane plane, double x, double y, double z)
        {
            return new Point3D(
                plane.Origin.X + x * plane.AxisX.X + y * plane.AxisY.X + z * plane.Normal.X,
                plane.Origin.Y + x * plane.AxisX.Y + y * plane.AxisY.Y + z * plane.Normal.Y,
                plane.Origin.Z + x * plane.AxisX.Z + y * plane.AxisY.Z + z * plane.Normal.Z);
        }

        // -------------------------------------------------------------- mounting offset ----

        [Fact]
        public void Zero_Mounting_Offset_Keeps_The_Awning_Anchored_To_The_Aperture_Plane()
        {
            // Backward compatibility: MountingOffset 0.0 must reproduce the original facade-anchored
            // geometry — rear edge at z = 0, front bar at z = projection.
            ApertureSolarTarget target = Target();
            double projection = 2.6;
            double tilt = 15.0;

            RetractableAwning awning = new RetractableAwning(projection, tilt, 0.0, 0.0, 0.0); // offset defaults to 0
            List<ShadingElement> elements = awning.ShadingElements(target);
            Face3D canopy = elements[0].Face3D;

            Assert.True(Analytical.SolarCalculator.Query.TryGetApertureLocalBounds(target, out double minX, out double maxX, out double _, out double maxY));
            double drop = projection * Math.Tan(tilt * Math.PI / 180.0);

            Assert.True(canopy.InRange(Point(target.Plane, minX, maxY, 0.0), 1e-6), "rear-left corner at the facade");
            Assert.True(canopy.InRange(Point(target.Plane, maxX, maxY, 0.0), 1e-6), "rear-right corner at the facade");
            Assert.True(canopy.InRange(Point(target.Plane, maxX, maxY - drop, projection), 1e-6), "front-right corner at the horizontal projection");
            Assert.True(canopy.InRange(Point(target.Plane, minX, maxY - drop, projection), 1e-6), "front-left corner at the horizontal projection");
        }

        [Fact]
        public void Mounting_Offset_Moves_The_Whole_Awning_Outward_Without_Changing_Projection()
        {
            // Rear canopy edge at z = MountingOffset, front bar at z = MountingOffset + Projection.
            // The product projection (horizontal reach) is the difference, never the aperture-plane
            // distance, and the vertical drop Projection x tan(tilt) is unchanged.
            ApertureSolarTarget target = Target();
            double projection = 2.6;
            double tilt = 15.0;
            double offset = 0.35;

            RetractableAwning awning = new RetractableAwning(projection, tilt, 0.0, 0.0, 0.0, offset);
            List<ShadingElement> elements = awning.ShadingElements(target);
            Face3D canopy = elements[0].Face3D;

            Assert.True(Analytical.SolarCalculator.Query.TryGetApertureLocalBounds(target, out double minX, out double maxX, out double _, out double maxY));
            double drop = projection * Math.Tan(tilt * Math.PI / 180.0);
            double rearZ = offset;
            double frontZ = offset + projection;

            Assert.True(canopy.InRange(Point(target.Plane, minX, maxY, rearZ), 1e-6), "rear-left corner at the mounting offset");
            Assert.True(canopy.InRange(Point(target.Plane, maxX, maxY, rearZ), 1e-6), "rear-right corner at the mounting offset");
            Assert.True(canopy.InRange(Point(target.Plane, maxX, maxY - drop, frontZ), 1e-6), "front-right corner at offset + projection");
            Assert.True(canopy.InRange(Point(target.Plane, minX, maxY - drop, frontZ), 1e-6), "front-left corner at offset + projection");

            Assert.Equal(projection, frontZ - rearZ, 12);
        }

        [Fact]
        public void Valance_Sits_At_The_Front_Bar_Of_The_Mounting_Offset()
        {
            ApertureSolarTarget target = Target();
            double projection = 2.6;
            double tilt = 15.0;
            double valance = 0.21;
            double offset = 0.35;

            RetractableAwning awning = new RetractableAwning(projection, tilt, 0.0, 0.0, valance, offset);
            List<ShadingElement> elements = awning.ShadingElements(target);
            Assert.Equal(2, elements.Count);
            Face3D valanceFace = elements[1].Face3D;

            Assert.True(Analytical.SolarCalculator.Query.TryGetApertureLocalBounds(target, out double minX, out double maxX, out double _, out double maxY));
            double drop = projection * Math.Tan(tilt * Math.PI / 180.0);
            double frontZ = offset + projection;

            // The valance hangs from the front bar, so it sits at z = MountingOffset + Projection.
            Assert.True(valanceFace.InRange(Point(target.Plane, minX, maxY - drop, frontZ), 1e-6));
            Assert.True(valanceFace.InRange(Point(target.Plane, maxX, maxY - drop, frontZ), 1e-6));
            Assert.True(valanceFace.InRange(Point(target.Plane, maxX, maxY - drop - valance, frontZ), 1e-6));
            Assert.True(valanceFace.InRange(Point(target.Plane, minX, maxY - drop - valance, frontZ), 1e-6));
        }

        [Fact]
        public void Mounting_Offset_Changes_The_Element_Identity()
        {
            // The element GUID scheme hashes every parameter, so two otherwise identical awnings at
            // different mounting offsets must never share geometry identity.
            ApertureSolarTarget target = Target();
            RetractableAwning atFacade = new RetractableAwning(2.6, 15.0, 0.0, 0.0, 0.0, 0.0);
            RetractableAwning recessed = new RetractableAwning(2.6, 15.0, 0.0, 0.0, 0.0, 0.35);

            Assert.NotEqual(Guids(atFacade.ShadingElements(target)), Guids(recessed.ShadingElements(target)));
        }

        [Fact]
        public void Json_Round_Trip_Preserves_A_Non_Zero_Mounting_Offset()
        {
            RetractableAwning awning = new RetractableAwning(2.6, 22.0, 0.1, 0.15, 0.21, 0.35);

            IShadingTypology reloaded = Core.Create.IJSAMObject<IShadingTypology>(awning.ToJsonObject().ToJsonString());
            Assert.IsType<RetractableAwning>(reloaded);
            Assert.Equal(0.35, reloaded.GetParameter("MountingOffset"), 12);
        }

        [Fact]
        public void Material_Area_Equals_Canopy_Plus_Optional_Valance()
        {
            ApertureSolarTarget target = Target();
            double width = 2.0;

            // Closed forms: canopy = width x projection / cos(tilt); valance = width x valance.
            double projection = 2.6;
            double tilt = 15.0;
            double canopyArea = width * projection / Math.Cos(tilt * Math.PI / 180.0);

            RetractableAwning withoutValance = new RetractableAwning(projection, tilt, 0.0, 0.0, 0.0);
            double totalArea = 0;
            foreach (ShadingElement element in withoutValance.ShadingElements(target))
            {
                totalArea += element.Area;
            }

            Assert.Equal(canopyArea, totalArea, 9);
            Assert.Equal(canopyArea, withoutValance.MaterialFraction(target) * target.GrossArea, 9);

            double valance = 0.21;
            RetractableAwning withValance = new RetractableAwning(projection, tilt, 0.0, 0.0, valance);
            double totalWithValance = 0;
            foreach (ShadingElement element in withValance.ShadingElements(target))
            {
                totalWithValance += element.Area;
            }

            Assert.Equal(canopyArea + width * valance, totalWithValance, 9);
            Assert.Equal(canopyArea + width * valance, withValance.MaterialFraction(target) * target.GrossArea, 9);
        }

        [Fact]
        public void Element_Guids_Are_Deterministic_And_Move_With_The_Parameters()
        {
            ApertureSolarTarget target = Target();
            RetractableAwning awning = new RetractableAwning(2.6, 15.0, 0.0, 0.0, 0.0);

            List<Guid> a = Guids(awning.ShadingElements(target));
            List<Guid> b = Guids(awning.ShadingElements(target));
            Assert.Equal(a, b);

            RetractableAwning changed = new RetractableAwning(2.6, 20.0, 0.0, 0.0, 0.0);
            List<Guid> c = Guids(changed.ShadingElements(target));
            Assert.NotEqual(a, c);

            // The GUID scheme hashes EVERY parameter, so adding a valance moves the whole element
            // table — exactly like the composed EggCrate re-issues its children's Guids.
            RetractableAwning withValance = new RetractableAwning(2.6, 15.0, 0.0, 0.0, 0.21);
            Assert.NotEqual(a, Guids(withValance.ShadingElements(target)));
        }

        private static List<Guid> Guids(List<ShadingElement> elements)
        {
            return elements.ConvertAll(x => x.Guid);
        }

        // ------------------------------------------------------------------- Dakar preset ----

        [Fact]
        public void Dakar_Projection_Set_Is_Exact()
        {
            Assert.Equal(new double[] { 1.6, 2.1, 2.6, 3.1, 3.6 }, AwningSpecification.Dakar.AllowedProjections);
            Assert.True(AwningSpecification.Dakar.IsProjectionAllowed(1.6));
            Assert.True(AwningSpecification.Dakar.IsProjectionAllowed(2.6));
            Assert.True(AwningSpecification.Dakar.IsProjectionAllowed(3.6));
            Assert.False(AwningSpecification.Dakar.IsProjectionAllowed(2.7));
            Assert.False(AwningSpecification.Dakar.IsProjectionAllowed(4.0));
        }

        [Fact]
        public void Dakar_Width_Projection_Rules_Are_Enforced_Without_Clamping()
        {
            // 3.00 m width with a 2.60 m projection is valid: 2.60 + 0.40 = 3.00 m.
            Assert.True(AwningSpecification.Dakar.IsValid(3.00, 2.60, 15.0, out string message));
            Assert.Null(message);

            // 3.00 m width with a 3.10 m projection is invalid: it needs at least 3.50 m.
            Assert.False(AwningSpecification.Dakar.IsValid(3.00, 3.10, 15.0, out message));
            Assert.Contains("minimum width", message);

            // Above the maximum width nothing is valid.
            Assert.False(AwningSpecification.Dakar.IsValid(6.20, 2.60, 15.0, out message));
            Assert.Contains("maximum", message);

            // Tilt outside 5°-40° is refused.
            Assert.False(AwningSpecification.Dakar.IsValid(3.00, 2.60, 4.0, out message));
            Assert.False(AwningSpecification.Dakar.IsValid(3.00, 2.60, 41.0, out message));
            Assert.True(AwningSpecification.Dakar.IsValid(3.00, 2.60, 5.0, out message));
            Assert.True(AwningSpecification.Dakar.IsValid(3.00, 2.60, 40.0, out message));
        }

        [Fact]
        public void Dakar_Minimum_Width_And_Brackets_Follow_The_Product_Table()
        {
            Assert.Equal(2.0, AwningSpecification.Dakar.MinimumWidth(1.6), 9);
            Assert.Equal(3.0, AwningSpecification.Dakar.MinimumWidth(2.6), 9);
            Assert.Equal(4.0, AwningSpecification.Dakar.MinimumWidth(3.6), 9);
            Assert.True(double.IsNaN(AwningSpecification.Dakar.MinimumWidth(2.7)), "minimum width for an unlisted projection is unavailable, not invented");

            Assert.Equal(2, AwningSpecification.Dakar.RequiredWallBracketCount(3.0));
            Assert.Equal(2, AwningSpecification.Dakar.RequiredWallBracketCount(4.1));
            Assert.Equal(3, AwningSpecification.Dakar.RequiredWallBracketCount(4.100001));
            Assert.Equal(3, AwningSpecification.Dakar.RequiredWallBracketCount(6.0));
        }

        [Fact]
        public void Dakar_Valance_Is_None_Or_The_Standard_Depth()
        {
            Assert.True(AwningSpecification.Dakar.IsValidValanceDepth(0.0));
            Assert.True(AwningSpecification.Dakar.IsValidValanceDepth(0.21));
            Assert.False(AwningSpecification.Dakar.IsValidValanceDepth(0.15));
            Assert.Equal(0.21, AwningSpecification.Dakar.StandardValanceDepth, 12);
        }

        [Fact]
        public void Single_Aperture_Search_Lattice_Is_The_Dakar_Projection_Set()
        {
            // The generic optimiser's parameter lattice for the awning family must sample the exact
            // Dakar nominal projections — the 0.5 m step over the 1.6-3.6 m bounds.
            RetractableAwning awning = new RetractableAwning();
            List<ShadingParameter> parameters = Analytical.SolarCalculator.Create.ShadingParameters(awning);

            ShadingParameter projection = parameters.Find(x => x.Name == "Projection");
            Assert.NotNull(projection);
            Assert.Equal(1.6, projection.Minimum, 9);
            Assert.Equal(3.6, projection.Maximum, 9);
            Assert.Equal(0.5, projection.MinimumIncrement, 9);

            List<double> lattice = new List<double>();
            for (double value = projection.Minimum; value <= projection.Maximum + 1e-9; value += 0.5)
            {
                lattice.Add(value);
            }

            Assert.Equal(new double[] { 1.6, 2.1, 2.6, 3.1, 3.6 }, lattice);

            // The awning's horizontal reach parameter is Projection, never a non-existent Depth.
            Assert.Contains("Projection", awning.ParameterNames);
            Assert.DoesNotContain("Depth", awning.ParameterNames);

            // Valance is a two-state parameter: none or the standard depth.
            ShadingParameter valance = parameters.Find(x => x.Name == "ValanceDepth");
            Assert.NotNull(valance);
            Assert.Equal(0.0, valance.Minimum, 9);
            Assert.Equal(0.21, valance.Maximum, 9);
            Assert.Equal(0.21, valance.MinimumIncrement, 9);

            // The awning-specific 1° tilt refinement lives in the GROUP search only: the global
            // TiltDegrees step for the existing louvre/fin families is untouched.
            List<ShadingParameter> louvreParameters = Analytical.SolarCalculator.Create.ShadingParameters(new HorizontalLouvres());
            Assert.Equal(5.0, louvreParameters.Find(x => x.Name == "TiltDegrees").MinimumIncrement, 9);
        }
    }
}

