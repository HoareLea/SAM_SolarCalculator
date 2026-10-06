// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>How the extraction threshold was chosen (provenance, so a result can be reproduced).</summary>
    public enum ShadingThresholdMethod
    {
        /// <summary>An absolute field score in kWh, supplied by the caller.</summary>
        Absolute,

        /// <summary>Smallest score retaining the requested fraction of the field's positive benefit.</summary>
        CumulativeCapture,

        /// <summary>A fraction of the field's maximum voxel score.</summary>
        MaxFraction,
    }

    /// <summary>
    /// Stage 7 result: the ideal shading geometry for one aperture, extracted from the Stage 6
    /// field at a chosen threshold.
    ///
    /// THE FIELD REMAINS THE SOURCE OF TRUTH. Everything numeric here — the selected voxel set,
    /// the captured benefit fraction, projected area, volume, depth, the connected-region
    /// breakdown — is computed from the scalar field and is complete on its own. The Mesh3D is a
    /// DERIVED, DISPOSABLE display and take-off artefact. If iso-surfacing fails the mesh is null
    /// and MeshFailureReason says why, and every number above is still valid and still populated;
    /// a meshing problem must never be able to invalidate the analysis.
    /// </summary>
    public class IdealShadingResult : IJSAMObject, ISolarObject
    {
        /// <summary>
        /// 2 — CapturedBenefitFraction became CapturedPotentialFraction (Stage 10.2). The quantity is
        /// unchanged; the old name asserted it was a share of solar energy, which it is not. Schema 1
        /// files still load, reading the old key.
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        private int schemaVersion = CurrentSchemaVersion;
        private Guid apertureGuid;
        private double threshold = double.NaN;
        private ShadingThresholdMethod thresholdMethod = ShadingThresholdMethod.Absolute;
        private double thresholdParameter = double.NaN;
        private double wantedSolarPenalty = 1.0;
        private bool requireFacadeContact;
        private bool keepLargestRegionOnly;
        private List<int> voxelIndices;
        private List<int> regionSizes;
        private double capturedPotentialFraction = double.NaN;
        private double projectedArea = double.NaN;
        private double enclosedVolume = double.NaN;
        private double maxProjectionDepth = double.NaN;
        private string desirabilityStrategyName;
        private double gridSize = double.NaN;
        private double sunAngleStep = double.NaN;
        private double voxelSize = double.NaN;
        private Mesh3D mesh;
        private string meshFailureReason;

        public IdealShadingResult(
            Guid apertureGuid,
            double threshold,
            ShadingThresholdMethod thresholdMethod,
            double thresholdParameter,
            double wantedSolarPenalty,
            bool requireFacadeContact,
            bool keepLargestRegionOnly,
            IEnumerable<int> voxelIndices,
            IEnumerable<int> regionSizes,
            double capturedPotentialFraction,
            double projectedArea,
            double enclosedVolume,
            double maxProjectionDepth,
            string desirabilityStrategyName,
            double gridSize,
            double sunAngleStep,
            double voxelSize,
            Mesh3D mesh,
            string meshFailureReason)
        {
            this.apertureGuid = apertureGuid;
            this.threshold = threshold;
            this.thresholdMethod = thresholdMethod;
            this.thresholdParameter = thresholdParameter;
            this.wantedSolarPenalty = wantedSolarPenalty;
            this.requireFacadeContact = requireFacadeContact;
            this.keepLargestRegionOnly = keepLargestRegionOnly;
            this.voxelIndices = voxelIndices == null ? new List<int>() : new List<int>(voxelIndices);
            this.regionSizes = regionSizes == null ? new List<int>() : new List<int>(regionSizes);
            this.capturedPotentialFraction = capturedPotentialFraction;
            this.projectedArea = projectedArea;
            this.enclosedVolume = enclosedVolume;
            this.maxProjectionDepth = maxProjectionDepth;
            this.desirabilityStrategyName = desirabilityStrategyName;
            this.gridSize = gridSize;
            this.sunAngleStep = sunAngleStep;
            this.voxelSize = voxelSize;
            this.mesh = mesh == null ? null : new Mesh3D(mesh);
            this.meshFailureReason = meshFailureReason;
        }

        public IdealShadingResult(IdealShadingResult idealShadingResult)
        {
            if (idealShadingResult != null)
            {
                schemaVersion = idealShadingResult.schemaVersion;
                apertureGuid = idealShadingResult.apertureGuid;
                threshold = idealShadingResult.threshold;
                thresholdMethod = idealShadingResult.thresholdMethod;
                thresholdParameter = idealShadingResult.thresholdParameter;
                wantedSolarPenalty = idealShadingResult.wantedSolarPenalty;
                requireFacadeContact = idealShadingResult.requireFacadeContact;
                keepLargestRegionOnly = idealShadingResult.keepLargestRegionOnly;
                voxelIndices = new List<int>(idealShadingResult.voxelIndices ?? new List<int>());
                regionSizes = new List<int>(idealShadingResult.regionSizes ?? new List<int>());
                capturedPotentialFraction = idealShadingResult.capturedPotentialFraction;
                projectedArea = idealShadingResult.projectedArea;
                enclosedVolume = idealShadingResult.enclosedVolume;
                maxProjectionDepth = idealShadingResult.maxProjectionDepth;
                desirabilityStrategyName = idealShadingResult.desirabilityStrategyName;
                gridSize = idealShadingResult.gridSize;
                sunAngleStep = idealShadingResult.sunAngleStep;
                voxelSize = idealShadingResult.voxelSize;
                mesh = idealShadingResult.mesh == null ? null : new Mesh3D(idealShadingResult.mesh);
                meshFailureReason = idealShadingResult.meshFailureReason;
            }
        }

        public IdealShadingResult(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public int SchemaVersion { get { return schemaVersion; } }

        public Guid ApertureGuid { get { return apertureGuid; } }

        /// <summary>The absolute field score used as the extraction level, kWh.</summary>
        public double Threshold { get { return threshold; } }

        public ShadingThresholdMethod ThresholdMethod { get { return thresholdMethod; } }

        /// <summary>The fraction the method was asked for (NaN for an absolute threshold).</summary>
        public double ThresholdParameter { get { return thresholdParameter; } }

        public double WantedSolarPenalty { get { return wantedSolarPenalty; } }

        public bool RequireFacadeContact { get { return requireFacadeContact; } }

        public bool KeepLargestRegionOnly { get { return keepLargestRegionOnly; } }

        /// <summary>Selected voxels, ascending linear index (deterministic).</summary>
        public List<int> VoxelIndices { get { return voxelIndices == null ? null : new List<int>(voxelIndices); } }

        public int SelectedVoxelCount { get { return voxelIndices == null ? 0 : voxelIndices.Count; } }

        /// <summary>Sizes of the 6-connected regions of the selection, descending.</summary>
        public List<int> RegionSizes { get { return regionSizes == null ? null : new List<int>(regionSizes); } }

        public int RegionCount { get { return regionSizes == null ? 0 : regionSizes.Count; } }

        /// <summary>
        /// Share of the FIELD'S POSITIVE SHADING POTENTIAL that the selected region contains:
        /// sum of the selected voxels' positive scores over
        /// <see cref="ShadingPotentialField.PositiveTotal"/>.
        ///
        /// IT IS NOT A SHARE OF ANY SOLAR ENERGY, and it was called CapturedBenefitFraction until
        /// Stage 10.2, which is precisely how it got read as one. Both numerator and denominator are
        /// sums over voxels, and a single ray contributes to every voxel along its path — so this is
        /// a ratio of spatial potentials, and "captures 90 %" means the region holds 90 % of the
        /// field's positive potential, NOT that a device built there would block 90 % of the
        /// unwanted solar. It will not: the region is a volume, a device is a surface, and the
        /// Stage 7 mesh is display geometry that in testing intercepted materially less than the
        /// region it draws.
        ///
        /// What it IS good for is the job Stage 7 gives it: choosing a threshold, and comparing two
        /// thresholds on the same field. For what a device blocks, build one and measure it with
        /// <see cref="ShadingPerformance"/>.
        /// </summary>
        public double CapturedPotentialFraction { get { return capturedPotentialFraction; } }

        /// <summary>Selection footprint projected on the aperture plane, m2.</summary>
        public double ProjectedArea { get { return projectedArea; } }

        /// <summary>Selected voxel volume, m3.</summary>
        public double EnclosedVolume { get { return enclosedVolume; } }

        /// <summary>Furthest outward projection of the selection, m.</summary>
        public double MaxProjectionDepth { get { return maxProjectionDepth; } }

        public string DesirabilityStrategyName { get { return desirabilityStrategyName; } }

        public double GridSize { get { return gridSize; } }

        public double SunAngleStep { get { return sunAngleStep; } }

        public double VoxelSize { get { return voxelSize; } }

        /// <summary>
        /// Display/take-off geometry. NULL is a legitimate outcome — an empty selection, or an
        /// iso-surfacing failure — and never invalidates the numbers above. Check
        /// MeshFailureReason to tell the two apart.
        /// </summary>
        public Mesh3D Mesh { get { return mesh == null ? null : new Mesh3D(mesh); } }

        /// <summary>Why Mesh is null, or null when meshing succeeded.</summary>
        public string MeshFailureReason { get { return meshFailureReason; } }

        /// <summary>True when the numerical result stands but no display geometry was produced.</summary>
        public bool HasMesh { get { return mesh != null && mesh.TrianglesCount > 0; } }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("SchemaVersion")) { schemaVersion = jObject["SchemaVersion"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("ApertureGuid")) { Guid.TryParse(jObject["ApertureGuid"]?.GetValue<string>(), out apertureGuid); }
            if (jObject.ContainsKey("Threshold")) { threshold = jObject["Threshold"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("ThresholdMethod") && Enum.TryParse(jObject["ThresholdMethod"]?.GetValue<string>(), out ShadingThresholdMethod method)) { thresholdMethod = method; }
            if (jObject.ContainsKey("ThresholdParameter")) { thresholdParameter = jObject["ThresholdParameter"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("WantedSolarPenalty")) { wantedSolarPenalty = jObject["WantedSolarPenalty"]?.GetValue<double>() ?? 1.0; }
            if (jObject.ContainsKey("RequireFacadeContact")) { requireFacadeContact = jObject["RequireFacadeContact"]?.GetValue<bool>() ?? false; }
            if (jObject.ContainsKey("KeepLargestRegionOnly")) { keepLargestRegionOnly = jObject["KeepLargestRegionOnly"]?.GetValue<bool>() ?? false; }
            // Renamed at Stage 10.2. The old key is still read so a saved schema-1 result keeps its
            // number instead of silently coming back NaN; only the new key is ever written.
            if (jObject.ContainsKey("CapturedPotentialFraction")) { capturedPotentialFraction = jObject["CapturedPotentialFraction"]?.GetValue<double>() ?? double.NaN; }
            else if (jObject.ContainsKey("CapturedBenefitFraction")) { capturedPotentialFraction = jObject["CapturedBenefitFraction"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("ProjectedArea")) { projectedArea = jObject["ProjectedArea"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("EnclosedVolume")) { enclosedVolume = jObject["EnclosedVolume"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("MaxProjectionDepth")) { maxProjectionDepth = jObject["MaxProjectionDepth"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("DesirabilityStrategyName")) { desirabilityStrategyName = jObject["DesirabilityStrategyName"]?.GetValue<string>(); }
            if (jObject.ContainsKey("GridSize")) { gridSize = jObject["GridSize"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("SunAngleStep")) { sunAngleStep = jObject["SunAngleStep"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("VoxelSize")) { voxelSize = jObject["VoxelSize"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("MeshFailureReason")) { meshFailureReason = jObject["MeshFailureReason"]?.GetValue<string>(); }

            voxelIndices = ReadIntegers(jObject, "VoxelIndices");
            regionSizes = ReadIntegers(jObject, "RegionSizes");

            mesh = jObject.ContainsKey("Mesh") ? new Mesh3D(jObject["Mesh"] as JsonObject) : null;
            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("SchemaVersion", schemaVersion);
            jObject.Add("ApertureGuid", apertureGuid.ToString());
            jObject.Add("Threshold", threshold);
            jObject.Add("ThresholdMethod", thresholdMethod.ToString());
            jObject.Add("ThresholdParameter", thresholdParameter);
            jObject.Add("WantedSolarPenalty", wantedSolarPenalty);
            jObject.Add("RequireFacadeContact", requireFacadeContact);
            jObject.Add("KeepLargestRegionOnly", keepLargestRegionOnly);
            jObject.Add("CapturedPotentialFraction", capturedPotentialFraction);
            jObject.Add("ProjectedArea", projectedArea);
            jObject.Add("EnclosedVolume", enclosedVolume);
            jObject.Add("MaxProjectionDepth", maxProjectionDepth);
            jObject.Add("GridSize", gridSize);
            jObject.Add("SunAngleStep", sunAngleStep);
            jObject.Add("VoxelSize", voxelSize);

            if (desirabilityStrategyName != null) { jObject.Add("DesirabilityStrategyName", desirabilityStrategyName); }
            if (meshFailureReason != null) { jObject.Add("MeshFailureReason", meshFailureReason); }

            WriteIntegers(jObject, "VoxelIndices", voxelIndices);
            WriteIntegers(jObject, "RegionSizes", regionSizes);

            if (mesh != null)
            {
                jObject.Add("Mesh", mesh.ToJsonObject());
            }

            return jObject;
        }

        private static List<int> ReadIntegers(JsonObject jObject, string name)
        {
            List<int> result = new List<int>();
            if (!jObject.ContainsKey(name) || !(jObject[name] is JsonArray jsonArray))
            {
                return result;
            }

            foreach (JsonNode node in jsonArray)
            {
                result.Add(node?.GetValue<int>() ?? 0);
            }

            return result;
        }

        private static void WriteIntegers(JsonObject jObject, string name, List<int> values)
        {
            if (values == null)
            {
                return;
            }

            JsonArray jsonArray = new JsonArray();
            foreach (int value in values)
            {
                jsonArray.Add(value);
            }

            jObject.Add(name, jsonArray);
        }
    }
}
