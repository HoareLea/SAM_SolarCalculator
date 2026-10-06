// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.Weather.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Builds the first-hit attribution cache over the SAME sun groups a visibility cache uses,
        /// so the two are index-compatible in the bin dimension: bin b means the same sun group in
        /// both.
        ///
        /// THE CELL DIMENSION IS A WINDOW, NOT A MIRROR. A visibility cache is normally built over
        /// the WHOLE model's apertures flattened into one cell space, while attribution is only ever
        /// wanted for the one aperture a candidate device sits in front of. So the cells given here
        /// are a CONTIGUOUS SLICE of that space, starting at cellIndexOffset, and the cache built
        /// from them is indexed LOCALLY: its cell 0 is the slice's first cell, i.e. the visibility
        /// cache's cell cellIndexOffset. A reader holding both must therefore address the visibility
        /// cache at cellIndexOffset + c and this cache at c — see Create.ShadingPerformance, which is
        /// the only place that pairing is made.
        ///
        /// Requiring the two cell counts to be EQUAL instead (which is what this did before) would
        /// mean attribution could only ever be built for a model with exactly one aperture, and
        /// would also cost a whole-model ray-cast per candidate device — thousands of times the work
        /// actually needed. The window is both the correct contract and the affordable one.
        ///
        /// The occluder GUID table is the DISTINCT occluder GUIDs in input order, and the stored
        /// index is an index into that table, not into the caller's list, so duplicate-GUID faces
        /// collapse onto one entry and the table hash pins the meaning of every stored value.
        ///
        /// Determinism: each sun group writes its own row and rows are never shared, so the
        /// Parallel.For has no accumulation order to disagree about.
        /// </summary>
        /// <param name="solarVisibilityCache">Supplies the sun groups and the sampling identity.</param>
        /// <param name="occluders">Context plus any candidate shading faces, in a stable order.</param>
        /// <param name="analysisCells">The window of cells to attribute, in visibility-cache order.</param>
        /// <param name="cellIndexOffset">Where that window starts in the visibility cache's cell space. 0 when the cache was built for these cells alone.</param>
        /// <param name="tolerance_Area">Area tolerance.</param>
        /// <param name="tolerance_Snap">Snap tolerance (also the ray-start offset).</param>
        /// <param name="tolerance_Angle">Angle tolerance, RADIANS.</param>
        /// <param name="tolerance_Distance">Distance tolerance.</param>
        /// <param name="baseLitSamplesOnly">
        /// Trace ONLY the samples the visibility cache reports as lit, leaving the rest at
        /// <see cref="Query.FirstHitNotEvaluated"/>. Exactly equivalent for Stage 8 accounting, which
        /// never consults attribution at an unlit sample — see the remarks on this parameter in
        /// Analytical.SolarCalculator.Create.CandidateAttributionCache. False builds the complete
        /// table, which is what a caller inspecting attribution in its own right wants.
        /// </param>
        public static SolarAttributionCache SolarAttributionCache(this SolarVisibilityCache solarVisibilityCache, List<LinkedFace3D> occluders, List<AnalysisCell> analysisCells, int cellIndexOffset = 0, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, bool baseLitSamplesOnly = false)
        {
            return SolarAttributionCache(solarVisibilityCache, occluders, analysisCells, cellIndexOffset, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, baseLitSamplesOnly, null);
        }

        /// <summary>
        /// As the overload above, with an ACTIVE-BIN MASK: rows whose mask entry is false are left
        /// UNTRACED (null) and their bin indices are never removed, so <see cref="SolarAttributionCache.BinCount"/>
        /// stays equal to the visibility cache's bin count and the two caches remain index-compatible
        /// in the bin dimension exactly as before.
        ///
        /// WHY THE ROWS STAY PRESENT. The pruning is a performance optimisation for candidate
        /// scoring, not a change to the cache contract. A null row already exists today (no lit
        /// sample at that sun group) and reads as a negative sentinel; a masked row behaves the same,
        /// so downstream accounting can be told which bins may be read and which never will be.
        /// Because the identity and the attribution-table hash cover the occluder table and the
        /// sampling, not the traced rows, a masked cache carries the same identity as the full one.
        ///
        /// Rows are private to their own sun group, so the mask check preserves the build's
        /// determinism: the Parallel.For still has no accumulation order to disagree about.
        /// </summary>
        /// <param name="solarVisibilityCache">Supplies the sun groups and the sampling identity.</param>
        /// <param name="occluders">Context plus any candidate shading faces, in a stable order.</param>
        /// <param name="analysisCells">The window of cells to attribute, in visibility-cache order.</param>
        /// <param name="cellIndexOffset">Where that window starts in the visibility cache's cell space.</param>
        /// <param name="tolerance_Area">Area tolerance.</param>
        /// <param name="tolerance_Snap">Snap tolerance (also the ray-start offset).</param>
        /// <param name="tolerance_Angle">Angle tolerance, RADIANS.</param>
        /// <param name="tolerance_Distance">Distance tolerance.</param>
        /// <param name="baseLitSamplesOnly">Trace only the samples the baseline reports as lit.</param>
        /// <param name="activeBins">Per-bin mask, one entry per sun group. Null = trace every bin (the full path). A bin with a false entry, or an index past the mask, is left untraced.</param>
        public static SolarAttributionCache SolarAttributionCache(this SolarVisibilityCache solarVisibilityCache, List<LinkedFace3D> occluders, List<AnalysisCell> analysisCells, int cellIndexOffset, double tolerance_Area, double tolerance_Snap, double tolerance_Angle, double tolerance_Distance, bool baseLitSamplesOnly, bool[] activeBins)
        {
            List<SunBin> bins = solarVisibilityCache?.Bins;
            if (bins == null || bins.Count == 0 || analysisCells == null || analysisCells.Count == 0)
            {
                return null;
            }

            // The window must lie inside the visibility cache's cell space, or the two could not be
            // read together at all.
            if (cellIndexOffset < 0 || cellIndexOffset + analysisCells.Count > solarVisibilityCache.CellCount)
            {
                return null;
            }

            List<Point3D> points = new List<Point3D>(analysisCells.Count);
            List<Vector3D> normals = new List<Vector3D>(analysisCells.Count);
            foreach (AnalysisCell analysisCell in analysisCells)
            {
                points.Add(analysisCell?.InternalPoint3D);
                normals.Add(analysisCell?.Face3D?.GetPlane()?.Normal?.Unit);
            }

            List<LinkedFace3D> occluders_Local = occluders ?? new List<LinkedFace3D>();

            // Distinct GUIDs in input order, plus the map from occluder position to table index.
            List<Guid> occluderGuids = new List<Guid>();
            Dictionary<Guid, int> guidToTableIndex = new Dictionary<Guid, int>();
            int[] occluderToTable = new int[occluders_Local.Count];
            for (int i = 0; i < occluders_Local.Count; i++)
            {
                Guid guid = occluders_Local[i] == null ? Guid.Empty : occluders_Local[i].Guid;
                if (!guidToTableIndex.TryGetValue(guid, out int tableIndex))
                {
                    tableIndex = occluderGuids.Count;
                    occluderGuids.Add(guid);
                    guidToTableIndex[guid] = tableIndex;
                }

                occluderToTable[i] = tableIndex;
            }

            int[][] firstHit = new int[bins.Count][];

            int cellCount = analysisCells.Count;

            Parallel.For(0, bins.Count, b =>
            {
                if (activeBins != null && (b >= activeBins.Length || !activeBins[b]))
                {
                    return; // masked row: left null/untraced, bin index preserved
                }

                Vector3D representativeDirection = bins[b]?.RepresentativeDirection;
                if (representativeDirection == null || !representativeDirection.IsValid())
                {
                    return;
                }

                Vector3D towardSun = representativeDirection.GetNegated();

                List<Point3D> points_Bin = points;
                List<Vector3D> normals_Bin = normals;
                List<int> litCells = null;

                if (baseLitSamplesOnly)
                {
                    litCells = new List<int>(cellCount);
                    for (int c = 0; c < cellCount; c++)
                    {
                        if (solarVisibilityCache.IsLit(b, cellIndexOffset + c))
                        {
                            litCells.Add(c);
                        }
                    }

                    // No lit sample in this window at this sun group: the accounting cannot read a
                    // single entry of this row, so the row is left null and nothing is traced.
                    if (litCells.Count == 0)
                    {
                        return;
                    }

                    if (litCells.Count < cellCount)
                    {
                        points_Bin = new List<Point3D>(litCells.Count);
                        normals_Bin = new List<Vector3D>(litCells.Count);
                        foreach (int c in litCells)
                        {
                            points_Bin.Add(points[c]);
                            normals_Bin.Add(normals[c]);
                        }
                    }
                    else
                    {
                        litCells = null; // every sample is lit: trace the row as it stands
                    }
                }

                int[] traced = Query.CellFirstHit(occluders_Local, points_Bin, normals_Bin, towardSun, tolerance_Area, tolerance_Angle, tolerance_Distance, tolerance_Snap);
                if (traced == null)
                {
                    return;
                }

                // Translate occluder-list positions into stable table indices; sentinels pass through.
                for (int i = 0; i < traced.Length; i++)
                {
                    if (traced[i] >= 0)
                    {
                        traced[i] = occluderToTable[traced[i]];
                    }
                }

                if (litCells == null)
                {
                    firstHit[b] = traced;
                    return;
                }

                // Scatter the traced subset back into a full-width row, so the cache stays addressable
                // at every local cell index and the untraced samples say so.
                int[] hits = new int[cellCount];
                for (int c = 0; c < cellCount; c++)
                {
                    hits[c] = Query.FirstHitNotEvaluated;
                }

                for (int i = 0; i < litCells.Count; i++)
                {
                    hits[litCells[i]] = traced[i];
                }

                firstHit[b] = hits;
            });

            string contextGeometryHash = Geometry.SolarCalculator.Query.GeometryHash(occluders_Local, tolerance_Distance);
            string targetGeometryHash = Geometry.SolarCalculator.Query.TargetHash(analysisCells, tolerance_Distance);

            return new SolarAttributionCache(
                contextGeometryHash, targetGeometryHash,
                solarVisibilityCache.CellSize, solarVisibilityCache.BinSizeDegrees,
                solarVisibilityCache.SunPositionShiftInMinutes, solarVisibilityCache.Year,
                analysisCells.Count, occluderGuids, firstHit, cellIndexOffset);
        }
    }
}
