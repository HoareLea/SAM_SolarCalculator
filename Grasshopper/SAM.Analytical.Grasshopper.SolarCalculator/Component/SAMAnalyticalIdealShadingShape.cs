// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Analytical.SolarCalculator;
using SolarCreate = SAM.Analytical.SolarCalculator.Create;
using SolarQuery = SAM.Analytical.SolarCalculator.Query;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMAnalyticalIdealShadingShape : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3c9a10-5b28-4e63-9a41-6c0d2e7b1105");

        /// <summary>
        /// The latest version of this component.
        ///
        /// 1.0.1 — capturedFraction is renamed capturedPotentialFraction. Manual testing read
        /// "captures 90 % of the benefit" as "blocks 90 % of the unwanted solar"; it is a share of
        /// the field's positive potential, a sum over voxels, and this shape was never traced. The
        /// quantity is unchanged. _wantedSolarPenalty_ now refuses negative values.
        /// </summary>
        public override string LatestComponentVersion => "1.0.1";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        public SAMAnalyticalIdealShadingShape()
          : base("SAMAnalytical.IdealShadingShape", "SAMAnalytical.IdealShadingShape",
              "SUMMARY\nTurns the shading potential map into a SHAPE: the region in front of the window most worth filling with material, at a threshold you choose. It is a design aid — the shape it draws is not a buildable device, and RationaliseShading is what produces one.\n\nINPUTS\n  _shadingPotentialField — the map, from ShadingPotentialField.\n  _thresholdMethod_ — how the cut level is chosen:\n    CumulativeCapture (default) — keep the locations that together deliver the share of the benefit asked for in _threshold_.\n    MaxFraction — keep everything above a share of the single best location.\n    Absolute — _threshold_ is a level in kWh.\n  _threshold_ — the share (0-1) or the kWh level. Default 0.9, i.e. keep 90 % of the available benefit.\n  _wantedSolarPenalty_ — how many kWh of unwanted solar blocked is worth one kWh of wanted solar lost. Default 1.0. Use the same value as the map and the device.\n  _requireFacadeContact_ — drop pieces that float free of the facade. Default false.\n  _keepLargestRegionOnly_ — keep only the biggest connected piece. Default false.\n\nOUTPUTS\n  idealShadingResult — the result object.\n  mesh — DISPLAY geometry only, see the note below.\n  threshold — the kWh level actually used.\n  capturedPotentialFraction — the share of the map's POSITIVE SHADING POTENTIAL this shape holds, 0-1. NOT a share of solar energy, and NOT what a device here would block: both parts of the ratio are sums over map locations, and one ray is counted at every location along its path.\n  voxelCount / regionCount / regionSizes — size and connectedness of the selected region.\n  projectedArea [m²], enclosedVolume [m³], maxProjectionDepth [m] — take-off quantities of the region.\n  meshNote — why no mesh was produced, when there is none.\n\nNOTES\nDISPLAY GEOMETRY VS PERFORMANCE. The mesh is drawn from the selected region for viewing and take-off. It is NOT the geometry the performance numbers describe: in testing the mesh intercepted materially less solar than the region it represents. Judge shading with VerifyShading on a real device, never by the look of this mesh.\nAn empty result is a legitimate answer: it means nothing in front of this window is worth shading under the brief you gave.\nThe ideal shape over-shades on purpose — it blocks everything unwanted and pays for it in lost winter sun. That is why it beats the buildable devices on paper and is not a design.\n\nEXAMPLE\nShadingPotentialField → IdealShadingShape (_threshold_ 0.9) to see WHERE the problem is, then RationaliseShading to get something buildable.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooShadingPotentialFieldParam() { Name = "_shadingPotentialField", NickName = "_shadingPotentialField", Description = "The shading potential map, from SAMAnalytical.ShadingPotentialField", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_thresholdMethod_", NickName = "_thresholdMethod_", Description = "CumulativeCapture (default), MaxFraction or Absolute", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number threshold = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_threshold_", NickName = "_threshold_", Description = "Share of the benefit to keep (0-1) for CumulativeCapture and MaxFraction, or the level in kWh for Absolute.\nDefault 0.9", Access = GH_ParamAccess.item };
                threshold.SetPersistentData(0.9);
                result.Add(new GH_SAMParam(threshold, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number wantedSolarPenalty = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_wantedSolarPenalty_", NickName = "_wantedSolarPenalty_", Description = "How many kWh of unwanted solar blocked are worth one kWh of wanted solar lost. DIMENSIONLESS WEIGHT.\n\nALLOWED: any finite number from 0 upwards. There is no upper limit.\nNORMAL: 0.5 to 2.0.\nDEFAULT: 1.0.\n\nRAISE IT and the selected shape shrinks away from the region that carries wanted solar.\nNEGATIVE IS REFUSED: it would select the region that destroys wanted solar.\n\nUse the SAME value as the map it came from.", Access = GH_ParamAccess.item };
                wantedSolarPenalty.SetPersistentData(1.0);
                result.Add(new GH_SAMParam(wantedSolarPenalty, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean requireFacadeContact = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_requireFacadeContact_", NickName = "_requireFacadeContact_", Description = "Drop pieces that do not reach back to the facade.\nDefault false", Access = GH_ParamAccess.item };
                requireFacadeContact.SetPersistentData(false);
                result.Add(new GH_SAMParam(requireFacadeContact, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean keepLargestRegionOnly = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_keepLargestRegionOnly_", NickName = "_keepLargestRegionOnly_", Description = "Keep only the largest connected piece.\nDefault false", Access = GH_ParamAccess.item };
                keepLargestRegionOnly.SetPersistentData(false);
                result.Add(new GH_SAMParam(keepLargestRegionOnly, ParamVisibility.Voluntary));

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooIdealShadingResultParam() { Name = "idealShadingResult", NickName = "idealShadingResult", Description = "The ideal shading region and its numbers", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Mesh() { Name = "mesh", NickName = "mesh", Description = "DISPLAY geometry of the region. Not the verified performance geometry", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "threshold", NickName = "threshold", Description = "The kWh level actually used", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "capturedPotentialFraction", NickName = "capturedPotentialFraction", Description = "Share of the map's POSITIVE SHADING POTENTIAL held by this region, 0-1.\n\nNOT a share of solar energy. Both the numerator and the denominator are sums over map locations, and one solar ray is counted at every location along its path — so '90 %' means this region holds 90 % of the map's positive potential, NOT that a device built here would block 90 % of the unwanted solar. It will not: this region is a volume, a device is a surface, and this shape was never ray-traced.\n\nUse it to choose a threshold and to compare two thresholds on the same map. For what a device blocks, build one and read VerifyShading", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "voxelCount", NickName = "voxelCount", Description = "Number of selected locations", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "regionCount", NickName = "regionCount", Description = "Number of separate connected pieces", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "regionSizes", NickName = "regionSizes", Description = "Size of each connected piece, largest first", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "projectedArea", NickName = "projectedArea", Description = "Footprint of the region on the facade [m²]", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "enclosedVolume", NickName = "enclosedVolume", Description = "Volume of the region [m³]", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "maxProjectionDepth", NickName = "maxProjectionDepth", Description = "Furthest the region projects from the facade [m]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "meshNote", NickName = "meshNote", Description = "Why no mesh was produced, when there is none", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                return result.ToArray();
            }
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index = Params.IndexOfInputParam("_shadingPotentialField");
            ShadingPotentialField field = null;
            if (index == -1 || !dataAccess.GetData(index, ref field) || field == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply a shading potential map from SAMAnalytical.ShadingPotentialField.");
                return;
            }

            ShadingThresholdMethod thresholdMethod = ShadingThresholdMethod.CumulativeCapture;
            index = Params.IndexOfInputParam("_thresholdMethod_");
            if (index != -1)
            {
                global::Grasshopper.Kernel.Types.GH_ObjectWrapper objectWrapper = null;
                if (dataAccess.GetData(index, ref objectWrapper) && objectWrapper?.Value != null)
                {
                    if (!Query.TryGetEnum(objectWrapper, out thresholdMethod))
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_thresholdMethod_ was not recognised. Use CumulativeCapture, MaxFraction or Absolute.");
                        return;
                    }
                }
            }

            double threshold = 0.9;
            index = Params.IndexOfInputParam("_threshold_");
            if (index != -1)
            {
                double threshold_Temp = threshold;
                if (dataAccess.GetData(index, ref threshold_Temp) && !double.IsNaN(threshold_Temp))
                {
                    threshold = threshold_Temp;
                }
            }

            if (thresholdMethod != ShadingThresholdMethod.Absolute && (threshold < 0 || threshold > 1))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_threshold_ must be between 0 and 1 for CumulativeCapture and MaxFraction. Use the Absolute method to give a level in kWh.");
                return;
            }

            double wantedSolarPenalty = 1.0;
            index = Params.IndexOfInputParam("_wantedSolarPenalty_");
            if (index != -1)
            {
                double wantedSolarPenalty_Temp = wantedSolarPenalty;
                if (dataAccess.GetData(index, ref wantedSolarPenalty_Temp) && !double.IsNaN(wantedSolarPenalty_Temp))
                {
                    wantedSolarPenalty = wantedSolarPenalty_Temp;
                }
            }

            if (ShadingObjective.Validity(wantedSolarPenalty) == PenaltyValidity.Invalid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format(
                    "_wantedSolarPenalty_ must be a finite number of zero or more (it is {0}). It is a dimensionless weight: 1.0 trades one kWh of wanted solar for one kWh of unwanted solar blocked. A negative value would select the region that destroys wanted solar.",
                    wantedSolarPenalty));
                return;
            }

            bool requireFacadeContact = false;
            index = Params.IndexOfInputParam("_requireFacadeContact_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref requireFacadeContact);
            }

            bool keepLargestRegionOnly = false;
            index = Params.IndexOfInputParam("_keepLargestRegionOnly_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref keepLargestRegionOnly);
            }

            IdealShadingResult idealShadingResult = SolarCreate.IdealShadingResult(field, thresholdMethod, threshold, wantedSolarPenalty, requireFacadeContact, keepLargestRegionOnly);
            if (idealShadingResult == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No ideal shading shape could be extracted from this map.");
                return;
            }

            if (idealShadingResult.SelectedVoxelCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Nothing in front of this window is worth shading under the brief and threshold given.");
            }
            else if (!idealShadingResult.HasMesh)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No display mesh was produced: " + (idealShadingResult.MeshFailureReason ?? "reason not recorded") + ". The numbers below are unaffected.");
            }

            index = Params.IndexOfOutputParam("idealShadingResult");
            if (index != -1)
            {
                dataAccess.SetData(index, new GooIdealShadingResult(idealShadingResult));
            }

            index = Params.IndexOfOutputParam("mesh");
            if (index != -1)
            {
                Geometry.Spatial.Mesh3D mesh3D = idealShadingResult.Mesh;
                dataAccess.SetData(index, mesh3D == null ? null : Geometry.Rhino.Convert.ToRhino(mesh3D));
            }

            index = Params.IndexOfOutputParam("threshold");
            if (index != -1)
            {
                dataAccess.SetData(index, idealShadingResult.Threshold);
            }

            index = Params.IndexOfOutputParam("capturedPotentialFraction");
            if (index != -1)
            {
                dataAccess.SetData(index, idealShadingResult.CapturedPotentialFraction);
            }

            index = Params.IndexOfOutputParam("voxelCount");
            if (index != -1)
            {
                dataAccess.SetData(index, idealShadingResult.SelectedVoxelCount);
            }

            index = Params.IndexOfOutputParam("regionCount");
            if (index != -1)
            {
                dataAccess.SetData(index, idealShadingResult.RegionCount);
            }

            index = Params.IndexOfOutputParam("regionSizes");
            if (index != -1)
            {
                dataAccess.SetDataList(index, idealShadingResult.RegionSizes);
            }

            index = Params.IndexOfOutputParam("projectedArea");
            if (index != -1)
            {
                dataAccess.SetData(index, idealShadingResult.ProjectedArea);
            }

            index = Params.IndexOfOutputParam("enclosedVolume");
            if (index != -1)
            {
                dataAccess.SetData(index, idealShadingResult.EnclosedVolume);
            }

            index = Params.IndexOfOutputParam("maxProjectionDepth");
            if (index != -1)
            {
                dataAccess.SetData(index, idealShadingResult.MaxProjectionDepth);
            }

            index = Params.IndexOfOutputParam("meshNote");
            if (index != -1)
            {
                dataAccess.SetData(index, idealShadingResult.MeshFailureReason);
            }
        }
    }
}

