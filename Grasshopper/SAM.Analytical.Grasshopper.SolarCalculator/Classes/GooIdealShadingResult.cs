// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Analytical.SolarCalculator;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    /// <summary>
    /// The ideal shading shape for one aperture: the region of the space in front of it worth
    /// filling with material, at a chosen threshold.
    ///
    /// The mesh is DISPLAY geometry. The numbers on the result — captured benefit, projected area,
    /// depth, region counts — come from the field, not from the mesh, and remain valid when the
    /// mesh is missing. Do not treat the mesh as the verified performance geometry: it can
    /// intercept measurably less solar than the region it draws.
    /// </summary>
    public class GooIdealShadingResult : GooJSAMObject<IdealShadingResult>, IGH_PreviewData
    {
        public GooIdealShadingResult()
            : base()
        {
        }

        public GooIdealShadingResult(IdealShadingResult idealShadingResult)
            : base(idealShadingResult)
        {
        }

        public override IGH_Goo Duplicate()
        {
            return new GooIdealShadingResult(Value);
        }

        public override string ToString()
        {
            IdealShadingResult result = Value;
            if (result == null)
            {
                return typeof(IdealShadingResult).Name;
            }

            // "captures X % of the benefit" invited the reading "this shape blocks X % of the
            // unwanted solar". It does not: the fraction is of the FIELD'S positive potential, a sum
            // over voxels, and this shape is display geometry that was never traced.
            return string.Format("IdealShadingResult [{0} regions, {1} voxels, holds {2:0.#} % of the positive shading potential — not verified performance]", result.RegionCount, result.SelectedVoxelCount, 100.0 * result.CapturedPotentialFraction);
        }

        private Mesh RhinoMesh()
        {
            Geometry.Spatial.Mesh3D mesh3D = Value?.Mesh;
            return mesh3D == null ? null : Geometry.Rhino.Convert.ToRhino(mesh3D);
        }

        public BoundingBox ClippingBox
        {
            get
            {
                Mesh mesh = RhinoMesh();
                return mesh == null ? BoundingBox.Empty : mesh.GetBoundingBox(false);
            }
        }

        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            Mesh mesh = RhinoMesh();
            if (mesh != null)
            {
                args.Pipeline.DrawMeshWires(mesh, args.Color);
            }
        }

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            Mesh mesh = RhinoMesh();
            if (mesh != null)
            {
                args.Pipeline.DrawMeshShaded(mesh, args.Material);
            }
        }

        public override bool CastTo<Y>(ref Y target)
        {
            if (typeof(Y).IsAssignableFrom(typeof(GH_Mesh)))
            {
                Mesh mesh = RhinoMesh();
                if (mesh != null)
                {
                    target = (Y)(object)new GH_Mesh(mesh);
                    return true;
                }
            }

            return base.CastTo(ref target);
        }
    }

    public class GooIdealShadingResultParam : GH_PersistentParam<GooIdealShadingResult>, IGH_PreviewObject
    {
        public override Guid ComponentGuid => new Guid("8d5e1b02-3a44-4c7e-9e6b-2b1f0d54c003");

        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        bool IGH_PreviewObject.Hidden { get; set; }

        bool IGH_PreviewObject.IsPreviewCapable => !VolatileData.IsEmpty;

        BoundingBox IGH_PreviewObject.ClippingBox => Preview_ComputeClippingBox();

        void IGH_PreviewObject.DrawViewportMeshes(IGH_PreviewArgs args) => Preview_DrawMeshes(args);

        void IGH_PreviewObject.DrawViewportWires(IGH_PreviewArgs args) => Preview_DrawWires(args);

        public GooIdealShadingResultParam()
            : base(typeof(IdealShadingResult).Name, typeof(IdealShadingResult).Name, "The region in front of an aperture worth shading, with its display mesh", "Params", "SAM")
        {
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GooIdealShadingResult> values)
        {
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Singular(ref GooIdealShadingResult value)
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
