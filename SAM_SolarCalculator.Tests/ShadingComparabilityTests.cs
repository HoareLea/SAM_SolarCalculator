// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using Xunit;
using SAM.Analytical.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The comparability predicate: every single-field change is refused with a reason naming the
    /// field, and shuffling the aperture list alone leaves the signature unchanged (I12).
    /// </summary>
    public class ShadingComparabilityTests
    {
        private static ShadingAnalysisSignature Baseline()
        {
            return new ShadingAnalysisSignature(
                new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000001"), new Guid("aaaaaaa1-0000-0000-0000-000000000002") },
                new List<int> { 8, 6 },
                "contexthash", "targethash",
                0.5, 2.0, 2018, 30.0,
                51.5, -0.13, 0.0,
                "weatherhash", "desirabilityhash", "SeasonalDesirability");
        }

        [Fact]
        public void Identical_Signatures_Are_Comparable()
        {
            Assert.True(Query.Comparable(Baseline(), Baseline(), out string reason));
            Assert.Null(reason);
        }

        [Fact]
        public void Shuffling_The_Aperture_List_Alone_Keeps_The_Hash_Unchanged()
        {
            ShadingAnalysisSignature baseline = Baseline();
            ShadingAnalysisSignature shuffled = new ShadingAnalysisSignature(
                new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000002"), new Guid("aaaaaaa1-0000-0000-0000-000000000001") },
                new List<int> { 6, 8 },
                "contexthash", "targethash",
                0.5, 2.0, 2018, 30.0,
                51.5, -0.13, 0.0,
                "weatherhash", "desirabilityhash", "SeasonalDesirability");

            // Sorting makes the two byte-identical.
            Assert.Equal(baseline.SignatureHash, shuffled.SignatureHash);
            Assert.True(Query.Comparable(baseline, shuffled, out string _));
        }

        [Fact]
        public void Each_Single_Field_Change_Is_Refused_With_The_Field_Named()
        {
            ShadingAnalysisSignature baseline = Baseline();

            var cases = new Dictionary<string, ShadingAnalysisSignature>
            {
                {
                    "aperture",
                    new ShadingAnalysisSignature(
                        new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000001"), new Guid("aaaaaaa1-0000-0000-0000-000000000009") },
                        new List<int> { 8, 6 }, "contexthash", "targethash", 0.5, 2.0, 2018, 30.0, 51.5, -0.13, 0.0, "weatherhash", "desirabilityhash", "SeasonalDesirability")
                },
                {
                    "cells",
                    new ShadingAnalysisSignature(
                        new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000001"), new Guid("aaaaaaa1-0000-0000-0000-000000000002") },
                        new List<int> { 9, 6 }, "contexthash", "targethash", 0.5, 2.0, 2018, 30.0, 51.5, -0.13, 0.0, "weatherhash", "desirabilityhash", "SeasonalDesirability")
                },
                {
                    "context geometry",
                    new ShadingAnalysisSignature(
                        new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000001"), new Guid("aaaaaaa1-0000-0000-0000-000000000002") },
                        new List<int> { 8, 6 }, "OTHERCONTEXT", "targethash", 0.5, 2.0, 2018, 30.0, 51.5, -0.13, 0.0, "weatherhash", "desirabilityhash", "SeasonalDesirability")
                },
                {
                    "target geometry",
                    new ShadingAnalysisSignature(
                        new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000001"), new Guid("aaaaaaa1-0000-0000-0000-000000000002") },
                        new List<int> { 8, 6 }, "contexthash", "OTHERTARGET", 0.5, 2.0, 2018, 30.0, 51.5, -0.13, 0.0, "weatherhash", "desirabilityhash", "SeasonalDesirability")
                },
                {
                    "grid size",
                    new ShadingAnalysisSignature(
                        new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000001"), new Guid("aaaaaaa1-0000-0000-0000-000000000002") },
                        new List<int> { 8, 6 }, "contexthash", "targethash", 0.25, 2.0, 2018, 30.0, 51.5, -0.13, 0.0, "weatherhash", "desirabilityhash", "SeasonalDesirability")
                },
                {
                    "sun-angle step",
                    new ShadingAnalysisSignature(
                        new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000001"), new Guid("aaaaaaa1-0000-0000-0000-000000000002") },
                        new List<int> { 8, 6 }, "contexthash", "targethash", 0.5, 1.0, 2018, 30.0, 51.5, -0.13, 0.0, "weatherhash", "desirabilityhash", "SeasonalDesirability")
                },
                {
                    "weather year",
                    new ShadingAnalysisSignature(
                        new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000001"), new Guid("aaaaaaa1-0000-0000-0000-000000000002") },
                        new List<int> { 8, 6 }, "contexthash", "targethash", 0.5, 2.0, 2019, 30.0, 51.5, -0.13, 0.0, "weatherhash", "desirabilityhash", "SeasonalDesirability")
                },
                {
                    "weather identity",
                    new ShadingAnalysisSignature(
                        new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000001"), new Guid("aaaaaaa1-0000-0000-0000-000000000002") },
                        new List<int> { 8, 6 }, "contexthash", "targethash", 0.5, 2.0, 2018, 30.0, 51.5, -0.13, 0.0, "OTHERWEATHER", "desirabilityhash", "SeasonalDesirability")
                },
                {
                    "desirability",
                    new ShadingAnalysisSignature(
                        new List<Guid> { new Guid("aaaaaaa1-0000-0000-0000-000000000001"), new Guid("aaaaaaa1-0000-0000-0000-000000000002") },
                        new List<int> { 8, 6 }, "contexthash", "targethash", 0.5, 2.0, 2018, 30.0, 51.5, -0.13, 0.0, "weatherhash", "OTHERDESIRABILITY", "SeasonalDesirability")
                },
            };

            foreach (KeyValuePair<string, ShadingAnalysisSignature> pair in cases)
            {
                Assert.False(Query.Comparable(baseline, pair.Value, out string reason), pair.Key + " must not be comparable");
                Assert.NotNull(reason);
                Assert.False(string.IsNullOrWhiteSpace(reason), pair.Key + " must name the field");
            }
        }

        [Fact]
        public void The_Display_Name_Never_Participates_In_The_Comparison()
        {
            ShadingAnalysisSignature baseline = Baseline();
            ShadingAnalysisSignature renamed = new ShadingAnalysisSignature(
                baseline.ApertureGuids, baseline.CellCounts, baseline.ContextGeometryHash, baseline.TargetGeometryHash,
                baseline.GridSize, baseline.SunAngleStep, baseline.Year, baseline.TimeShiftInMinutes,
                baseline.Latitude, baseline.Longitude, baseline.TimeZoneOffset,
                baseline.WeatherIdentityHash, baseline.DesirabilityHash, "A Different Display Name");

            Assert.True(Query.Comparable(baseline, renamed, out string _));
            Assert.Equal(baseline.SignatureHash, renamed.SignatureHash);
        }
    }
}
