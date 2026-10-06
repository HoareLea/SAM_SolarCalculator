// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Analytical.SolarCalculator;
using SAM.Core.Grasshopper;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    /// <summary>How the field is drawn in the viewport.</summary>
    public enum ShadingFieldPreviewMode
    {
        /// <summary>One dot per location. Cheap, and the default.</summary>
        Points,

        /// <summary>A shaded box per location. Easier to read as a volume; only for smaller maps.</summary>
        Voxels,

        /// <summary>Nothing drawn. The numbers are unaffected.</summary>
        None,
    }

    /// <summary>
    /// The shading potential field in front of one aperture: for every location in the space where
    /// shading could go, what putting shading material THERE would be worth.
    ///
    /// WHAT THE COLOURS MEAN. This is the single most misread thing in the whole workflow, so it is
    /// worth being exact. The colour is not "how much sun is here". It is THE VALUE OF PUTTING
    /// SHADING MATERIAL AT THIS LOCATION:
    ///
    ///   RED   — SHADE HERE. Material here intercepts solar you asked to block. Stronger red,
    ///           bigger gain.
    ///   BLUE  — KEEP OPEN. Material here would intercept solar you asked to KEEP. Stronger blue,
    ///           worse the loss.
    ///   GREY  — neither: almost no beam passes through, so material here does nothing either way.
    ///
    /// Read that way, the picture is a set of instructions rather than a heat map: fill the red,
    /// stay out of the blue.
    ///
    /// SAM has no shared diverging colour ramp (its colour queries are keyed to panel and aperture
    /// types), so the ramp is defined here and stays local to this preview rather than inventing a
    /// shared convention other components would then have to honour.
    ///
    /// Near-zero locations are not drawn at all: a full grid of grey dots hides the answer. The
    /// threshold is a share of the field's own strongest value, so it scales with the project.
    /// </summary>
    public class GooShadingPotentialField : GooJSAMObject<ShadingPotentialField>, IGH_PreviewData
    {
        /// <summary>Locations weaker than this share of the field's own extreme are not drawn.</summary>
        private const double PreviewCutoff = 0.02;

        private const int PreviewPointSize = 3;

        /// <summary>
        /// Above this many drawable locations, the voxel preview silently becomes the point preview.
        ///
        /// A box is 8 vertices and 12 triangles, rebuilt whenever the field changes and held for as
        /// long as the wire does. At a 0.1 m voxel a generous studied space is already tens of
        /// thousands of locations, and a viewport that stops responding is a worse answer than a
        /// coarser picture. The mesh is built ONCE and cached, never per redraw.
        /// </summary>
        private const int VoxelPreviewLimit = 20000;

        private readonly ShadingFieldPreviewMode previewMode = ShadingFieldPreviewMode.Points;
        private readonly double previewWantedSolarPenalty = 1.0;

        private Mesh voxelMesh;
        private bool voxelMeshBuilt;

        public GooShadingPotentialField()
            : base()
        {
        }

        public GooShadingPotentialField(ShadingPotentialField shadingPotentialField)
            : base(shadingPotentialField)
        {
        }

        public GooShadingPotentialField(ShadingPotentialField shadingPotentialField, ShadingFieldPreviewMode previewMode, double wantedSolarPenalty)
            : base(shadingPotentialField)
        {
            this.previewMode = previewMode;
            previewWantedSolarPenalty = double.IsNaN(wantedSolarPenalty) ? 1.0 : wantedSolarPenalty;
        }

        public override IGH_Goo Duplicate()
        {
            return new GooShadingPotentialField(Value, previewMode, previewWantedSolarPenalty);
        }

        public override string ToString()
        {
            ShadingPotentialField field = Value;
            if (field == null)
            {
                return typeof(ShadingPotentialField).Name;
            }

            // NO UNIT ON THE TOTALS, and that is a deliberate decision rather than an omission. The
            // per-voxel values are genuinely kWh — the beam energy a piece of material at that spot
            // would intercept — but a ray passes through many voxels and contributes to every one,
            // so the SUM counts the same kWh repeatedly and is not an amount of energy anything
            // could save. Writing "benefit 5504 kWh" next to it, which is what this said until
            // Stage 10.2, reads as a saving roughly ten times larger than the window even admits.
            // The totals are a cumulative spatial potential and are shown as bare numbers for
            // ranking and comparison; kWh belongs on VerifyShading, where a real device was traced.
            return string.Format(
                "ShadingPotentialField [{0} voxels | positive potential {1:0.#} | negative potential {2:0.#}]",
                field.Volume?.VoxelCount ?? 0, field.PositiveTotal(), Math.Abs(field.NegativeTotal()));
        }

        /// <summary>
        /// The red/blue/grey colour for a normalised score in [-1, 1]. Kept public so a component
        /// can colour its own preview geometry the same way this Goo does.
        /// </summary>
        public static System.Drawing.Color Color(double normalizedScore)
        {
            if (double.IsNaN(normalizedScore))
            {
                return System.Drawing.Color.FromArgb(160, 160, 160);
            }

            double value = Math.Max(-1.0, Math.Min(1.0, normalizedScore));
            if (value > 0)
            {
                // Pale to saturated red as the benefit rises.
                int fade = (int)Math.Round(200.0 * (1.0 - value));
                return System.Drawing.Color.FromArgb(220, fade, fade);
            }

            if (value < 0)
            {
                int fade = (int)Math.Round(200.0 * (1.0 + value));
                return System.Drawing.Color.FromArgb(fade, fade, 220);
            }

            return System.Drawing.Color.FromArgb(160, 160, 160);
        }

        /// <summary>The drawable points of a field, with their colours. Empty rather than null when there is nothing to draw.</summary>
        public static List<Tuple<Point3d, System.Drawing.Color>> PreviewPoints(ShadingPotentialField field, double wantedSolarPenalty = 1.0)
        {
            List<Tuple<Point3d, System.Drawing.Color>> result = new List<Tuple<Point3d, System.Drawing.Color>>();

            ShadingVolume volume = field?.Volume;
            if (volume == null)
            {
                return result;
            }

            for (int i = 0; i < volume.VoxelCount; i++)
            {
                double normalized = field.NormalizedScore(i, wantedSolarPenalty);
                if (double.IsNaN(normalized) || Math.Abs(normalized) < PreviewCutoff)
                {
                    continue;
                }

                Point3D centre = volume.GetCentre(i);
                if (centre == null)
                {
                    continue;
                }

                result.Add(new Tuple<Point3d, System.Drawing.Color>(Geometry.Rhino.Convert.ToRhino(centre), Color(normalized)));
            }

            return result;
        }

        public BoundingBox ClippingBox
        {
            get
            {
                ShadingVolume volume = Value?.Volume;
                if (volume == null)
                {
                    return BoundingBox.Empty;
                }

                // The eight corners of the studied space, not every point in it: this is asked for
                // on every viewport redraw and a large map has tens of thousands of points.
                double sizeX = volume.CountX * volume.VoxelSize;
                double sizeY = volume.CountY * volume.VoxelSize;
                double sizeZ = volume.CountZ * volume.VoxelSize;

                BoundingBox result = BoundingBox.Empty;
                for (int i = 0; i < 8; i++)
                {
                    Point3D corner = volume.ToWorld(
                        (i & 1) == 0 ? 0 : sizeX,
                        (i & 2) == 0 ? 0 : sizeY,
                        (i & 4) == 0 ? 0 : sizeZ);

                    if (corner != null)
                    {
                        result.Union(Geometry.Rhino.Convert.ToRhino(corner));
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// A concise, copyable statement of what the picture means, plus the two totals that size
        /// the opportunity.
        ///
        /// A string output rather than custom viewport drawing on purpose: a hand-drawn legend has
        /// to survive every camera, display mode and DPI in Rhino, and a broken one is worse than
        /// none. This can be panelled next to the map, read in a report, or ignored.
        /// </summary>
        public static string Legend(ShadingPotentialField field, double wantedSolarPenalty = 1.0)
        {
            System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();
            stringBuilder.AppendLine("RED   SHADE HERE — material here would block solar you asked to block");
            stringBuilder.AppendLine("BLUE  KEEP OPEN  — material here would block solar you asked to keep");
            stringBuilder.AppendLine("grey  Neither — almost no beam passes through, so material here does nothing");

            if (field != null)
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    "Positive shading potential {0:0.#}   Negative shading potential {1:0.#}   ({2} voxels)",
                    field.PositiveTotal(wantedSolarPenalty), Math.Abs(field.NegativeTotal(wantedSolarPenalty)),
                    field.Volume?.VoxelCount ?? 0);

                stringBuilder.AppendLine();
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("THESE TOTALS ARE NOT ENERGY SAVINGS. Each voxel independently records the beam that");
                stringBuilder.AppendLine("would pass through it, and one ray passes through many voxels, so the totals count the");
                stringBuilder.AppendLine("same solar over and over. They rank locations and compare thresholds; they do not say");
                stringBuilder.AppendLine("what a device would save. For that, build one and read VerifyShading, whose kWh are");
                stringBuilder.AppendLine("measured on real geometry.");
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// One shaded box per drawn location, as a single mesh with per-vertex colours.
        ///
        /// Built once and cached. Returns null when there is nothing to draw or when the field is
        /// larger than the voxel preview is willing to handle, in which case the caller falls back
        /// to points.
        /// </summary>
        private Mesh VoxelMesh()
        {
            if (voxelMeshBuilt)
            {
                return voxelMesh;
            }

            voxelMeshBuilt = true;

            ShadingPotentialField field = Value;
            ShadingVolume volume = field?.Volume;
            if (volume == null)
            {
                return null;
            }

            List<Tuple<int, System.Drawing.Color>> drawn = new List<Tuple<int, System.Drawing.Color>>();
            for (int i = 0; i < volume.VoxelCount; i++)
            {
                double normalized = field.NormalizedScore(i, previewWantedSolarPenalty);
                if (double.IsNaN(normalized) || Math.Abs(normalized) < PreviewCutoff)
                {
                    continue;
                }

                drawn.Add(new Tuple<int, System.Drawing.Color>(i, Color(normalized)));
                if (drawn.Count > VoxelPreviewLimit)
                {
                    return null; // too big to draw as solids; the caller shows points instead
                }
            }

            if (drawn.Count == 0)
            {
                return null;
            }

            // Boxes are drawn slightly under size so neighbours read as separate cells rather than
            // fusing into one opaque block.
            double half = 0.45 * volume.VoxelSize;

            Mesh mesh = new Mesh();
            foreach (Tuple<int, System.Drawing.Color> entry in drawn)
            {
                Point3D centre = volume.GetCentre(entry.Item1);
                if (centre == null)
                {
                    continue;
                }

                if (!volume.TryToLocal(centre, out double x, out double y, out double z))
                {
                    continue;
                }

                int baseIndex = mesh.Vertices.Count;
                for (int corner = 0; corner < 8; corner++)
                {
                    Point3D point3D = volume.ToWorld(
                        x + ((corner & 1) == 0 ? -half : half),
                        y + ((corner & 2) == 0 ? -half : half),
                        z + ((corner & 4) == 0 ? -half : half));

                    mesh.Vertices.Add(Geometry.Rhino.Convert.ToRhino(point3D));
                    mesh.VertexColors.Add(entry.Item2);
                }

                // The six faces of the box, in the corner-bit order used above.
                AddQuad(mesh, baseIndex, 0, 2, 3, 1);
                AddQuad(mesh, baseIndex, 4, 5, 7, 6);
                AddQuad(mesh, baseIndex, 0, 1, 5, 4);
                AddQuad(mesh, baseIndex, 2, 6, 7, 3);
                AddQuad(mesh, baseIndex, 0, 4, 6, 2);
                AddQuad(mesh, baseIndex, 1, 3, 7, 5);
            }

            if (mesh.Vertices.Count == 0)
            {
                return null;
            }

            mesh.Normals.ComputeNormals();
            voxelMesh = mesh;
            return voxelMesh;
        }

        private static void AddQuad(Mesh mesh, int baseIndex, int a, int b, int c, int d)
        {
            mesh.Faces.AddFace(baseIndex + a, baseIndex + b, baseIndex + c, baseIndex + d);
        }

        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if (previewMode == ShadingFieldPreviewMode.None)
            {
                return;
            }

            // Voxels draw as meshes; fall back to points when the map is too big for solids.
            if (previewMode == ShadingFieldPreviewMode.Voxels && VoxelMesh() != null)
            {
                return;
            }

            foreach (Tuple<Point3d, System.Drawing.Color> point in PreviewPoints(Value, previewWantedSolarPenalty))
            {
                args.Pipeline.DrawPoint(point.Item1, PointStyle.RoundSimple, PreviewPointSize, point.Item2);
            }
        }

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            // Points mode draws nothing shaded: a solid would imply a surface the field does not
            // have. Voxel mode draws the cells the field actually occupies, which is the one case
            // where a solid is honest.
            if (previewMode != ShadingFieldPreviewMode.Voxels)
            {
                return;
            }

            Mesh mesh = VoxelMesh();
            if (mesh != null)
            {
                args.Pipeline.DrawMeshFalseColors(mesh);
            }
        }
    }

    public class GooShadingPotentialFieldParam : GH_PersistentParam<GooShadingPotentialField>, IGH_PreviewObject
    {
        public override Guid ComponentGuid => new Guid("8d5e1b02-3a44-4c7e-9e6b-2b1f0d54c002");

        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        bool IGH_PreviewObject.Hidden { get; set; }

        bool IGH_PreviewObject.IsPreviewCapable => !VolatileData.IsEmpty;

        BoundingBox IGH_PreviewObject.ClippingBox => Preview_ComputeClippingBox();

        void IGH_PreviewObject.DrawViewportMeshes(IGH_PreviewArgs args) => Preview_DrawMeshes(args);

        void IGH_PreviewObject.DrawViewportWires(IGH_PreviewArgs args) => Preview_DrawWires(args);

        public GooShadingPotentialFieldParam()
            : base(typeof(ShadingPotentialField).Name, typeof(ShadingPotentialField).Name, "Where shading in front of an aperture would help and where it would harm, in kWh", "Params", "SAM")
        {
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GooShadingPotentialField> values)
        {
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Singular(ref GooShadingPotentialField value)
        {
            return GH_GetterResult.cancel;
        }

        public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "Save As...", Menu_SaveAs, VolatileData.AllData(true).Any());

            base.AppendAdditionalMenuItems(menu);
        }

        private void Menu_SaveAs(object sender, EventArgs e)
        {
            Core.Grasshopper.Query.SaveAs(VolatileData);
        }
    }
}
