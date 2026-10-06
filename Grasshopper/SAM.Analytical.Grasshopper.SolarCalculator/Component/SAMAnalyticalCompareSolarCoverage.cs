// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Geometry.Grasshopper;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMAnalyticalCompareSolarCoverage : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("0e3a2cf4-1d23-4f5e-9b7a-1c5e8d3f7a91");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.3.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public SAMAnalyticalCompareSolarCoverage()
          : base("SAMAnalytical.CompareSolarCoverage", "SAMAnalytical.CompareSolarCoverage",
              "Compare SolarCoverageSimulationResults between two AnalyticalModels — typically one TAS-imported and one SAM-computed — to benchmark SAM's solar engine against TAS shade-proportion data.\nFaces are matched by Face3D.InternalPoint3D proximity within _tolerance_; unmatched faces are reported separately.\nTimestamps from both results are ceiling-rounded to the next whole hour and matched on (month, day, hour) ignoring year, so half-hour offsets and different base years align.\nPer matched pair, the per-hour delta (B − A) is reduced to mean absolute, max absolute, and RMSE.\n\nEXAMPLE\nSAMAnalytical.SolarSimulation (_coverageOnly_ = true) → CompareSolarCoverage._analyticalModel_B; the TAS-imported model (e.g. SAMAnalytical.FromTBD with _importSurfaceShades_ = true) → CompareSolarCoverage._analyticalModel_A.\nRead overallMeanAbsDelta for the single benchmark number and logs for the per-face detail.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel_A", NickName = "_analyticalModel_A", Description = "First AnalyticalModel (typically TAS-imported, with SolarModel attached via AnalyticalModelParameter.SolarModel)", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel_B", NickName = "_analyticalModel_B", Description = "Second AnalyticalModel (typically SAM-computed via SAMAnalytical.SolarSimulation with _coverageOnly_ = true)", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_tolerance_", NickName = "_tolerance_", Description = "InternalPoint3D-match distance tolerance in metres. A face from model A is paired with the nearest face in model B whose InternalPoint3D is within this distance. Default 0.5 m.", Access = GH_ParamAccess.item };
                tolerance.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(tolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean alignModels = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_alignModels_", NickName = "_alignModels_", Description = "If true, translate Model B onto Model A (by the min corner of their surface bounding boxes) before matching, so a model intentionally moved in Rhino still compares. Default false — leaving it off keeps position matching as a sanity check that flags misaligned inputs. Assumes a pure translation; the residual is absorbed by _tolerance_.", Access = GH_ParamAccess.item };
                alignModels.SetPersistentData(false);
                result.Add(new GH_SAMParam(alignModels, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean run = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "Run", Access = GH_ParamAccess.item };
                run.SetPersistentData(false);
                result.Add(new GH_SAMParam(run, ParamVisibility.Binding));

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "linkedFace3Ds_A", NickName = "linkedFace3Ds_A", Description = "Face3Ds (from LinkedFace3Ds in model A) that were successfully matched — previewable in the Rhino viewport", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "linkedFace3Ds_B", NickName = "linkedFace3Ds_B", Description = "Face3Ds (from LinkedFace3Ds in model B) paired 1:1 with linkedFace3Ds_A — previewable in the Rhino viewport", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "meanAbsDelta", NickName = "meanAbsDelta", Description = "Mean absolute coverage difference per matched pair, averaged over overlapping DateTimes (coverage is a 0–1 fraction; 0 = identical shading, →1 = complete mismatch)", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "maxAbsDelta", NickName = "maxAbsDelta", Description = "Max absolute coverage difference per matched pair (coverage is a 0–1 fraction; 0 = identical shading, →1 = complete mismatch)", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "rmse", NickName = "rmse", Description = "Root-mean-square error of (B − A) per matched pair (coverage is a 0–1 fraction; 0 = identical shading, →1 = complete mismatch)", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "overlapCount", NickName = "overlapCount", Description = "Number of hour-of-year buckets present in BOTH coverage results after ceiling-to-hour alignment, per matched pair", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "unmatched_A", NickName = "unmatched_A", Description = "Face3Ds (from LinkedFace3Ds in model A) that had no neighbour within _tolerance_ in model B — previewable in the Rhino viewport", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "unmatched_B", NickName = "unmatched_B", Description = "Face3Ds (from LinkedFace3Ds in model B) that were never claimed by any model-A face (no neighbour within _tolerance_ with overlapping hours) — previewable in the Rhino viewport", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "overallMeanAbsDelta", NickName = "overallMeanAbsDelta", Description = "Mean absolute delta across ALL matched pairs and ALL overlapping DateTimes — a single benchmark scalar (coverage is a 0–1 fraction; 0 = identical shading, →1 = complete mismatch)", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "logs", NickName = "logs", Description = "Human-readable diagnostic report: face counts, matched/unmatched counts, tolerance, delta-quality histogram and overall benchmark stats. Read in a Panel.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "Successful?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index;
            int index_Successful = Params.IndexOfOutputParam("successful");
            if (index_Successful != -1)
                dataAccess.SetData(index_Successful, false);

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (!dataAccess.GetData(index, ref run) || !run)
            {
                return;
            }

            AnalyticalModel analyticalModel_A = null;
            index = Params.IndexOfInputParam("_analyticalModel_A");
            if (index == -1 || !dataAccess.GetData(index, ref analyticalModel_A) || analyticalModel_A == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid analyticalModel_A");
                return;
            }

            AnalyticalModel analyticalModel_B = null;
            index = Params.IndexOfInputParam("_analyticalModel_B");
            if (index == -1 || !dataAccess.GetData(index, ref analyticalModel_B) || analyticalModel_B == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid analyticalModel_B");
                return;
            }

            double tolerance = 0.5;
            index = Params.IndexOfInputParam("_tolerance_");
            if (index != -1)
            {
                double tolerance_Temp = tolerance;
                if (dataAccess.GetData(index, ref tolerance_Temp) && !double.IsNaN(tolerance_Temp) && tolerance_Temp > 0)
                {
                    tolerance = tolerance_Temp;
                }
            }

            bool alignModels = false;
            index = Params.IndexOfInputParam("_alignModels_");
            if (index != -1)
            {
                bool alignModels_Temp = false;
                if (dataAccess.GetData(index, ref alignModels_Temp))
                {
                    alignModels = alignModels_Temp;
                }
            }

            SolarModel solarModel_A = analyticalModel_A.GetValue<SolarModel>(AnalyticalModelParameter.SolarModel);
            SolarModel solarModel_B = analyticalModel_B.GetValue<SolarModel>(AnalyticalModelParameter.SolarModel);

            if (solarModel_A == null || solarModel_B == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Both AnalyticalModels must carry a SolarModel under AnalyticalModelParameter.SolarModel (run SAMAnalytical.FromTBD with _importSurfaceShades_ = true on one side, and SAMAnalytical.SolarSimulation with _coverageOnly_ = true on the other).");
                return;
            }

            List<Pair> pairs_A = ExtractPairs(solarModel_A);
            List<Pair> pairs_B = ExtractPairs(solarModel_B);

            // SAM imports a TBD's shading as separate PanelType.Shade panels, which enter Model B's
            // SolarModel as occluder surfaces; TAS has no shade surfaces (it bakes the shade effect into
            // each exposed surface's proportion). These are NOT benchmark targets — exclude them from the
            // candidate sets in BOTH matchers (geometry + results) so an occluder that happens to fall
            // within tolerance of a TAS surface can never be matched in place of the real comparable face.
            HashSet<Guid> shadeGuids_B = ShadePanelGuids(analyticalModel_B);
            int shadeSurfaces_B = CountShadeSurfaces(solarModel_B, shadeGuids_B);

            // Bounding boxes of the COMPARABLE (non-Shade) surfaces only. Shade occluders extend Model B's
            // box and would skew both the alignment offset (shifting common faces to the wrong place) and
            // the rotation check, so they are excluded here. Box size is translation-invariant, so it also
            // serves as a rotation/scale signal (see the report).
            double[] bbox_A = BoundingBox(NonShadeInternalPoints(analyticalModel_A, solarModel_A));
            double[] bbox_B = BoundingBox(NonShadeInternalPoints(analyticalModel_B, solarModel_B));

            // Optional alignment: when the two models were moved apart in Rhino (e.g. one imported at the
            // TBD origin, the other transformed for side-by-side viewing), translate Model B onto Model A
            // by the min corner of their NON-Shade bounding boxes so they still compare. Only the MATCHING
            // points are translated — output geometry stays where the user placed it. Off by default, so
            // position matching remains a sanity check for genuinely misaligned inputs.
            double alignDx = 0, alignDy = 0, alignDz = 0;
            if (alignModels && bbox_A != null && bbox_B != null)
            {
                alignDx = bbox_A[0] - bbox_B[0];
                alignDy = bbox_A[1] - bbox_B[1];
                alignDz = bbox_A[2] - bbox_B[2];
                if (alignDx != 0 || alignDy != 0 || alignDz != 0)
                {
                    pairs_B = pairs_B.ConvertAll(p => new Pair(p.LinkedFace3D, new Point3D(p.InternalPoint3D.X + alignDx, p.InternalPoint3D.Y + alignDy, p.InternalPoint3D.Z + alignDz), p.Result));
                }
            }

            // Geometry-level diagnostic FIRST — compare ALL panels (LinkedFace3Ds) regardless of
            // whether they carry a result. This separates "do the two models share the same panel
            // geometry 1:1?" from "do the panels that have results agree?". A models that bake
            // identically should align here at ~0 distance; any gap points at the geometry/import,
            // not the result values.
            List<string> logs = BuildGeometryReport(solarModel_A, solarModel_B, shadeGuids_B, pairs_A.Count, pairs_B.Count, tolerance, shadeSurfaces_B, bbox_A, bbox_B, alignDx, alignDy, alignDz, out GeometryAlignmentResult geometry);

            // Only when some Model A surfaces have no 1:1 match in Model B is the panel-level breakdown
            // meaningful — it explains WHY SAM dropped them (internal / non-sun-exposed / no panel). On a
            // clean 1:1 match it would just print noise (windows are apertures, not panels), so skip it.
            if (geometry.UnmatchedA > 0)
            {
                logs.AddRange(BuildModelBSelectionReport(analyticalModel_B, solarModel_A, tolerance));
            }

            if (pairs_A.Count == 0 || pairs_B.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "One or both SolarModels contain no SolarCoverageSimulationResults — only the geometry-alignment section of 'logs' is populated.");
                logs.Add("--- No SolarCoverageSimulationResults on one or both models — results comparison skipped. ---");
                index = Params.IndexOfOutputParam("logs");
                if (index != -1) dataAccess.SetDataList(index, logs);
                return;
            }

            // Match each pair from A to nearest (by InternalPoint3D distance) in B, within tolerance.
            // Each B face can only be claimed once — greedy first-come matching, which is fine
            // when models share geometry.
            HashSet<int> usedB = new HashSet<int>();
            List<LinkedFace3D> matched_A = new List<LinkedFace3D>();
            List<LinkedFace3D> matched_B = new List<LinkedFace3D>();
            List<double> meanAbsDeltas = new List<double>();
            List<double> maxAbsDeltas = new List<double>();
            List<double> rmses = new List<double>();
            List<int> overlapCounts = new List<int>();
            List<LinkedFace3D> unmatched_A = new List<LinkedFace3D>();

            double sumAbsDelta_All = 0;
            double sumSignedDelta_All = 0;
            double sumValues_All = 0;
            int sumOverlap_All = 0;

            foreach (Pair pair_A in pairs_A)
            {
                // Collect every still-unused B candidate inside the tolerance radius
                // and walk them in nearest-first order. If the closest candidate has no
                // overlapping DateTimes we fall through to the next nearest rather than
                // immediately classifying pair_A as unmatched — handles duplicated /
                // near-coincident geometry and mixed timestep sets correctly.
                List<KeyValuePair<int, double>> candidates = new List<KeyValuePair<int, double>>();
                for (int j = 0; j < pairs_B.Count; j++)
                {
                    if (usedB.Contains(j))
                    {
                        continue;
                    }

                    // Shade occluders are not benchmark targets — never let one be matched to a TAS
                    // surface (it would corrupt the delta and leave the real comparable face unused).
                    if (shadeGuids_B.Contains(pairs_B[j].LinkedFace3D.Guid))
                    {
                        continue;
                    }

                    double distance = pair_A.InternalPoint3D.Distance(pairs_B[j].InternalPoint3D);
                    if (distance <= tolerance)
                    {
                        candidates.Add(new KeyValuePair<int, double>(j, distance));
                    }
                }
                candidates.Sort((a, b) => a.Value.CompareTo(b.Value));

                bool matched = false;
                foreach (KeyValuePair<int, double> candidate in candidates)
                {
                    int candidateIndex = candidate.Key;
                    Pair pair_B = pairs_B[candidateIndex];

                    ComputeDeltaStats(pair_A.Result, pair_B.Result, out double meanAbs, out double maxAbs, out double rmse, out int overlap, out double sumAbs_Pair, out double sumSigned_Pair, out double sumValues_Pair);
                    if (overlap == 0)
                    {
                        // Nearest candidate has no shared DateTimes — try the next one.
                        // The candidate stays in usedB? NO — leave it free so a later A
                        // face with overlapping DateTimes can still claim it.
                        continue;
                    }

                    usedB.Add(candidateIndex);
                    matched_A.Add(pair_A.LinkedFace3D);
                    matched_B.Add(pair_B.LinkedFace3D);
                    meanAbsDeltas.Add(meanAbs);
                    maxAbsDeltas.Add(maxAbs);
                    rmses.Add(rmse);
                    overlapCounts.Add(overlap);
                    sumAbsDelta_All += sumAbs_Pair;
                    sumSignedDelta_All += sumSigned_Pair;
                    sumValues_All += sumValues_Pair;
                    sumOverlap_All += overlap;
                    matched = true;
                    break;
                }

                if (!matched)
                {
                    unmatched_A.Add(pair_A.LinkedFace3D);
                }
            }

            // COMPARABLE B faces never claimed by any A face (no neighbour within tolerance with overlapping
            // hours). Shade occluders are excluded — they are reported separately, not as benchmark misses,
            // so this count flags only genuinely-unmatched comparable surfaces.
            List<LinkedFace3D> unmatched_B = new List<LinkedFace3D>();
            for (int j = 0; j < pairs_B.Count; j++)
            {
                if (usedB.Contains(j)) continue;
                if (shadeGuids_B.Contains(pairs_B[j].LinkedFace3D.Guid)) continue;
                unmatched_B.Add(pairs_B[j].LinkedFace3D);
            }

            double overallMeanAbsDelta = sumOverlap_All == 0 ? double.NaN : sumAbsDelta_All / sumOverlap_All;

            index = Params.IndexOfOutputParam("linkedFace3Ds_A");
            if (index != -1) dataAccess.SetDataList(index, matched_A.ConvertAll(x => x?.Face3D));

            index = Params.IndexOfOutputParam("linkedFace3Ds_B");
            if (index != -1) dataAccess.SetDataList(index, matched_B.ConvertAll(x => x?.Face3D));

            index = Params.IndexOfOutputParam("meanAbsDelta");
            if (index != -1) dataAccess.SetDataList(index, meanAbsDeltas);

            index = Params.IndexOfOutputParam("maxAbsDelta");
            if (index != -1) dataAccess.SetDataList(index, maxAbsDeltas);

            index = Params.IndexOfOutputParam("rmse");
            if (index != -1) dataAccess.SetDataList(index, rmses);

            index = Params.IndexOfOutputParam("overlapCount");
            if (index != -1) dataAccess.SetDataList(index, overlapCounts);

            index = Params.IndexOfOutputParam("unmatched_A");
            if (index != -1) dataAccess.SetDataList(index, unmatched_A.ConvertAll(x => x?.Face3D));

            index = Params.IndexOfOutputParam("unmatched_B");
            if (index != -1) dataAccess.SetDataList(index, unmatched_B.ConvertAll(x => x?.Face3D));

            index = Params.IndexOfOutputParam("overallMeanAbsDelta");
            if (index != -1) dataAccess.SetData(index, overallMeanAbsDelta);

            double overallSignedDelta = sumOverlap_All == 0 ? double.NaN : sumSignedDelta_All / sumOverlap_All;
            double overallMeanSum = sumOverlap_All == 0 ? double.NaN : sumValues_All / sumOverlap_All;
            logs.AddRange(BuildResultsReport(matched_A.Count, unmatched_A.Count, unmatched_B.Count, meanAbsDeltas, rmses, overlapCounts, overallMeanAbsDelta, overallSignedDelta, overallMeanSum, sumOverlap_All));
            index = Params.IndexOfOutputParam("logs");
            if (index != -1) dataAccess.SetDataList(index, logs);

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, string.Format(System.Globalization.CultureInfo.InvariantCulture, "Geometry: {0}/{1} panels aligned (A unaligned {2}, B unaligned {3}). Results: matched {4}/{5} A-faces; overallMeanAbsDelta={6:0.0000}", geometry.Matched, Math.Max(geometry.TotalA, geometry.TotalB), geometry.UnmatchedA, geometry.UnmatchedB, matched_A.Count, pairs_A.Count, overallMeanAbsDelta));

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, matched_A.Count > 0);
            }
        }

        /// <summary>
        /// Build the geometry-alignment section of the report. Compares EVERY LinkedFace3D (panel)
        /// in the two SolarModels by InternalPoint3D proximity — independent of whether the panel
        /// carries a result — so a model that bakes identically is confirmed 1:1 before any result
        /// values are looked at. <see cref="GetInternalPoints"/> is deterministic, so identical
        /// geometry aligns at ~0 distance.
        /// </summary>
        private static List<string> BuildGeometryReport(SolarModel solarModel_A, SolarModel solarModel_B, HashSet<Guid> shadeGuids_B, int resultsCount_A, int resultsCount_B, double tolerance, int shadeSurfaces_B, double[] bbox_A, double[] bbox_B, double alignDx, double alignDy, double alignDz, out GeometryAlignmentResult alignment)
        {
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
            List<string> logs = new List<string>();

            List<Point3D> points_A = GetInternalPoints(solarModel_A, out int total_A);

            // Model B matching points exclude Shade occluders: they are not benchmark targets, and an
            // occluder within tolerance must never be matched in place of the real comparable face. total_B
            // still counts all surfaces (incl. Shade) for the headline; the Shade count is reported separately.
            int total_B = 0;
            List<Point3D> points_B = new List<Point3D>();
            List<LinkedFace3D> faces_B = solarModel_B?.GetLinkedFace3Ds();
            if (faces_B != null)
            {
                total_B = faces_B.Count;
                foreach (LinkedFace3D linkedFace3D in faces_B)
                {
                    if (linkedFace3D?.Face3D == null) continue;
                    if (shadeGuids_B != null && shadeGuids_B.Contains(linkedFace3D.Guid)) continue;

                    Point3D internalPoint3D = linkedFace3D.Face3D.InternalPoint3D();
                    if (internalPoint3D != null) points_B.Add(internalPoint3D);
                }
            }

            // Apply the optional Model B -> Model A alignment translation to the matching points only.
            bool aligned = alignDx != 0 || alignDy != 0 || alignDz != 0;
            if (aligned)
            {
                for (int i = 0; i < points_B.Count; i++)
                {
                    points_B[i] = new Point3D(points_B[i].X + alignDx, points_B[i].Y + alignDy, points_B[i].Z + alignDz);
                }
            }

            // Greedy nearest-neighbour 1:1 match by internal point — same scheme as the result
            // matching, but over all panels and reduced to distance only.
            HashSet<int> usedB = new HashSet<int>();
            List<double> distances = new List<double>();
            int matched = 0;
            foreach (Point3D point_A in points_A)
            {
                int bestIndex = -1;
                double bestDistance = double.MaxValue;
                for (int j = 0; j < points_B.Count; j++)
                {
                    if (usedB.Contains(j)) continue;

                    double distance = point_A.Distance(points_B[j]);
                    if (distance <= tolerance && distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = j;
                    }
                }

                if (bestIndex != -1)
                {
                    usedB.Add(bestIndex);
                    distances.Add(bestDistance);
                    matched++;
                }
            }

            alignment = new GeometryAlignmentResult
            {
                TotalA = total_A,
                TotalB = total_B,
                Matched = matched,
                UnmatchedA = points_A.Count - matched,
                UnmatchedB = points_B.Count - matched
            };

            logs.Add("=== SAMAnalytical.CompareSolarCoverage ===");
            logs.Add(string.Format(culture, "Tolerance: {0:0.###} m", tolerance));
            logs.Add("--- Geometry alignment (ALL LinkedFace3Ds — independent of results) ---");
            if (aligned)
            {
                logs.Add(string.Format(culture, "_alignModels_ = true: Model B translated by ({0:0.###}, {1:0.###}, {2:0.###}) m onto Model A before matching (output geometry is unchanged).", alignDx, alignDy, alignDz));
            }
            logs.Add(string.Format(culture, "Model A: {0} panels total  |  {1} with coverage results", total_A, resultsCount_A));
            logs.Add(string.Format(culture, "Model B: {0} panels total  |  {1} with coverage results", total_B, resultsCount_B));
            logs.Add(string.Format(culture, "Comparable panels with a valid internal point (Shade excluded): A {0}, B {1}", points_A.Count, points_B.Count));
            logs.Add(string.Format(culture, "Panels matched 1:1 within {0:0.###} m: {1}", tolerance, matched));
            logs.Add(string.Format(culture, "Geometry-unmatched A (no comparable panel within tolerance in B): {0}", alignment.UnmatchedA));
            logs.Add(string.Format(culture, "Geometry-unmatched B comparable (excludes Shade occluders): {0}", alignment.UnmatchedB));
            if (distances.Count > 0)
            {
                double min = double.MaxValue, max = 0, sum = 0;
                foreach (double distance in distances)
                {
                    if (distance < min) min = distance;
                    if (distance > max) max = distance;
                    sum += distance;
                }
                logs.Add(string.Format(culture, "Matched-panel internal-point distance min/mean/max: {0:0.0000} / {1:0.0000} / {2:0.0000} m", min, sum / distances.Count, max));
            }

            // Bounding box (comparable, non-Shade surfaces) of each model — bounds reveal an offset,
            // and box SIZE (translation-invariant) reveals a rotation/scale a translation can't fix.
            if (bbox_A != null && bbox_B != null)
            {
                logs.Add(string.Format(culture, "Model A bounds (non-Shade): X[{0:0.0}..{1:0.0}] Y[{2:0.0}..{3:0.0}] Z[{4:0.0}..{5:0.0}]  size ({6:0.0}, {7:0.0}, {8:0.0})", bbox_A[0], bbox_A[3], bbox_A[1], bbox_A[4], bbox_A[2], bbox_A[5], bbox_A[3] - bbox_A[0], bbox_A[4] - bbox_A[1], bbox_A[5] - bbox_A[2]));
                logs.Add(string.Format(culture, "Model B bounds (non-Shade): X[{0:0.0}..{1:0.0}] Y[{2:0.0}..{3:0.0}] Z[{4:0.0}..{5:0.0}]  size ({6:0.0}, {7:0.0}, {8:0.0})", bbox_B[0], bbox_B[3], bbox_B[1], bbox_B[4], bbox_B[2], bbox_B[5], bbox_B[3] - bbox_B[0], bbox_B[4] - bbox_B[1], bbox_B[5] - bbox_B[2]));

                double dSizeX = Math.Abs((bbox_A[3] - bbox_A[0]) - (bbox_B[3] - bbox_B[0]));
                double dSizeY = Math.Abs((bbox_A[4] - bbox_A[1]) - (bbox_B[4] - bbox_B[1]));
                double dSizeZ = Math.Abs((bbox_A[5] - bbox_A[2]) - (bbox_B[5] - bbox_B[2]));
                double sizeTolerance = Math.Max(tolerance, 0.5);
                if (dSizeX > sizeTolerance || dSizeY > sizeTolerance || dSizeZ > sizeTolerance)
                {
                    logs.Add(string.Format(culture, "WARNING: bounding-box sizes differ by ({0:0.0}, {1:0.0}, {2:0.0}) m — the models are likely ROTATED or SCALED relative to each other. A translation (incl. _alignModels_) cannot fix this; re-orient them to the same coordinate frame.", dSizeX, dSizeY, dSizeZ));
                }
            }

            if (shadeSurfaces_B > 0)
            {
                logs.Add(string.Format(culture, "Model B includes {0} PanelType.Shade occluder surface(s) — SAM imports the TBD's shading as separate Shade panels, whereas TAS bakes the shade effect into each exposed surface's proportion (no shade surfaces). These have no Model A counterpart, so they are expected to be unmatched-B and are excluded from the matched-pair benchmark.", shadeSurfaces_B));
            }

            // When Model B carries extra Shade occluders, the count difference is expected — don't
            // raise the "models don't share geometry" alarm if the surplus is fully explained by them.
            int unexplainedExtraB = (total_B - total_A) - shadeSurfaces_B;
            if (total_A != total_B && unexplainedExtraB > 0)
            {
                logs.Add("NOTE: panel counts differ beyond the Shade occluders — the two models do NOT share the same geometry. Fix the geometry/import before trusting result deltas.");
            }
            else if (alignment.UnmatchedA == 0 && alignment.UnmatchedB == 0)
            {
                logs.Add("NOTE: every comparable Model A surface matches Model B 1:1 (Shade occluders excluded) — differences are a RESULTS gap, not a geometry mismatch.");
            }

            return logs;
        }

        /// <summary>
        /// Internal points for every LinkedFace3D in a SolarModel (regardless of results).
        /// <paramref name="total"/> returns the raw panel count before null/degenerate faces are
        /// dropped, so the report can distinguish "no panels" from "panels with no internal point".
        /// </summary>
        private static List<Point3D> GetInternalPoints(SolarModel solarModel, out int total)
        {
            total = 0;
            List<Point3D> result = new List<Point3D>();
            if (solarModel == null) return result;

            List<LinkedFace3D> linkedFace3Ds = solarModel.GetLinkedFace3Ds();
            if (linkedFace3Ds == null) return result;

            total = linkedFace3Ds.Count;
            foreach (LinkedFace3D linkedFace3D in linkedFace3Ds)
            {
                if (linkedFace3D?.Face3D == null) continue;

                Point3D internalPoint3D = linkedFace3D.Face3D.InternalPoint3D();
                if (internalPoint3D == null) continue;

                result.Add(internalPoint3D);
            }

            return result;
        }

        /// <summary>
        /// Translation (added to Model B's points) that puts the min corner of Model B's internal-point
        /// bounding box onto Model A's, so a model moved in Rhino still matches. Assumes a pure rigid
        /// translation; any residual (e.g. shade panels extending Model B's box) is absorbed by the
        /// match tolerance. Returns zeros when either model has no internal points.
        /// </summary>
        /// <summary>
        /// Axis-aligned bounding box of a point set as {minX, minY, minZ, maxX, maxY, maxZ}, or null when
        /// empty. Used for the alignment offset (min corner) and the rotation/scale check (size).
        /// </summary>
        private static double[] BoundingBox(List<Point3D> points)
        {
            if (points == null || points.Count == 0)
            {
                return null;
            }

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = -double.MaxValue, maxY = -double.MaxValue, maxZ = -double.MaxValue;
            foreach (Point3D p in points)
            {
                if (p == null) continue;
                if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y; if (p.Z < minZ) minZ = p.Z;
                if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y; if (p.Z > maxZ) maxZ = p.Z;
            }

            return new double[] { minX, minY, minZ, maxX, maxY, maxZ };
        }

        /// <summary>
        /// Guids of the model's PanelType.Shade panels (occluders SAM adds that TAS has no surface for).
        /// </summary>
        private static HashSet<Guid> ShadePanelGuids(AnalyticalModel analyticalModel)
        {
            HashSet<Guid> result = new HashSet<Guid>();
            if (analyticalModel == null) return result;

            List<Analytical.SolarCalculator.PanelSolarClassification> classifications = Analytical.SolarCalculator.Query.ClassifyPanelsForSolarModel(analyticalModel);
            if (classifications == null) return result;

            foreach (Analytical.SolarCalculator.PanelSolarClassification classification in classifications)
            {
                if (classification.PanelType == PanelType.Shade)
                {
                    result.Add(classification.Guid);
                }
            }

            return result;
        }

        /// <summary>
        /// Internal points of the comparable surfaces only — every LinkedFace3D EXCEPT those that come
        /// from a PanelType.Shade panel. Used for the alignment offset and rotation check so shade
        /// occluders (which Model A lacks) don't skew the result.
        /// </summary>
        private static List<Point3D> NonShadeInternalPoints(AnalyticalModel analyticalModel, SolarModel solarModel)
        {
            List<Point3D> result = new List<Point3D>();
            if (solarModel == null) return result;

            HashSet<Guid> shadeGuids = ShadePanelGuids(analyticalModel);
            List<LinkedFace3D> linkedFace3Ds = solarModel.GetLinkedFace3Ds();
            if (linkedFace3Ds == null) return result;

            foreach (LinkedFace3D linkedFace3D in linkedFace3Ds)
            {
                if (linkedFace3D?.Face3D == null) continue;
                if (shadeGuids.Contains(linkedFace3D.Guid)) continue;

                Point3D internalPoint3D = linkedFace3D.Face3D.InternalPoint3D();
                if (internalPoint3D != null) result.Add(internalPoint3D);
            }

            return result;
        }

        private struct GeometryAlignmentResult
        {
            public int TotalA;
            public int TotalB;
            public int Matched;
            public int UnmatchedA;
            public int UnmatchedB;
        }

        /// <summary>
        /// Count the SolarModel's LinkedFace3Ds that come from PanelType.Shade panels — SAM imports a
        /// TBD's shading as separate Shade panels that enter the SolarModel as occluder surfaces. Matched
        /// by Guid (ToSAM_SolarModel keys a panel's LinkedFace3D by panel.Guid). Returns 0 when the model
        /// has no AdjacencyCluster, no Shade panels, or no SolarModel.
        /// </summary>
        private static int CountShadeSurfaces(SolarModel solarModel, HashSet<Guid> shadeGuids)
        {
            if (solarModel == null || shadeGuids == null || shadeGuids.Count == 0)
            {
                return 0;
            }

            List<LinkedFace3D> linkedFace3Ds = solarModel.GetLinkedFace3Ds();
            if (linkedFace3Ds == null)
            {
                return 0;
            }

            int count = 0;
            foreach (LinkedFace3D linkedFace3D in linkedFace3Ds)
            {
                if (linkedFace3D != null && shadeGuids.Contains(linkedFace3D.Guid))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Explain the surface-set gap: classify Model B's analytical panels by the same rule SAM's
        /// SolarModel builder uses (single-space AND sun-exposed PanelType), then map each of Model A's
        /// SolarModel surfaces to the nearest Model B panel within tolerance and tally why it is kept or
        /// dropped. Tells the user whether the surfaces SAM omits are dropped as internal, by PanelType,
        /// or are simply absent from Model B.
        /// </summary>
        private static List<string> BuildModelBSelectionReport(AnalyticalModel analyticalModel_B, SolarModel solarModel_A, double tolerance)
        {
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
            List<string> logs = new List<string>();

            List<Analytical.SolarCalculator.PanelSolarClassification> classifications = analyticalModel_B == null ? null : Analytical.SolarCalculator.Query.ClassifyPanelsForSolarModel(analyticalModel_B);
            if (classifications == null || classifications.Count == 0)
            {
                logs.Add("--- Model B surface selection ---");
                logs.Add("Model B has no AdjacencyCluster panels to classify — selection breakdown unavailable.");
                return logs;
            }

            int totalPanels = classifications.Count;
            int wouldEnter = 0;
            foreach (Analytical.SolarCalculator.PanelSolarClassification classification in classifications)
            {
                if (classification.Kept) wouldEnter++;
            }

            // Map each Model A surface to the nearest Model B panel within tolerance and tally the verdict.
            List<LinkedFace3D> faces_A = solarModel_A?.GetLinkedFace3Ds() ?? new List<LinkedFace3D>();
            int kept = 0, droppedInternal = 0, droppedPanelType = 0, noPanel = 0;
            Dictionary<string, int> droppedPanelTypeHistogram = new Dictionary<string, int>();
            int surfaces_A = 0;

            foreach (LinkedFace3D face_A in faces_A)
            {
                Point3D point_A = face_A?.Face3D?.InternalPoint3D();
                if (point_A == null) continue;
                surfaces_A++;

                Analytical.SolarCalculator.PanelSolarClassification nearest = null;
                double bestDistance = double.MaxValue;
                foreach (Analytical.SolarCalculator.PanelSolarClassification classification in classifications)
                {
                    if (classification.InternalPoint3D == null) continue;

                    double distance = point_A.Distance(classification.InternalPoint3D);
                    if (distance <= tolerance && distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = classification;
                    }
                }

                if (nearest == null)
                {
                    noPanel++;
                }
                else if (nearest.Kept)
                {
                    kept++;
                }
                else if (nearest.DropReason == Analytical.SolarCalculator.PanelSolarClassification.Reason_Internal)
                {
                    droppedInternal++;
                    AddToHistogram(droppedPanelTypeHistogram, nearest.PanelType.ToString());
                }
                else
                {
                    droppedPanelType++;
                    AddToHistogram(droppedPanelTypeHistogram, nearest.PanelType.ToString());
                }
            }

            logs.Add("--- Model B surface selection (why SAM keeps only some of Model A's surfaces) ---");
            logs.Add(string.Format(culture, "Model B AdjacencyCluster: {0} panels total; would enter SolarModel (single-space + sun-exposed): {1}", totalPanels, wouldEnter));
            logs.Add(string.Format(culture, "Mapping each of Model A's {0} surfaces to the nearest Model B panel within {1:0.###} m:", surfaces_A, tolerance));
            logs.Add(string.Format(culture, "  kept (single-space + sun-exposed PanelType): {0}", kept));
            logs.Add(string.Format(culture, "  dropped - internal (>= 2 spaces): {0}", droppedInternal));
            logs.Add(string.Format(culture, "  dropped - non-sun-exposed PanelType: {0}", droppedPanelType));
            logs.Add(string.Format(culture, "  no Model B panel within tolerance: {0}", noPanel));
            if (droppedPanelTypeHistogram.Count > 0)
            {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, int> entry in droppedPanelTypeHistogram)
                {
                    parts.Add(string.Format(culture, "{0}x{1}", entry.Value, entry.Key));
                }
                logs.Add("  dropped PanelTypes: " + string.Join(", ", parts));
            }

            return logs;
        }

        private static void AddToHistogram(Dictionary<string, int> histogram, string key)
        {
            if (histogram.TryGetValue(key, out int count))
            {
                histogram[key] = count + 1;
            }
            else
            {
                histogram[key] = 1;
            }
        }

        /// <summary>
        /// Build the results-comparison section of the report (per-pair delta distribution and
        /// overall benchmark stats) so a benchmark run can be judged without reading the raw
        /// number panels. The geometry section is produced separately by <see cref="BuildGeometryReport"/>.
        /// </summary>
        private static List<string> BuildResultsReport(int matchedCount, int unmatchedCount_A, int unmatchedCount_B, List<double> meanAbsDeltas, List<double> rmses, List<int> overlapCounts, double overallMeanAbsDelta, double overallSignedDelta, double overallMeanSum, int sumOverlap_All)
        {
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
            List<string> logs = new List<string>();

            logs.Add("--- Results matching (faces that carry a coverage result) ---");
            logs.Add(string.Format(culture, "Matched pairs: {0}", matchedCount));
            logs.Add(string.Format(culture, "Unmatched A (no neighbour within tolerance / no time overlap): {0}", unmatchedCount_A));
            logs.Add(string.Format(culture, "Unmatched B (never claimed): {0}", unmatchedCount_B));

            // Per-pair meanAbsDelta values that are actually comparable (skip NaN — those pairs had no overlap).
            List<double> validDeltas = new List<double>();
            foreach (double value in meanAbsDeltas)
            {
                if (!double.IsNaN(value)) validDeltas.Add(value);
            }

            if (validDeltas.Count == 0)
            {
                logs.Add("--- No matched pairs with overlapping hours — no delta statistics available. ---");
                return logs;
            }

            // Delta-quality histogram over per-pair meanAbsDelta.
            int excellent = 0, good = 0, fair = 0, poor = 0;
            foreach (double value in validDeltas)
            {
                if (value < 0.01) excellent++;
                else if (value < 0.05) good++;
                else if (value < 0.10) fair++;
                else poor++;
            }

            int total = validDeltas.Count;
            logs.Add("--- Delta distribution (mean abs coverage delta per pair) ---");
            logs.Add(string.Format(culture, "< 0.01 (excellent): {0} pairs ({1:0.0}%)", excellent, 100.0 * excellent / total));
            logs.Add(string.Format(culture, "0.01–0.05 (good): {0} pairs ({1:0.0}%)", good, 100.0 * good / total));
            logs.Add(string.Format(culture, "0.05–0.10 (fair): {0} pairs ({1:0.0}%)", fair, 100.0 * fair / total));
            logs.Add(string.Format(culture, "> 0.10 (poor): {0} pairs ({1:0.0}%)", poor, 100.0 * poor / total));

            // Mean RMSE over valid pairs.
            double sumRmse = 0;
            int countRmse = 0;
            foreach (double value in rmses)
            {
                if (!double.IsNaN(value)) { sumRmse += value; countRmse++; }
            }
            double meanRmse = countRmse == 0 ? double.NaN : sumRmse / countRmse;

            // Median & max of per-pair meanAbsDelta.
            List<double> sorted = new List<double>(validDeltas);
            sorted.Sort();
            double median = sorted.Count % 2 == 1
                ? sorted[sorted.Count / 2]
                : 0.5 * (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]);
            double maxDelta = sorted[sorted.Count - 1];

            double avgOverlap = matchedCount == 0 ? 0 : (double)sumOverlap_All / matchedCount;

            logs.Add("--- Overall ---");
            logs.Add(string.Format(culture, "overallMeanAbsDelta: {0:0.0000}  (across {1} pairs, {2} overlapping hours)", overallMeanAbsDelta, matchedCount, sumOverlap_All));
            logs.Add(string.Format(culture, "overallSignedDelta (B-A): {0:+0.0000;-0.0000;0.0000}  (>0 = B reads higher than A; |signed| approaching |abs| = one-directional bias)", overallSignedDelta));
            logs.Add(string.Format(culture, "mean(A+B): {0:0.0000}  (~1.0 suggests a lit/shade convention flip between the two; ~2x the mean coverage if same convention)", overallMeanSum));
            logs.Add(string.Format(culture, "mean RMSE: {0:0.0000}", meanRmse));
            logs.Add(string.Format(culture, "median meanAbsDelta: {0:0.0000}", median));
            logs.Add(string.Format(culture, "max meanAbsDelta: {0:0.0000}  (worst matched pair)", maxDelta));
            logs.Add(string.Format(culture, "avg overlap hours/pair: {0:0}", avgOverlap));

            return logs;
        }

        /// <summary>
        /// Build a list of (LinkedFace3D, InternalPoint3D, SolarCoverageSimulationResult) tuples from a SolarModel.
        /// Match is by Reference == linkedFace3D.Guid.ToString() — the convention used by both
        /// the TAS-import path (Create.SolarModel) and the SAM coverage-simulate path.
        /// </summary>
        private static List<Pair> ExtractPairs(SolarModel solarModel)
        {
            List<Pair> result = new List<Pair>();
            if (solarModel == null) return result;

            List<LinkedFace3D> linkedFace3Ds = solarModel.GetLinkedFace3Ds();
            List<SolarCoverageSimulationResult> coverageResults = solarModel.SolarCoverageSimulationResults;
            if (linkedFace3Ds == null || coverageResults == null) return result;

            Dictionary<string, SolarCoverageSimulationResult> dictionary_Result = new Dictionary<string, SolarCoverageSimulationResult>();
            foreach (SolarCoverageSimulationResult coverageResult in coverageResults)
            {
                if (coverageResult?.Reference == null) continue;
                dictionary_Result[coverageResult.Reference] = coverageResult;
            }

            foreach (LinkedFace3D linkedFace3D in linkedFace3Ds)
            {
                if (linkedFace3D?.Face3D == null) continue;
                if (!dictionary_Result.TryGetValue(linkedFace3D.Guid.ToString(), out SolarCoverageSimulationResult coverageResult)) continue;

                Point3D internalPoint3D = linkedFace3D.Face3D.InternalPoint3D();
                if (internalPoint3D == null) continue;

                result.Add(new Pair(linkedFace3D, internalPoint3D, coverageResult));
            }

            return result;
        }

        private static void ComputeDeltaStats(SolarCoverageSimulationResult a, SolarCoverageSimulationResult b, out double meanAbs, out double maxAbs, out double rmse, out int overlap, out double sumAbs, out double sumSigned, out double sumValues)
        {
            meanAbs = double.NaN;
            maxAbs = double.NaN;
            rmse = double.NaN;
            overlap = 0;
            sumAbs = 0;
            sumSigned = 0;
            sumValues = 0;

            if (a == null || b == null) return;

            Dictionary<HourKey, double> map_A = BuildHourMap(a);
            Dictionary<HourKey, double> map_B = BuildHourMap(b);
            if (map_A.Count == 0 || map_B.Count == 0) return;

            double sumSq = 0;
            double max = 0;
            foreach (KeyValuePair<HourKey, double> entry in map_A)
            {
                if (!map_B.TryGetValue(entry.Key, out double valueB)) continue;

                double diff = valueB - entry.Value;
                double absDiff = Math.Abs(diff);
                sumAbs += absDiff;
                sumSigned += diff;                  // signed (B - A): >0 means B reads higher than A
                sumValues += valueB + entry.Value;  // for the convention check (mean(A+B) ~ 1 => lit/shade flip)
                sumSq += diff * diff;
                if (absDiff > max) max = absDiff;
                overlap++;
            }

            if (overlap == 0) return;

            meanAbs = sumAbs / overlap;
            maxAbs = max;
            rmse = Math.Sqrt(sumSq / overlap);
        }

        private sealed class Pair
        {
            public Pair(LinkedFace3D linkedFace3D, Point3D internalPoint3D, SolarCoverageSimulationResult result)
            {
                LinkedFace3D = linkedFace3D;
                InternalPoint3D = internalPoint3D;
                Result = result;
            }

            public LinkedFace3D LinkedFace3D { get; }
            public Point3D InternalPoint3D { get; }
            public SolarCoverageSimulationResult Result { get; }
        }

        private readonly struct HourKey : IEquatable<HourKey>
        {
            public readonly byte Month;
            public readonly byte Day;
            public readonly byte Hour;

            public HourKey(DateTime dt)
            {
                Month = (byte)dt.Month;
                Day = (byte)dt.Day;
                Hour = (byte)dt.Hour;
            }

            public bool Equals(HourKey other) => Month == other.Month && Day == other.Day && Hour == other.Hour;
            public override bool Equals(object obj) => obj is HourKey other && Equals(other);
            public override int GetHashCode() => (Month << 16) | (Day << 8) | Hour;
        }

        private static DateTime CeilingToHour(DateTime dt)
        {
            if (dt.Minute == 0 && dt.Second == 0 && dt.Millisecond == 0)
            {
                return dt;
            }

            // Truncate to the start of the current hour and add one, so X:30 -> (X+1):00.
            // Year roll-over (Dec 31 23:30 -> Jan 1 00:00 next year) is fine — HourKey discards the year.
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Kind).AddHours(1);
        }

        private static Dictionary<HourKey, double> BuildHourMap(SolarCoverageSimulationResult result)
        {
            Dictionary<HourKey, double> map = new Dictionary<HourKey, double>();
            if (result == null) return map;

            List<Tuple<DateTime, double>> coverage = result.Coverage;
            if (coverage == null) return map;

            foreach (Tuple<DateTime, double> entry in coverage)
            {
                if (entry == null) continue;
                if (double.IsNaN(entry.Item2)) continue;

                HourKey key = new HourKey(CeilingToHour(entry.Item1));
                // First-write-wins: if multiple raw timestamps round to the same hour bucket
                // (sub-hour input), keep the earliest one. Annual sims have one value per hour.
                if (!map.ContainsKey(key))
                {
                    map[key] = entry.Item2;
                }
            }

            return map;
        }
    }
}
