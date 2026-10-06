// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Shared machinery for the buildable shading families: named bounded parameters, the aperture
    /// local frame, and DETERMINISTIC element Guids.
    ///
    /// The Guids are derived from the family name, the parameter values and the element ordinal
    /// rather than allocated fresh. Attribution reports Guids, so a rationalisation sweep that
    /// regenerated random Guids on every candidate could never compare two candidates or reuse an
    /// attribution cache. Deriving them also means a geometry change necessarily changes the Guid
    /// table, which is exactly what the attribution-table hash is there to detect.
    /// </summary>
    public abstract class ShadingTypology : IShadingTypology
    {
        private readonly Dictionary<string, double> parameters = new Dictionary<string, double>();
        private readonly Dictionary<string, double[]> bounds = new Dictionary<string, double[]>();
        private readonly List<string> parameterNames = new List<string>();

        protected ShadingTypology()
        {
        }

        public abstract string Name { get; }

        public List<string> ParameterNames { get { return new List<string>(parameterNames); } }

        protected void Define(string name, double value, double minimum, double maximum)
        {
            if (!parameters.ContainsKey(name))
            {
                parameterNames.Add(name);
            }

            bounds[name] = new double[] { minimum, maximum };
            parameters[name] = Math.Min(maximum, Math.Max(minimum, value));
        }

        public double GetParameter(string name)
        {
            return parameters.TryGetValue(name, out double value) ? value : double.NaN;
        }

        public bool SetParameter(string name, double value)
        {
            if (!bounds.TryGetValue(name, out double[] range))
            {
                return false;
            }

            parameters[name] = Math.Min(range[1], Math.Max(range[0], value));
            return true;
        }

        public bool TryGetBounds(string name, out double minimum, out double maximum)
        {
            minimum = double.NaN;
            maximum = double.NaN;
            if (!bounds.TryGetValue(name, out double[] range))
            {
                return false;
            }

            minimum = range[0];
            maximum = range[1];
            return true;
        }

        public abstract List<ShadingElement> ShadingElements(ApertureSolarTarget target);

        public double MaterialFraction(ApertureSolarTarget target)
        {
            List<ShadingElement> elements = ShadingElements(target);
            double grossArea = target == null ? double.NaN : target.GrossArea;
            if (elements == null || double.IsNaN(grossArea) || grossArea <= 0)
            {
                return double.NaN;
            }

            double total = 0;
            foreach (ShadingElement element in elements)
            {
                double area = element.Area;
                if (!double.IsNaN(area))
                {
                    total += area;
                }
            }

            return total / grossArea;
        }

        /// <summary>Aperture local bounding box: x across the facade, y up-slope.</summary>
        protected static bool TryGetLocalBounds(ApertureSolarTarget target, out double minX, out double maxX, out double minY, out double maxY)
        {
            minX = maxX = minY = maxY = double.NaN;

            Plane plane = target?.Plane;
            Face3D face3D = target?.Face3D;
            if (plane == null || face3D == null)
            {
                return false;
            }

            Geometry.Planar.BoundingBox2D boundingBox2D = plane.Convert(face3D)?.GetBoundingBox();
            if (boundingBox2D == null)
            {
                return false;
            }

            minX = boundingBox2D.Min.X;
            maxX = boundingBox2D.Max.X;
            minY = boundingBox2D.Min.Y;
            maxY = boundingBox2D.Max.Y;
            return true;
        }

        /// <summary>World point from aperture-local coordinates (X across, Y up-slope, Z outward).</summary>
        protected static Point3D World(Plane plane, double x, double y, double z)
        {
            Point3D origin = plane.Origin;
            Vector3D axisX = plane.AxisX;
            Vector3D axisY = plane.AxisY;
            Vector3D axisZ = plane.Normal;

            return new Point3D(
                origin.X + x * axisX.X + y * axisY.X + z * axisZ.X,
                origin.Y + x * axisX.Y + y * axisY.Y + z * axisZ.Y,
                origin.Z + x * axisX.Z + y * axisY.Z + z * axisZ.Z);
        }

        /// <summary>A planar quad from four aperture-local corners.</summary>
        protected static Face3D Quad(Plane plane, double[][] corners)
        {
            List<Point3D> points = new List<Point3D>(4);
            foreach (double[] corner in corners)
            {
                points.Add(World(plane, corner[0], corner[1], corner[2]));
            }

            return new Face3D(new Polygon3D(points));
        }

        /// <summary>
        /// A Guid derived from the family, its parameters and the element ordinal, so identical
        /// geometry always carries identical identity.
        /// </summary>
        protected Guid ElementGuid(int ordinal)
        {
            System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();
            stringBuilder.Append(Name);
            foreach (string name in parameterNames)
            {
                stringBuilder.Append('|');
                stringBuilder.Append(name);
                stringBuilder.Append('=');
                stringBuilder.Append(parameters[name].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }

            stringBuilder.Append("|#");
            stringBuilder.Append(ordinal);

            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stringBuilder.ToString())));
            }
        }

        public virtual bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            foreach (string name in new List<string>(parameterNames))
            {
                if (jObject.ContainsKey(name))
                {
                    SetParameter(name, jObject[name]?.GetValue<double>() ?? double.NaN);
                }
            }

            return true;
        }

        public virtual JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            foreach (string name in parameterNames)
            {
                jObject.Add(name, parameters[name]);
            }

            return jObject;
        }
    }

    /// <summary>
    /// The NULL DEVICE: leaving the window alone, expressed as a device so it can be measured
    /// rather than merely asserted.
    ///
    /// "No shading is worth building here" is a successful engineering answer, not a failure, and an
    /// engineer is entitled to see it verified on the same footing as any other proposal. Because
    /// this builds an EMPTY element list (empty, never null — null means "this family cannot act on
    /// this aperture"), the ordinary Stage 8 accounting runs over it unchanged: the admitted
    /// baseline is measured, nothing is intercepted, and the percentages come out as the true 0 %
    /// blocked / 100 % retained rather than being fabricated by a special case. Where a denominator
    /// is genuinely zero the result stays NaN, exactly as for a real device.
    ///
    /// It is deliberately NOT one of Create.ShadingTypologyNames: there is nothing to optimise.
    /// </summary>
    public class NoShading : ShadingTypology
    {
        public override string Name { get { return "NoShading"; } }

        public override List<ShadingElement> ShadingElements(ApertureSolarTarget target)
        {
            return target == null ? null : new List<ShadingElement>();
        }
    }

    /// <summary>A single horizontal plate above the head: the canonical south-facade device.</summary>
    public class Overhang : ShadingTypology
    {
        public Overhang() : this(0.5, 0.0, 0.0)
        {
        }

        public Overhang(double depth, double riseAboveHead = 0.0, double extensionBeyondJambs = 0.0)
        {
            Define("Depth", depth, 0.05, 3.0);
            Define("RiseAboveHead", riseAboveHead, 0.0, 1.0);
            Define("ExtensionBeyondJambs", extensionBeyondJambs, 0.0, 1.0);
        }

        public override string Name { get { return "Overhang"; } }

        public override List<ShadingElement> ShadingElements(ApertureSolarTarget target)
        {
            if (!TryGetLocalBounds(target, out double minX, out double maxX, out double _, out double maxY))
            {
                return null;
            }

            double depth = GetParameter("Depth");
            double rise = GetParameter("RiseAboveHead");
            double extension = GetParameter("ExtensionBeyondJambs");
            Plane plane = target.Plane;

            double y = maxY + rise;
            double x0 = minX - extension;
            double x1 = maxX + extension;

            Face3D face3D = Quad(plane, new double[][]
            {
                new double[] { x0, y, 0 },
                new double[] { x1, y, 0 },
                new double[] { x1, y, depth },
                new double[] { x0, y, depth },
            });

            return new List<ShadingElement> { new ShadingElement(ElementGuid(0), "Overhang", face3D) };
        }
    }

    /// <summary>Stacked horizontal blades over the aperture, optionally tilted down-and-out.</summary>
    public class HorizontalLouvres : ShadingTypology
    {
        public HorizontalLouvres() : this(0.3, 4, 0.0)
        {
        }

        public HorizontalLouvres(double depth, int count, double tiltDegrees = 0.0)
        {
            Define("Depth", depth, 0.05, 2.0);
            Define("Count", count, 1, 24);
            Define("TiltDegrees", tiltDegrees, -60.0, 60.0);
        }

        public override string Name { get { return "HorizontalLouvres"; } }

        public override List<ShadingElement> ShadingElements(ApertureSolarTarget target)
        {
            if (!TryGetLocalBounds(target, out double minX, out double maxX, out double minY, out double maxY))
            {
                return null;
            }

            double depth = GetParameter("Depth");
            int count = (int)Math.Round(GetParameter("Count"));
            double tilt = GetParameter("TiltDegrees") * Math.PI / 180.0;
            Plane plane = target.Plane;

            // Blades sit at even heights from the head DOWN over the glass, the topmost at the head.
            double span = maxY - minY;
            double spacing = count <= 1 ? 0 : span / (count - 1);

            // Positive tilt drops the outer edge, the usual light-shelf/brise-soleil convention.
            double outerY = -depth * Math.Sin(tilt);
            double outerZ = depth * Math.Cos(tilt);

            List<ShadingElement> result = new List<ShadingElement>();
            for (int i = 0; i < count; i++)
            {
                double y = maxY - i * spacing;
                Face3D face3D = Quad(plane, new double[][]
                {
                    new double[] { minX, y, 0 },
                    new double[] { maxX, y, 0 },
                    new double[] { maxX, y + outerY, outerZ },
                    new double[] { minX, y + outerY, outerZ },
                });

                result.Add(new ShadingElement(ElementGuid(i), "Louvre " + (i + 1), face3D));
            }

            return result;
        }
    }

    /// <summary>Vertical blades across the aperture: the oblique-sun (east/west facade) device.</summary>
    public class VerticalFins : ShadingTypology
    {
        public VerticalFins() : this(0.4, 3, 0.0)
        {
        }

        public VerticalFins(double depth, int count, double tiltDegrees = 0.0)
        {
            Define("Depth", depth, 0.05, 2.0);
            Define("Count", count, 1, 24);
            Define("TiltDegrees", tiltDegrees, -60.0, 60.0);
        }

        public override string Name { get { return "VerticalFins"; } }

        public override List<ShadingElement> ShadingElements(ApertureSolarTarget target)
        {
            if (!TryGetLocalBounds(target, out double minX, out double maxX, out double minY, out double maxY))
            {
                return null;
            }

            double depth = GetParameter("Depth");
            int count = (int)Math.Round(GetParameter("Count"));
            double tilt = GetParameter("TiltDegrees") * Math.PI / 180.0;
            Plane plane = target.Plane;

            double span = maxX - minX;
            double spacing = count <= 1 ? 0 : span / (count - 1);

            // Positive tilt swings the outer edge toward +X, which is how a fin is angled to favour
            // one side of an oblique facade.
            double outerX = depth * Math.Sin(tilt);
            double outerZ = depth * Math.Cos(tilt);

            List<ShadingElement> result = new List<ShadingElement>();
            for (int i = 0; i < count; i++)
            {
                double x = count <= 1 ? 0.5 * (minX + maxX) : minX + i * spacing;
                Face3D face3D = Quad(plane, new double[][]
                {
                    new double[] { x, minY, 0 },
                    new double[] { x, maxY, 0 },
                    new double[] { x + outerX, maxY, outerZ },
                    new double[] { x + outerX, minY, outerZ },
                });

                result.Add(new ShadingElement(ElementGuid(i), "Fin " + (i + 1), face3D));
            }

            return result;
        }
    }

    /// <summary>
    /// A retractable folding-arm awning, analysed in its fully deployed position: one sloping canopy
    /// quad in front of the aperture plus, when a valance depth is given, one front valance quad.
    ///
    /// PROJECTION is the manufacturer's HORIZONTAL reach from the mounting plane to the front bar —
    /// the plan dimension a product is sized by. TiltDegrees is the DOWNWARD angle of the deployed
    /// fabric from horizontal, the fixed installation setting, so the canopy drops by
    /// Projection x tan(tilt) at the front edge; the sloping fabric length Projection / cos(tilt) is
    /// reflected automatically by the quad area and must never be substituted for the projection.
    ///
    /// MOUNTINGOFFSET is the horizontal outward distance from the aperture plane to the awning rear
    /// mounting line. It is a project/building placement input (a recessed aperture whose awning is
    /// mounted on the external facade or soffit), not a product limit: the rear canopy edge sits at
    /// z = MountingOffset and the front bar at z = MountingOffset + Projection, moving the whole
    /// deployed awning outward without changing its product projection, drop or valance.
    ///
    /// THE FABRIC IS OPAQUE AND ZERO-THICKNESS, exactly like every other family here: the ray engine
    /// answers "blocked or not blocked". Deployment scheduling, automatic retraction, fabric solar
    /// transmittance, diffuse transmission, reflection, wind control and structural capacity are NOT
    /// modelled — the geometry describes the deployed device only, and a real product that can
    /// retract does not reduce the wanted-solar loss the analysis reports. Dynamic deployment
    /// control is a documented future extension.
    ///
    /// The parameter bounds mirror the Dakar product envelope (see AwningSpecification.Dakar for the
    /// authoritative limits and validation); the class itself is pure plane-building mathematics and
    /// carries no product policy.
    /// </summary>
    public class RetractableAwning : ShadingTypology
    {
        public RetractableAwning() : this(2.1, 15.0, 0.0, 0.0, 0.0)
        {
        }

        /// <summary>
        /// The original five-parameter constructor, retained exactly so callers compiled against the
        /// pre-mounting-offset signature keep resolving without recompilation. It forwards to the
        /// six-parameter overload with MountingOffset = 0.0.
        /// </summary>
        public RetractableAwning(double projection, double tiltDegrees = 15.0, double riseAboveHead = 0.0, double extensionBeyondJambs = 0.0, double valanceDepth = 0.0)
            : this(projection, tiltDegrees, riseAboveHead, extensionBeyondJambs, valanceDepth, 0.0)
        {
        }

        public RetractableAwning(double projection, double tiltDegrees, double riseAboveHead, double extensionBeyondJambs, double valanceDepth, double mountingOffset)
        {
            Define("Projection", projection, 1.6, 3.6);
            Define("TiltDegrees", tiltDegrees, 5.0, 40.0);
            Define("RiseAboveHead", riseAboveHead, 0.0, 1.0);
            Define("ExtensionBeyondJambs", extensionBeyondJambs, 0.0, 1.0);
            Define("ValanceDepth", valanceDepth, 0.0, 0.21);
            // MountingOffset is a project/building placement input, not a product limit: the awning
            // may be mounted any finite, non-negative distance outward from the aperture plane. The
            // upper bound is deliberately not a product restriction; the public API refuses negative,
            // NaN and infinite values instead of clamping them (see Optimise.ValidAwningInputs).
            Define("MountingOffset", mountingOffset, 0.0, double.MaxValue);
        }

        public override string Name { get { return "RetractableAwning"; } }

        public override List<ShadingElement> ShadingElements(ApertureSolarTarget target)
        {
            if (!TryGetLocalBounds(target, out double minX, out double maxX, out double _, out double maxY))
            {
                return null;
            }

            return ShadingElements(target.Plane, minX, maxX, maxY);
        }

        /// <summary>
        /// The deployed canopy (and optional valance) for an explicit local extent — the geometry
        /// entry point the grouped-aperture path uses, so one physical awning can span several
        /// apertures instead of a copy being rebuilt around each.
        /// </summary>
        public List<ShadingElement> ShadingElements(Plane plane, double minX, double maxX, double maxY)
        {
            if (plane == null || double.IsNaN(minX) || double.IsNaN(maxX) || double.IsNaN(maxY))
            {
                return null;
            }

            double projection = GetParameter("Projection");
            double tilt = GetParameter("TiltDegrees") * Math.PI / 180.0;
            double rise = GetParameter("RiseAboveHead");
            double extension = GetParameter("ExtensionBeyondJambs");
            double valance = GetParameter("ValanceDepth");
            double offset = GetParameter("MountingOffset");

            // Projection stays the HORIZONTAL reach from the mounting line to the front bar. The
            // mounting line itself sits MountingOffset outward of the aperture plane, so the rear
            // canopy edge is at z = MountingOffset and the front bar at z = MountingOffset + Projection.
            // The vertical drop Projection x tan(tilt) is unchanged.
            double rearZ = offset;
            double frontZ = offset + projection;

            double drop = projection * Math.Tan(tilt);
            double x0 = minX - extension;
            double x1 = maxX + extension;
            double y0 = maxY + rise;
            double y1 = y0 - drop;

            List<ShadingElement> result = new List<ShadingElement>();

            Face3D canopy = Quad(plane, new double[][]
            {
                new double[] { x0, y0, rearZ },
                new double[] { x1, y0, rearZ },
                new double[] { x1, y1, frontZ },
                new double[] { x0, y1, frontZ },
            });

            result.Add(new ShadingElement(ElementGuid(0), "RetractableAwning_Canopy", canopy));

            if (valance > 0)
            {
                Face3D valanceFace = Quad(plane, new double[][]
                {
                    new double[] { x0, y1, frontZ },
                    new double[] { x1, y1, frontZ },
                    new double[] { x1, y1 - valance, frontZ },
                    new double[] { x0, y1 - valance, frontZ },
                });

                result.Add(new ShadingElement(ElementGuid(1), "RetractableAwning_Valance", valanceFace));
            }

            return result;
        }
    }

    /// <summary>Horizontal blades crossed with vertical fins: for facades with both a high and an oblique problem.</summary>
    public class EggCrate : ShadingTypology
    {
        public EggCrate() : this(0.3, 3, 3)
        {
        }

        public EggCrate(double depth, int louvreCount, int finCount)
        {
            Define("Depth", depth, 0.05, 2.0);
            Define("LouvreCount", louvreCount, 1, 16);
            Define("FinCount", finCount, 1, 16);
        }

        public override string Name { get { return "EggCrate"; } }

        public override List<ShadingElement> ShadingElements(ApertureSolarTarget target)
        {
            double depth = GetParameter("Depth");
            int louvreCount = (int)Math.Round(GetParameter("LouvreCount"));
            int finCount = (int)Math.Round(GetParameter("FinCount"));

            List<ShadingElement> louvres = new HorizontalLouvres(depth, louvreCount).ShadingElements(target);
            List<ShadingElement> fins = new VerticalFins(depth, finCount).ShadingElements(target);
            if (louvres == null || fins == null)
            {
                return null;
            }

            // Re-issue Guids from THIS typology so an egg crate's elements are never confused with
            // those of a bare louvre or fin array carrying the same parameters.
            List<ShadingElement> result = new List<ShadingElement>();
            int ordinal = 0;
            foreach (ShadingElement element in louvres)
            {
                result.Add(new ShadingElement(ElementGuid(ordinal++), element.Name, element.Face3D));
            }

            foreach (ShadingElement element in fins)
            {
                result.Add(new ShadingElement(ElementGuid(ordinal++), element.Name, element.Face3D));
            }

            return result;
        }
    }
}
