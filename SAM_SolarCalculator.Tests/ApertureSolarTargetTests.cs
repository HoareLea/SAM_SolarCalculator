// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.SolarCalculator.Tests
{
    public class ApertureSolarTargetTests
    {
        private const int ExpectedApertures = 14;

        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private static AnalyticalModel Load(string fileName)
        {
            string path = Path.Combine(FixturesDirectory, fileName);
            Assert.True(File.Exists(path), $"Test fixture missing: {path}");

            List<AnalyticalModel> analyticalModels = SAM.Core.Convert.ToSAM<AnalyticalModel>(path);
            AnalyticalModel analyticalModel = analyticalModels?.FirstOrDefault(x => x != null);
            Assert.NotNull(analyticalModel);
            return analyticalModel;
        }

        [Fact]
        public void NullSelection_Returns_All_SunExposed_External_Apertures()
        {
            AnalyticalModel analyticalModel = Load("ModelB-SolarSimulation.sam");
            Assert.Equal(ExpectedApertures, analyticalModel.GetApertures().Count);

            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, 0.5);
            Assert.NotNull(targets);
            Assert.Equal(ExpectedApertures, targets.Count);

            // Same for an explicitly empty selection.
            List<ApertureSolarTarget> targetsEmpty = analyticalModel.ApertureSolarTargets(new List<Guid>(), 0.5);
            Assert.Equal(targets.Count, targetsEmpty.Count);
        }

        [Fact]
        public void SubsetSelection_Returns_Exactly_That_Subset()
        {
            AnalyticalModel analyticalModel = Load("ModelB-SolarSimulation.sam");

            List<Guid> subset = analyticalModel.GetApertures().Take(3).Select(x => x.Guid).ToList();
            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(subset, 0.5);

            Assert.NotNull(targets);
            Assert.Equal(subset.Count, targets.Count);
            Assert.All(targets, t => Assert.Contains(t.ApertureGuid, subset));
        }

        [Fact]
        public void OutwardNormals_Agree_With_HostPanel_And_Face()
        {
            AnalyticalModel analyticalModel = Load("ModelB-SolarSimulation.sam");
            AdjacencyCluster adjacencyCluster = analyticalModel.AdjacencyCluster;

            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, 0.5);
            Assert.NotNull(targets);
            Assert.NotEmpty(targets);

            foreach (ApertureSolarTarget target in targets)
            {
                Vector3D outward = target.OutwardNormal;
                Assert.NotNull(outward);

                // The target face itself must be oriented outward (normal dot outward > 0).
                Vector3D faceNormal = target.Face3D?.GetPlane()?.Normal;
                Assert.NotNull(faceNormal);
                Assert.True(faceNormal.DotProduct(outward) > 0.0,
                    $"Target {target.ApertureGuid}: face normal {faceNormal} disagrees with outward {outward}");

                // And the outward normal must agree with the host panel's resolved outward direction.
                Panel panel = adjacencyCluster.GetPanels().Find(x => x.Guid == target.PanelGuid);
                Assert.NotNull(panel);
                Vector3D panelOutward = adjacencyCluster.OutwardNormal(panel);
                Assert.NotNull(panelOutward);
                Assert.True(outward.DotProduct(panelOutward) > 0.0,
                    $"Target {target.ApertureGuid}: outward {outward} disagrees with panel outward {panelOutward}");

                // Local frame: Z == outward, X horizontal.
                Plane plane = target.Plane;
                Assert.NotNull(plane);
                Assert.True(System.Math.Abs(plane.AxisZ.DotProduct(outward) - 1.0) < 1e-6);
                Assert.True(System.Math.Abs(plane.AxisX.Z) < 1e-6);
            }
        }

        [Fact]
        public void AnalysisCells_Partition_Aperture_Area()
        {
            AnalyticalModel analyticalModel = Load("ModelB-SolarSimulation.sam");

            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, 0.25);
            Assert.NotNull(targets);

            foreach (ApertureSolarTarget target in targets)
            {
                Assert.True(target.CellCount > 0);
                Assert.True(target.GrossArea > 0);

                double cellAreaSum = 0;
                HashSet<int> indexes = new HashSet<int>();
                foreach (AnalysisCell cell in target.AnalysisCells)
                {
                    Assert.NotNull(cell.Face3D);
                    Assert.NotNull(cell.Centroid);
                    Assert.NotNull(cell.InternalPoint3D);
                    Assert.True(cell.Area > 0);
                    cellAreaSum += cell.Area;
                    Assert.True(indexes.Add(cell.Index), "cell indexes must be stable/unique");
                }

                // Clipped cells partition the opening: summed cell area approximates gross area.
                // (NTS clipping at the boundary costs at most a fraction of a percent.)
                Assert.True(System.Math.Abs(cellAreaSum - target.GrossArea) / target.GrossArea < 0.01,
                    $"Target {target.ApertureGuid}: cell area {cellAreaSum} vs gross {target.GrossArea}");
            }
        }

        [Fact]
        public void AnalysisCells_Match_SampledSimulation_Grid()
        {
            // The extracted cell builder must produce the same cells the sampled simulation uses.
            // Indirect check: same count for a simple rectangular face of known size.
            Face3D face3D = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1),
                new Point3D(2, 0, 1),
                new Point3D(2, 1, 1),
                new Point3D(0, 1, 1),
            }));

            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(face3D, 0.5);
            Assert.NotNull(cells);
            Assert.Equal(8, cells.Count); // 4 x 2 grid, all full cells
            Assert.Equal(2.0, cells.Sum(x => x.Area), 6); // total 2.0 m2
        }

        [Fact]
        public void ApertureSolarTarget_Json_RoundTrip()
        {
            AnalyticalModel analyticalModel = Load("ModelB-SolarSimulation.sam");
            ApertureSolarTarget target = analyticalModel.ApertureSolarTargets(null, 1.0).First();

            ApertureSolarTarget roundTripped = new ApertureSolarTarget(target.ToJsonObject());
            Assert.Equal(target.ApertureGuid, roundTripped.ApertureGuid);
            Assert.Equal(target.PanelGuid, roundTripped.PanelGuid);
            Assert.Equal(target.CellCount, roundTripped.CellCount);
            Assert.Equal(target.GrossArea, roundTripped.GrossArea, 9);
            Assert.Equal(target.Azimuth, roundTripped.Azimuth, 9);
        }
    }
}
