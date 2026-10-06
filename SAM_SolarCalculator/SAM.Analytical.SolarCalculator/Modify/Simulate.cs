// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Modify
    {
        // Aperture coverage-result name suffixes. MUST mirror SAM.Analytical.Tas.Query.Sufix so the
        // SAM-recomputed "… -pane"/"… -frame" results match the TAS-import names that UpdateShading
        // reads via Name.EndsWith(...). Duplicated (not referenced) because SAM_SolarCalculator cannot
        // depend on SAM_Tas — that would be a circular dependency.
        private const string PaneSufix = "-pane";
        private const string FrameSufix = "-frame";

        public static List<SolarFaceSimulationResult> Simulate(this AnalyticalModel analyticalModel, IEnumerable<DateTime> dateTimes, bool merge = false, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, double sampleSize = double.NaN)
        {
            if(analyticalModel == null || dateTimes == null)
            {
                return null;
            }

            Core.Location location = analyticalModel.Location;
            if (location == null)
            {
                return null;
            }

            Dictionary<DateTime, Vector3D> directionDictionary = new Dictionary<DateTime, Vector3D>();
            foreach (DateTime dateTime in dateTimes)
            {
                directionDictionary[dateTime] = Geometry.SolarCalculator.Query.SunDirection(location, dateTime, false);
            }

            return Simulate(analyticalModel, directionDictionary, merge, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize);
        }

        public static List<SolarFaceSimulationResult> Simulate(this AnalyticalModel analyticalModel, Dictionary<DateTime, Vector3D> directionDictionary, bool merge = false, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, double sampleSize = double.NaN)
        {
            if (analyticalModel == null || directionDictionary == null)
            {
                return null;
            }

            SolarModel solarModel = Convert.ToSAM_SolarModel(analyticalModel);
            if (solarModel == null)
            {
                return null;
            }

            List<SolarFaceSimulationResult> result = null;

            List<SolarFaceSimulationResult> solarFaceSimulationResults = Weather.SolarCalculator.Modify.Simulate(solarModel, directionDictionary, true, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize);
            if (solarFaceSimulationResults != null && solarFaceSimulationResults.Count != 0)
            {
                result = new List<SolarFaceSimulationResult>();

                List<Panel> panels = analyticalModel.GetPanels();
                foreach (SolarFaceSimulationResult solarFaceSimulationResult in solarFaceSimulationResults)
                {
                    Guid guid = Guid.Empty;

                    Panel panel = panels.Find(x => x.Guid.ToString().Equals(solarFaceSimulationResult.Reference));
                    if (panel != null)
                    {
                        guid = panel.Guid;
                    }

                    if(!merge)
                    {
                        analyticalModel.AddResult<Panel>(solarFaceSimulationResult, guid);
                        result.Add(solarFaceSimulationResult);
                        continue;
                    }

                    List<SolarFaceSimulationResult> solarFaceSimulationResuls_Panel = analyticalModel.GetRelatedObjects<SolarFaceSimulationResult>(panel);
                    if(solarFaceSimulationResuls_Panel == null || solarFaceSimulationResuls_Panel.Count == 0)
                    {
                        analyticalModel.AddResult<Panel>(solarFaceSimulationResult, guid);
                        result.Add(solarFaceSimulationResult);
                        continue;
                    }

                    foreach(SolarFaceSimulationResult solarFaceSimulationResult_Panel in solarFaceSimulationResuls_Panel)
                    {
                        SolarFaceSimulationResult solarFaceSimulationResult_New = solarFaceSimulationResult_Panel.Merge(solarFaceSimulationResult);
                        if(solarFaceSimulationResult_New == null)
                        {
                            continue;
                        }

                        analyticalModel.AddResult<Panel>(solarFaceSimulationResult_New, guid);
                        result.Add(solarFaceSimulationResult_New);
                    }
                }
            }

            // merge == true: return the merged results that were actually attached to the model.
            // (Previously this returned solarFaceSimulationResults — the un-merged list — making
            // result a dead store and handing callers data that was never attached.)
            if (merge)
            {
                return result ?? new List<SolarFaceSimulationResult>();
            }

            return solarFaceSimulationResults;
        }

        /// <summary>
        /// Coverage-only simulate: emits <see cref="SolarCoverageSimulationResult"/> instead of
        /// <see cref="SolarFaceSimulationResult"/> and attaches them to the AnalyticalModel.
        /// Designed for apples-to-apples comparison against TAS-imported shade coverage.
        /// </summary>
        /// <remarks>
        /// Binary-compatibility overload: preserves the original pre-<c>useModelSolarModel</c>
        /// signature so plugins/apps already compiled against it keep resolving at runtime
        /// (appending the optional flag in-place would be a binary break — MissingMethodException).
        /// Delegates with <c>useModelSolarModel = false</c>.
        /// </remarks>
        public static List<SolarCoverageSimulationResult> Simulate_Coverage(this AnalyticalModel analyticalModel, IEnumerable<DateTime> dateTimes, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, double sampleSize = double.NaN)
        {
            return Simulate_Coverage(analyticalModel, dateTimes, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize, false);
        }

        /// <remarks>
        /// Binary-compatibility overload — see the IEnumerable&lt;DateTime&gt; overload above. Delegates
        /// with <c>useModelSolarModel = false</c>.
        /// </remarks>
        public static List<SolarCoverageSimulationResult> Simulate_Coverage(this AnalyticalModel analyticalModel, Dictionary<DateTime, Vector3D> directionDictionary, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, double sampleSize = double.NaN)
        {
            return Simulate_Coverage(analyticalModel, directionDictionary, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize, false);
        }

        /// <summary>
        /// Coverage-only simulate with control over the surface set. When <paramref name="useModelSolarModel"/>
        /// is true, SAM recomputes coverage on the SolarModel already attached to the AnalyticalModel
        /// (e.g. the TAS-imported surfaces); otherwise it derives panels from the AdjacencyCluster as usual.
        /// </summary>
        public static List<SolarCoverageSimulationResult> Simulate_Coverage(this AnalyticalModel analyticalModel, IEnumerable<DateTime> dateTimes, double minHorizonAngle, double tolerance_Area, double tolerance_Snap, double tolerance_Angle, double tolerance_Distance, double sampleSize, bool useModelSolarModel)
        {
            if (analyticalModel == null || dateTimes == null)
            {
                return null;
            }

            Core.Location location = analyticalModel.Location;
            if (location == null)
            {
                return null;
            }

            Dictionary<DateTime, Vector3D> directionDictionary = new Dictionary<DateTime, Vector3D>();
            foreach (DateTime dateTime in dateTimes)
            {
                directionDictionary[dateTime] = Geometry.SolarCalculator.Query.SunDirection(location, dateTime, false);
            }

            return Simulate_Coverage(analyticalModel, directionDictionary, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize, useModelSolarModel);
        }

        /// <summary>
        /// Coverage-only simulate with control over the surface set — see the IEnumerable&lt;DateTime&gt;
        /// overload. When <paramref name="useModelSolarModel"/> is true, reuses the attached SolarModel's
        /// geometry instead of the AdjacencyCluster-derived panel set.
        /// </summary>
        public static List<SolarCoverageSimulationResult> Simulate_Coverage(this AnalyticalModel analyticalModel, Dictionary<DateTime, Vector3D> directionDictionary, double minHorizonAngle, double tolerance_Area, double tolerance_Snap, double tolerance_Angle, double tolerance_Distance, double sampleSize, bool useModelSolarModel)
        {
            if (analyticalModel == null || directionDictionary == null)
            {
                return null;
            }

            // useModelSolarModel: recompute coverage on the SolarModel ALREADY attached to the model
            // (e.g. the TAS-imported surfaces) instead of re-deriving panels from the AdjacencyCluster.
            // Guarantees SAM evaluates the exact same faces as the imported model — a 1:1 benchmark set.
            // Coverage path includes window apertures (the regular face-simulation path does not — see
            // ToSAM_SolarModel(AnalyticalModel, bool)). When reusing an already-attached SolarModel, its
            // surfaces (e.g. a TAS import) are taken as-is.
            SolarModel solarModel = useModelSolarModel ? GeometryOnlySolarModel(analyticalModel) : Convert.ToSAM_SolarModel(analyticalModel, true);
            if (solarModel == null)
            {
                return null;
            }

            List<SolarCoverageSimulationResult> solarCoverageSimulationResults = Weather.SolarCalculator.Modify.Simulate_Coverage(solarModel, directionDictionary, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize);

            // Attach the freshly-simulated SolarModel so downstream nodes (e.g. a comparison node) read
            // THIS run's result. Done unconditionally — even when the run produced no coverage (e.g. every
            // requested hour below minHorizonAngle): the new model then carries the simulated geometry with
            // no coverage results, which honestly reflects "no coverage" and, crucially, never leaves a
            // prior/stale SolarModel (e.g. a TAS import) attached for downstream to misread as this run's
            // output. The original import is preserved by the caller, which clones the model before
            // simulating (see SAMAnalytical.SolarSimulation).
            analyticalModel.SetValue(AnalyticalModelParameter.SolarModel, solarModel);

            if (solarCoverageSimulationResults == null || solarCoverageSimulationResults.Count == 0)
            {
                return solarCoverageSimulationResults;
            }

            // Classify each coverage result and relate it to its source object so downstream code
            // (e.g. SAM.Analytical.Tas.Modify.UpdateShading) reads SAM-recomputed coverage exactly the
            // way it reads TAS-imported coverage. Panels relate by their own Guid; apertures get a
            // "… -frame" result (the whole window-opening surface — its LinkedFace3D.Guid == aperture.Guid)
            // and a "… -pane" result (the inset glazing surface — a fresh Guid back-referenced to the
            // aperture in ToSAM_SolarModel). Mirrors Modify.CopyResults' naming/relation on the TAS side.
            List<Panel> panels = analyticalModel.GetPanels();

            List<Aperture> apertures = analyticalModel.GetApertures();
            Dictionary<string, Aperture> apertureByGuid = new Dictionary<string, Aperture>();
            if (apertures != null)
            {
                foreach (Aperture aperture in apertures)
                {
                    if (aperture != null)
                    {
                        apertureByGuid[aperture.Guid.ToString()] = aperture;
                    }
                }
            }

            // Map each pane surface's (fresh) LinkedFace3D.Guid to its owning aperture, via the
            // aperture-Guid reference stamped on the pane LinkedFace3D in ToSAM_SolarModel.
            Dictionary<string, Aperture> apertureByPaneSurfaceGuid = new Dictionary<string, Aperture>();
            List<Geometry.Object.Spatial.LinkedFace3D> linkedFace3Ds = solarModel.GetLinkedFace3Ds();
            if (linkedFace3Ds != null)
            {
                foreach (Geometry.Object.Spatial.LinkedFace3D linkedFace3D in linkedFace3Ds)
                {
                    if (linkedFace3D?.Reference == null)
                    {
                        continue;
                    }

                    if (apertureByGuid.TryGetValue(linkedFace3D.Reference, out Aperture aperture) && aperture != null)
                    {
                        apertureByPaneSurfaceGuid[linkedFace3D.Guid.ToString()] = aperture;
                    }
                }
            }

            foreach (SolarCoverageSimulationResult solarCoverageSimulationResult in solarCoverageSimulationResults)
            {
                string reference = solarCoverageSimulationResult.Reference;

                // Wall/roof panel.
                Panel panel = reference == null ? null : panels?.Find(x => x.Guid.ToString().Equals(reference));
                if (panel != null)
                {
                    analyticalModel.AddResult<Panel>(solarCoverageSimulationResult, panel.Guid);
                    continue;
                }

                // Aperture opening surface -> frame.
                if (reference != null && apertureByGuid.TryGetValue(reference, out Aperture apertureFrame))
                {
                    SolarCoverageSimulationResult frameResult = new SolarCoverageSimulationResult(string.Format("{0} {1}", apertureFrame.Name, FrameSufix), solarCoverageSimulationResult.Source, apertureFrame.Guid.ToString(), solarCoverageSimulationResult);
                    analyticalModel.AddResult<Aperture>(frameResult, apertureFrame);
                    continue;
                }

                // Aperture glazing pane surface -> pane.
                if (reference != null && apertureByPaneSurfaceGuid.TryGetValue(reference, out Aperture aperturePane))
                {
                    SolarCoverageSimulationResult paneResult = new SolarCoverageSimulationResult(string.Format("{0} {1}", aperturePane.Name, PaneSufix), solarCoverageSimulationResult.Source, aperturePane.Guid.ToString(), solarCoverageSimulationResult);
                    analyticalModel.AddResult<Aperture>(paneResult, aperturePane);
                    continue;
                }

                // Unmatched (e.g. useModelSolarModel = true reuses TAS surfaces whose Guids match
                // neither a panel nor an aperture): keep the result but without a relation, as before.
                analyticalModel.AddResult<Panel>(solarCoverageSimulationResult, Guid.Empty);
            }

            return solarCoverageSimulationResults;
        }

        /// <summary>
        /// Builds a fresh SolarModel that carries ONLY the geometry (LinkedFace3Ds) of the SolarModel
        /// already attached to the AnalyticalModel under <see cref="AnalyticalModelParameter.SolarModel"/>
        /// — e.g. the TAS-imported surfaces. Any existing results are intentionally dropped so that a
        /// subsequent coverage simulation recomputes SAM coverage on the EXACT same faces, giving a
        /// 1:1 benchmark surface set instead of the AdjacencyCluster-filtered panel set. Returns null
        /// when no SolarModel is attached or it has no usable geometry.
        /// </summary>
        private static SolarModel GeometryOnlySolarModel(AnalyticalModel analyticalModel)
        {
            SolarModel existing = analyticalModel?.GetValue<SolarModel>(AnalyticalModelParameter.SolarModel);
            if (existing == null)
            {
                return null;
            }

            List<Geometry.Object.Spatial.LinkedFace3D> linkedFace3Ds = existing.GetLinkedFace3Ds();
            if (linkedFace3Ds == null || linkedFace3Ds.Count == 0)
            {
                return null;
            }

            SolarModel result = new SolarModel(analyticalModel.Location);
            foreach (Geometry.Object.Spatial.LinkedFace3D linkedFace3D in linkedFace3Ds)
            {
                if (linkedFace3D?.Face3D == null)
                {
                    continue;
                }

                result.Add(linkedFace3D);
            }

            return result;
        }

        public static List<SolarFaceSimulationResult> Simulate(this BuildingModel buildingModel, IEnumerable<DateTime> dateTimes, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, double sampleSize = double.NaN)
        {
            if (buildingModel == null || dateTimes == null)
            {
                return null;
            }

            SolarModel solarModel = Convert.ToSAM_SolarModel(buildingModel);
            if (solarModel == null)
            {
                return null;
            }

            List<SolarFaceSimulationResult> result = Weather.SolarCalculator.Modify.Simulate(solarModel, dateTimes, true ,minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize);
            if (result != null && result.Count != 0)
            {
                List<IPartition> partitions = buildingModel.GetPartitions();
                foreach (SolarFaceSimulationResult solarFaceSimulationResult in result)
                {
                    Guid guid = Guid.Empty;

                    IPartition partition = partitions.Find(x => x.Guid.ToString().Equals(solarFaceSimulationResult.Reference));
                    if (partition != null)
                    {
                        guid = partition.Guid;
                    }

                    buildingModel.Add<IPartition>(solarFaceSimulationResult, guid);
                }
            }

            return result;
        }
    }
}
