// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Stage 7: extracts the ideal shading geometry for one aperture from its Stage 6 field.
        ///
        /// The threshold is resolved first (absolute, cumulative-capture or max-fraction), then the
        /// voxel selection and all of its metrics are computed from the SCALAR FIELD alone, and only
        /// then is a display mesh attempted. Meshing runs inside a guard: any failure leaves the
        /// mesh null with a recorded reason and returns a fully populated numerical result, because
        /// the field — not the mesh — is the analysis.
        ///
        /// The iso-surface is taken over a lattice of the voxel CENTRES padded by one sample on
        /// every side. The padding is held below the iso level, which closes the surface where the
        /// selected region runs into the edge of the shading volume; without it a region touching
        /// the boundary would extract as an open shell and have no meaningful volume.
        ///
        /// Note the two different notions of "selection" that are deliberately kept consistent:
        /// the voxel list uses a strict Score &gt; threshold test, and the iso-surface uses the same
        /// level, so the mesh encloses the selected voxels rather than some neighbouring set.
        /// </summary>
        /// <param name="field">The Stage 6 field. Source of truth for every number produced.</param>
        /// <param name="thresholdMethod">How to derive the extraction level.</param>
        /// <param name="thresholdParameter">Fraction for CumulativeCapture / MaxFraction; the absolute kWh level for Absolute.</param>
        /// <param name="wantedSolarPenalty">Lambda in Score = Unwanted - lambda x Wanted.</param>
        /// <param name="requireFacadeContact">Drop regions that do not reach back to the aperture plane.</param>
        /// <param name="keepLargestRegionOnly">Keep only the largest surviving region.</param>
        public static IdealShadingResult IdealShadingResult(this ShadingPotentialField field, ShadingThresholdMethod thresholdMethod, double thresholdParameter, double wantedSolarPenalty = 1.0, bool requireFacadeContact = false, bool keepLargestRegionOnly = false)
        {
            ShadingVolume volume = field?.Volume;
            if (volume == null)
            {
                return null;
            }

            double threshold;
            switch (thresholdMethod)
            {
                case ShadingThresholdMethod.CumulativeCapture:
                    threshold = field.ThresholdForCumulativeCapture(thresholdParameter, wantedSolarPenalty);
                    break;
                case ShadingThresholdMethod.MaxFraction:
                    threshold = field.ThresholdForMaxFraction(thresholdParameter, wantedSolarPenalty);
                    break;
                default:
                    threshold = thresholdParameter;
                    break;
            }

            // A field with no positive benefit yields no threshold and therefore an empty result —
            // a valid answer ("nothing here is worth shading"), not an error.
            List<int> voxelIndices = double.IsNaN(threshold)
                ? new List<int>()
                : Query.IdealShadingVoxels(field, threshold, wantedSolarPenalty, requireFacadeContact, keepLargestRegionOnly);

            if (voxelIndices == null)
            {
                voxelIndices = new List<int>();
            }

            List<int> regionSizes = new List<int>();
            List<List<int>> regions = Query.ShadingVoxelRegions(field, voxelIndices);
            if (regions != null)
            {
                foreach (List<int> region in regions)
                {
                    regionSizes.Add(region.Count);
                }
            }

            Query.TryGetIdealShadingMetrics(field, voxelIndices, wantedSolarPenalty,
                out double projectedArea, out double enclosedVolume, out double maxProjectionDepth, out double capturedPotentialFraction);

            Mesh3D mesh = null;
            string meshFailureReason = null;
            if (voxelIndices.Count == 0)
            {
                meshFailureReason = "No voxel exceeds the extraction threshold: the selection is empty.";
            }
            else
            {
                try
                {
                    mesh = ExtractMesh(field, volume, voxelIndices, threshold, wantedSolarPenalty);
                    if (mesh == null)
                    {
                        meshFailureReason = "Iso-surface extraction returned no surface.";
                    }
                    else if (mesh.TrianglesCount == 0)
                    {
                        meshFailureReason = "Iso-surface extraction produced an empty surface.";
                    }
                }
                catch (Exception exception)
                {
                    // The numbers above are already final; a meshing failure must not lose them.
                    mesh = null;
                    meshFailureReason = "Iso-surface extraction failed: " + exception.Message;
                }
            }

            return new IdealShadingResult(
                field.ApertureGuid, threshold, thresholdMethod, thresholdParameter, wantedSolarPenalty,
                requireFacadeContact, keepLargestRegionOnly, voxelIndices, regionSizes,
                capturedPotentialFraction, projectedArea, enclosedVolume, maxProjectionDepth,
                field.DesirabilityStrategyName, field.GridSize, field.SunAngleStep, volume.VoxelSize,
                mesh, meshFailureReason);
        }

        /// <summary>
        /// Builds the padded voxel-centre lattice and runs the iso-surface over it.
        ///
        /// Voxels that survived the region filters carry their true score; voxels that were
        /// filtered out are forced below the threshold so the surface follows the FILTERED
        /// selection rather than the raw level set. Otherwise asking for "the largest region only"
        /// would still mesh the specks it just discarded.
        /// </summary>
        private static Mesh3D ExtractMesh(ShadingPotentialField field, ShadingVolume volume, List<int> voxelIndices, double threshold, double wantedSolarPenalty)
        {
            int countX = volume.CountX;
            int countY = volume.CountY;
            int countZ = volume.CountZ;

            int sampleX = countX + 2;
            int sampleY = countY + 2;
            int sampleZ = countZ + 2;

            // One voxel-size below the threshold's own scale, so padding and rejected voxels sit
            // strictly outside without distorting the interpolated cut position.
            double outside = threshold - 1.0;
            double minimum = field.MinScore(wantedSolarPenalty);
            if (!double.IsNaN(minimum) && minimum - 1.0 < outside)
            {
                outside = minimum - 1.0;
            }

            double[] values = new double[sampleX * sampleY * sampleZ];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = outside;
            }

            HashSet<int> selected = new HashSet<int>(voxelIndices);
            foreach (int voxelIndex in selected)
            {
                int i = voxelIndex % countX;
                int j = (voxelIndex / countX) % countY;
                int k = voxelIndex / (countX * countY);

                double score = field.Score(voxelIndex, wantedSolarPenalty);
                values[(i + 1) + sampleX * ((j + 1) + sampleY * (k + 1))] = score;
            }

            // Sample (0,0,0) is one voxel outside the minimum corner, at a voxel CENTRE offset.
            double voxelSize = volume.VoxelSize;
            Point3D origin = volume.ToWorld(-0.5 * voxelSize, -0.5 * voxelSize, -0.5 * voxelSize);

            return Geometry.SolarCalculator.Create.IsoSurface(
                values, sampleX, sampleY, sampleZ, threshold,
                origin, volume.AxisX, volume.AxisY, volume.AxisZ, voxelSize);
        }
    }
}
