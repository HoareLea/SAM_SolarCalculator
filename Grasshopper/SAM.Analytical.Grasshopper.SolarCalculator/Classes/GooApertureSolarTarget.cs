// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
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
    /// <summary>
    /// An aperture prepared for solar analysis: the opening with its outward normal resolved, its
    /// orientation, and the analysis grid the calculation samples it on.
    ///
    /// The preview draws the opening and a short arrow along the OUTWARD normal, which is the one
    /// thing worth checking by eye before running anything: if the arrow points into the room, the
    /// model's aperture is the problem, not the analysis.
    /// </summary>
    public class GooApertureSolarTarget : GooJSAMObject<ApertureSolarTarget>, IGH_PreviewData
    {
        public GooApertureSolarTarget()
            : base()
        {
        }

        public GooApertureSolarTarget(ApertureSolarTarget apertureSolarTarget)
            : base(apertureSolarTarget)
        {
        }

        public override IGH_Goo Duplicate()
        {
            return new GooApertureSolarTarget(Value);
        }

        public override string ToString()
        {
            ApertureSolarTarget target = Value;
            if (target == null)
            {
                return typeof(ApertureSolarTarget).Name;
            }

            return string.Format("ApertureSolarTarget [azimuth {0:0.#}°, tilt {1:0.#}°, {2:0.##} m², {3} cells]", target.Azimuth, target.Tilt, target.GrossArea, target.CellCount);
        }

        public BoundingBox ClippingBox
        {
            get
            {
                Face3D face3D = Value?.Face3D;
                if (face3D == null)
                {
                    return BoundingBox.Empty;
                }

                Brep brep = Geometry.Rhino.Convert.ToRhino_Brep(face3D);
                return brep == null ? BoundingBox.Empty : brep.GetBoundingBox(false);
            }
        }

        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            ApertureSolarTarget target = Value;
            Face3D face3D = target?.Face3D;
            if (face3D == null)
            {
                return;
            }

            Brep brep = Geometry.Rhino.Convert.ToRhino_Brep(face3D);
            if (brep != null)
            {
                args.Pipeline.DrawBrepWires(brep, args.Color);
            }

            // The outward normal, drawn a quarter of the aperture's own size so it reads at any scale.
            Point3D centroid = face3D.GetCentroid();
            Vector3D outward = target.OutwardNormal;
            if (centroid == null || outward == null)
            {
                return;
            }

            double length = Math.Max(0.25, 0.25 * Math.Sqrt(Math.Max(0.0, target.GrossArea)));
            Point3d start = Geometry.Rhino.Convert.ToRhino(centroid);
            Point3d end = new Point3d(start.X + outward.X * length, start.Y + outward.Y * length, start.Z + outward.Z * length);
            args.Pipeline.DrawArrow(new Line(start, end), args.Color);
        }

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            Face3D face3D = Value?.Face3D;
            if (face3D == null)
            {
                return;
            }

            Mesh mesh = Geometry.Rhino.Convert.ToRhino_Mesh(face3D);
            if (mesh != null)
            {
                args.Pipeline.DrawMeshShaded(mesh, args.Material);
            }
        }

        public override bool CastTo<Y>(ref Y target)
        {
            ApertureSolarTarget apertureSolarTarget = Value;
            if (apertureSolarTarget != null)
            {
                Face3D face3D = apertureSolarTarget.Face3D;
                if (face3D != null)
                {
                    if (typeof(Y).IsAssignableFrom(typeof(GH_Brep)))
                    {
                        target = (Y)(object)new GH_Brep(Geometry.Rhino.Convert.ToRhino_Brep(face3D));
                        return true;
                    }

                    if (typeof(Y).IsAssignableFrom(typeof(GH_Mesh)))
                    {
                        target = (Y)(object)new GH_Mesh(Geometry.Rhino.Convert.ToRhino_Mesh(face3D));
                        return true;
                    }
                }
            }

            return base.CastTo(ref target);
        }
    }

    public class GooApertureSolarTargetParam : GH_PersistentParam<GooApertureSolarTarget>, IGH_PreviewObject
    {
        public override Guid ComponentGuid => new Guid("8d5e1b02-3a44-4c7e-9e6b-2b1f0d54c001");

        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        bool IGH_PreviewObject.Hidden { get; set; }

        bool IGH_PreviewObject.IsPreviewCapable => !VolatileData.IsEmpty;

        BoundingBox IGH_PreviewObject.ClippingBox => Preview_ComputeClippingBox();

        void IGH_PreviewObject.DrawViewportMeshes(IGH_PreviewArgs args) => Preview_DrawMeshes(args);

        void IGH_PreviewObject.DrawViewportWires(IGH_PreviewArgs args) => Preview_DrawWires(args);

        public GooApertureSolarTargetParam()
            : base(typeof(ApertureSolarTarget).Name, typeof(ApertureSolarTarget).Name, "Aperture prepared for solar analysis: opening, outward normal, orientation and analysis grid", "Params", "SAM")
        {
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GooApertureSolarTarget> values)
        {
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Singular(ref GooApertureSolarTarget value)
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
