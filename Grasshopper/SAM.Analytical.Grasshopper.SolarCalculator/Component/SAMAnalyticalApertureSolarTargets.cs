// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Analytical.SolarCalculator;
using SolarQuery = SAM.Analytical.SolarCalculator.Query;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMAnalyticalApertureSolarTargets : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3c9a10-5b28-4e63-9a41-6c0d2e7b1102");

        /// <summary>
        /// The latest version of this component.
        ///
        /// 1.0.1 — 'cellCounts' became 'samplePointCounts'. "Cell" is the internal name for one
        /// analysis sample location and meant nothing to the engineers who tried the node; the two
        /// quantities a user has to keep straight are the SPACING between sample points (_gridSize_,
        /// in metres) and the NUMBER of them on the opening, and the output now says which it is.
        /// The name is not "gridCount": that would read as a count of grids.
        ///
        /// 1.0.2 — added recommendedGridSize, the grid-resolution guidance for the selected
        /// apertures, with a warning when _gridSize_ is coarser than it. The recommendation is
        /// guidance, not a requirement: it never changes the supplied grid, and it does not
        /// guarantee convergence.
        /// </summary>
        public override string LatestComponentVersion => "1.0.2";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.primary;

        public SAMAnalyticalApertureSolarTargets()
          : base("SAMAnalytical.ApertureSolarTargets", "SAMAnalytical.ApertureSolarTargets",
              "SUMMARY\nPicks the windows and doors that can receive sun and prepares each one for solar analysis: the opening face with its OUTWARD direction resolved from the model's adjacency (never trusted to the way the surface happens to be drawn), its orientation, and the grid of sample points the calculation uses.\n\nThis is the first node of the shading workflow. The shading-design nodes use the apertureSolarTarget; ApertureIrradiance uses the corresponding apertureGuid to select the same aperture.\n\nINPUTS\n  _analyticalModel — the SAM Analytical Model.\n  _apertures_ — the apertures to analyse, as SAM Apertures or their Guids. LEAVE EMPTY for every external sun-exposed aperture in the model.\n  _gridSize_ — SPACING between the sample points across each opening, m. Default 0.5 m. It sets the finest shading detail the analysis can resolve, so keep the same value for the whole workflow.\n\nGRIDSIZE VS SAMPLEPOINTCOUNT. GridSize is a DISTANCE in metres: how far apart the analysis samples the opening. samplePointCount is a COUNT: how many sample locations that spacing produced on that opening. Halving GridSize roughly quadruples samplePointCount, and the run gets slower in proportion.\n\nOUTPUTS\n  apertureSolarTargets — one target per analysed aperture. Wire into ShadingPotentialField, RationaliseShading and VerifyShading; feed the apertureGuids output into ApertureIrradiance's _apertures_ input.\n  apertureGuids — the aperture identity behind each target.\n  azimuths — compass direction each opening faces, degrees (0 north, 90 east, 180 south, 270 west).\n  tilts — angle from horizontal, degrees (90 = vertical window).\n  areas — gross opening area, m².\n  samplePointCounts — number of analysis sample locations on each opening.\n  gridSize — the grid size used, so downstream nodes can be wired from it rather than retyped.\n  recommendedGridSize — the grid-resolution guidance for these apertures, when _gridSize_ is coarser than it a warning appears on this node. GUIDANCE, NOT A REQUIREMENT: the calculation keeps _gridSize_ as supplied.\n  count — number of targets.\n\nMULTIPLE WINDOWS\nThis node emits a LIST of targets, and every shading-design node downstream takes ONE target. Grasshopper therefore runs them once per target and keeps each aperture's results in its own branch — you do not need to graft anything for the ordinary 'analyse these ten windows' case. Each result carries its own apertureGuid and azimuth so a batch stays readable.\n\nNOTES\nApertures in internal walls are never analysed: they receive no direct sun and any result would be meaningless. If one is asked for by name it is reported, not silently dropped.\nAn aperture too small to hold a single sample point at the chosen grid produces no target; reduce _gridSize_ if you need it.\n\nEXAMPLE\nAnalyticalModel → ApertureSolarTargets (leave _apertures_ empty for every sun-exposed window).\napertureSolarTargets → ShadingPotentialField._apertureSolarTarget, SolarControlProfile._apertureSolarTarget, RationaliseShading._apertureSolarTarget or VerifyShading._apertureSolarTarget (one window at a time), and → RationaliseAwningGroup._apertureSolarTargets, AssembleShadingSchemes._apertureSolarTargets or ShadingOperation._apertureSolarTargets (the whole list). Each window's results stay in their own branch — no grafting needed.\napertureGuids → ApertureIrradiance._apertures_.\nCheck the azimuths against the model before running anything expensive.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel", NickName = "_analyticalModel", Description = "SAM Analytical Model", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_GenericObject apertures = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_apertures_", NickName = "_apertures_", Description = "Apertures to analyse, as SAM Apertures or Guids.\nEmpty = every external sun-exposed aperture in the model", Access = GH_ParamAccess.list, Optional = true };
                result.Add(new GH_SAMParam(apertures, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number gridSize = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_gridSize_", NickName = "_gridSize_", Description = "SPACING between the analysis sample points across each opening [m].\n\nALLOWED: greater than zero, and not finer than about 0.032 m - below that a sample cell is smaller than the geometry area tolerance and NO samples can be produced at all.\nHalving it roughly quadruples the sample count and the runtime.\nDefault 0.5 m", Access = GH_ParamAccess.item };
                gridSize.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(gridSize, ParamVisibility.Binding));

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooApertureSolarTargetParam() { Name = "apertureSolarTargets", NickName = "apertureSolarTargets", Description = "Apertures prepared for solar analysis", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "apertureGuids", NickName = "apertureGuids", Description = "Aperture Guid behind each target", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "azimuths", NickName = "azimuths", Description = "Compass direction each opening faces [°]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tilts", NickName = "tilts", Description = "Angle from horizontal [°]. 90 = vertical", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "areas", NickName = "areas", Description = "Gross opening area [m²]", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "samplePointCounts", NickName = "samplePointCounts", Description = "Number of analysis sample locations on each opening.\nTheir SPACING is _gridSize_ [m]", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "gridSize", NickName = "gridSize", Description = "The analysis grid size used [m]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "recommendedGridSize", NickName = "recommendedGridSize", Description = "Recommended analysis grid size for the selected apertures [m] — the finer of the design-grade resolution guidance (0.25 m) and half the shortest side of the smallest selected aperture, never below the smallest usable grid.\n\nGUIDANCE, NOT A REQUIREMENT: the calculation uses _gridSize_ as supplied and this output never changes it. It does not guarantee convergence — confirm by re-running at a finer grid and comparing.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "count", NickName = "count", Description = "Number of targets", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        /// <summary>The requested aperture Guids, from Apertures, Guids or Guid strings.</summary>
        internal static List<Guid> ApertureGuids(IEnumerable<GH_ObjectWrapper> objectWrappers, out int unrecognised)
        {
            unrecognised = 0;

            List<Guid> result = new List<Guid>();
            if (objectWrappers == null)
            {
                return result;
            }

            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                object @object = Query.Unwrap(objectWrapper);
                if (@object == null)
                {
                    continue;
                }

                if (@object is Aperture aperture)
                {
                    result.Add(aperture.Guid);
                    continue;
                }

                if (@object is Guid guid)
                {
                    result.Add(guid);
                    continue;
                }

                if (@object is string text && Guid.TryParse(text, out Guid parsed))
                {
                    result.Add(parsed);
                    continue;
                }

                unrecognised++;
            }

            return result;
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index = Params.IndexOfInputParam("_analyticalModel");
            AnalyticalModel analyticalModel = null;
            if (index == -1 || !dataAccess.GetData(index, ref analyticalModel) || analyticalModel == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply a valid SAM AnalyticalModel.");
                return;
            }

            double gridSize = 0.5;
            index = Params.IndexOfInputParam("_gridSize_");
            if (index != -1)
            {
                double gridSize_Temp = gridSize;
                if (dataAccess.GetData(index, ref gridSize_Temp) && !double.IsNaN(gridSize_Temp))
                {
                    gridSize = gridSize_Temp;
                }
            }

            // Zero, negative, NaN — and the case that used to fail silently: a grid so fine that
            // every sample cell falls below the geometry area tolerance and NOTHING can be built.
            if (SolarQuery.GridSizeValidity(gridSize, out string gridSizeMessage) != GridSizeValidity.Valid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, gridSizeMessage);
                return;
            }

            List<Guid> apertureGuids = new List<Guid>();
            index = Params.IndexOfInputParam("_apertures_");
            if (index != -1)
            {
                List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
                if (dataAccess.GetDataList(index, objectWrappers))
                {
                    apertureGuids = ApertureGuids(objectWrappers, out int unrecognised);
                    if (unrecognised > 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("{0} item(s) on _apertures_ were neither a SAM Aperture nor an aperture Guid and were ignored.", unrecognised));
                    }
                }
            }

            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(
                apertureGuids.Count == 0 ? null : apertureGuids, gridSize, out string targetsMessage);

            if (targets == null || targets.Count == 0)
            {
                // The builder knows WHICH of the several ways to get nothing actually happened, so
                // its own sentence is used rather than a guess assembled from the inputs here.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, targetsMessage ?? (apertureGuids.Count == 0
                    ? "No valid external sun-exposed apertures were found in this model."
                    : "None of the requested apertures could be analysed. Apertures in internal walls are excluded."));
                return;
            }

            if (apertureGuids.Count != 0)
            {
                List<string> missing = new List<string>();
                foreach (Guid apertureGuid in apertureGuids)
                {
                    if (targets.Find(x => x.ApertureGuid == apertureGuid) == null)
                    {
                        missing.Add(apertureGuid.ToString());
                    }
                }

                if (missing.Count != 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("{0} requested aperture(s) produced no target and were excluded: an aperture in an internal wall, or one too small for a single sample point at this grid size. First: {1}.", missing.Count, missing[0]));
                }
            }

            // The sample lattice starts at the opening's bounding-box corner, so a grid that does
            // not divide the opening leaves an edge strip. Usually that strip is just another cell;
            // when it is too thin to clear the geometry area tolerance it is discarded, and the
            // opening is then sampled slightly smaller than it is. Percentages are unaffected —
            // they are ratios over the same samples — but absolute kWh scale with the sampled area,
            // so an invisible few per cent would be a quiet bias on every energy this node feeds.
            double worstCoverage = 1.0;
            ApertureSolarTarget worstTarget = null;
            foreach (ApertureSolarTarget target in targets)
            {
                double coverage = target.SampledAreaFraction;
                if (!double.IsNaN(coverage) && coverage < worstCoverage)
                {
                    worstCoverage = coverage;
                    worstTarget = target;
                }
            }

            if (worstTarget != null && worstCoverage < 0.999)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "A grid size of {0:0.####} m does not divide one or more openings, and the leftover edge strip is too thin to be sampled: the worst-covered opening is analysed over {1:0.#}% of its area ({2:0.###} m² of {3:0.###} m²). Percentages are unaffected, but absolute energies for that opening will read about {4:0.#}% low. Choose a grid size that divides the opening — or a slightly coarser one — to remove this.",
                    gridSize, 100.0 * worstCoverage, worstTarget.SampledArea, worstTarget.GrossArea, 100.0 * (1.0 - worstCoverage)));
            }

            // Grid-resolution guidance for the SELECTED aperture set. One shared number, from the
            // governing (smallest) aperture, so a single grid stays valid for the whole workflow.
            // It never changes the calculation: a coarse grid keeps running and simply warns.
            double recommendedGridSize = SolarQuery.RecommendedGridSize(targets);
            if (!double.IsNaN(recommendedGridSize) && SolarQuery.CoarserThanRecommended(gridSize, recommendedGridSize, out string coarseMessage))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, coarseMessage);
            }

            index = Params.IndexOfOutputParam("apertureSolarTargets");
            if (index != -1)
            {
                dataAccess.SetDataList(index, targets.ConvertAll(x => new GooApertureSolarTarget(x)));
            }

            index = Params.IndexOfOutputParam("apertureGuids");
            if (index != -1)
            {
                dataAccess.SetDataList(index, targets.ConvertAll(x => x.ApertureGuid.ToString()));
            }

            index = Params.IndexOfOutputParam("azimuths");
            if (index != -1)
            {
                dataAccess.SetDataList(index, targets.ConvertAll(x => x.Azimuth));
            }

            index = Params.IndexOfOutputParam("tilts");
            if (index != -1)
            {
                dataAccess.SetDataList(index, targets.ConvertAll(x => x.Tilt));
            }

            index = Params.IndexOfOutputParam("areas");
            if (index != -1)
            {
                dataAccess.SetDataList(index, targets.ConvertAll(x => x.GrossArea));
            }

            // The pre-1.0.1 name is still honoured so a script placed before the rename keeps
            // producing numbers instead of silently emptying that wire.
            index = Params.IndexOfOutputParam("samplePointCounts");
            if (index == -1)
            {
                index = Params.IndexOfOutputParam("cellCounts");
            }

            if (index != -1)
            {
                dataAccess.SetDataList(index, targets.ConvertAll(x => x.CellCount));
            }

            index = Params.IndexOfOutputParam("gridSize");
            if (index != -1)
            {
                dataAccess.SetData(index, gridSize);
            }

            index = Params.IndexOfOutputParam("recommendedGridSize");
            if (index != -1)
            {
                dataAccess.SetData(index, recommendedGridSize);
            }

            index = Params.IndexOfOutputParam("count");
            if (index != -1)
            {
                dataAccess.SetData(index, targets.Count);
            }
        }
    }
}
