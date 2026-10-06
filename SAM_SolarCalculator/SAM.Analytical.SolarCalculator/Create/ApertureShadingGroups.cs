// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// The deterministic aperture grouping behind the grouped awning analysis: which selected
        /// apertures may share ONE physical awning, and in what left-to-right order.
        ///
        /// ELIGIBILITY. Apertures may share one awning only when all of the following hold:
        ///
        ///   1. same host PanelGuid (automatic grouping mode),
        ///   2. coplanar facade planes within the existing SAM geometry tolerances,
        ///   3. outward normals pointing the same way,
        ///   4. head levels aligned within HeadTolerance (default 0.02 m),
        ///   5. horizontal gaps between consecutive apertures no greater than MaximumGap
        ///      (default 0.20 m),
        ///   6. the combined awning width (aperture envelope plus BOTH side extensions) no greater
        ///      than the product maximum width.
        ///
        /// The outer aperture boundary/envelope is used throughout — never internal glazing or frame
        /// holes. A single aperture is a valid one-member group. Two units are never described as
        /// one: the product is NOT modularly connectable, so a split produces independent groups.
        ///
        /// DETERMINISM. Every aperture is transformed into one common facade-local frame, sorted
        /// left-to-right by minX with the aperture GUID as the final tie-break, and the widest valid
        /// contiguous run is built. When a run exceeds the maximum width it is split by the LARGEST
        /// gap where that produces valid subgroups, otherwise by a stable widest-valid greedy cut.
        /// Input ordering never changes the result; group GUIDs are MD5 identities over the panel
        /// and the sorted membership.
        /// </summary>
        /// <param name="apertureSolarTargets">The apertures to group. Order is irrelevant by contract.</param>
        /// <param name="specification">Product limits that decide the width split. Null = Dakar.</param>
        /// <param name="extensionBeyondJambs">Symmetric side extension [m] beyond the outermost jambs, included in the width check.</param>
        /// <param name="maximumGap">Largest horizontal gap between consecutive apertures that still shares one awning [m]. NaN reads as the default — it is normalised BEFORE grouping, never carried into a comparison.</param>
        /// <param name="headTolerance">Largest head-level spread within one group [m]. NaN reads as the default, exactly as maximumGap.</param>
        /// <returns>Groups sorted deterministically (by group GUID), never null.</returns>
        public static List<ApertureShadingGroup> ApertureShadingGroups(this IEnumerable<ApertureSolarTarget> apertureSolarTargets, AwningSpecification specification = null, double extensionBeyondJambs = 0.0, double maximumGap = GroupedShadingDevice.DefaultMaximumGap, double headTolerance = GroupedShadingDevice.DefaultHeadTolerance)
        {
            specification = specification ?? AwningSpecification.Dakar;

            // NaN IS NOT A CRITERION and is normalised here, before a single comparison runs.
            // Every ">" test against NaN is false, so grouping under NaN would never split on
            // that criterion — while GroupedShadingDevice records the DEFAULT for a NaN, and the
            // ShadingOperation hand-off would then re-establish a different group and refuse a
            // device this algorithm itself produced. Normalising in both places to the SAME
            // constants keeps the criterion the group was formed under and the criterion the
            // device carries identical by construction.
            if (double.IsNaN(maximumGap))
            {
                maximumGap = GroupedShadingDevice.DefaultMaximumGap;
            }

            if (double.IsNaN(headTolerance))
            {
                headTolerance = GroupedShadingDevice.DefaultHeadTolerance;
            }

            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>();
            foreach (ApertureSolarTarget target in apertureSolarTargets ?? new List<ApertureSolarTarget>())
            {
                if (target != null)
                {
                    targets.Add(target);
                }
            }

            // Bucket by host panel first; every member of a group must belong to the same panel.
            Dictionary<Guid, List<ApertureSolarTarget>> byPanel = new Dictionary<Guid, List<ApertureSolarTarget>>();
            foreach (ApertureSolarTarget target in targets)
            {
                if (!byPanel.TryGetValue(target.PanelGuid, out List<ApertureSolarTarget> bucket))
                {
                    bucket = new List<ApertureSolarTarget>();
                    byPanel[target.PanelGuid] = bucket;
                }

                bucket.Add(target);
            }

            List<ApertureShadingGroup> result = new List<ApertureShadingGroup>();
            foreach (KeyValuePair<Guid, List<ApertureSolarTarget>> pair in byPanel)
            {
                foreach (List<ApertureSolarTarget> planeClass in PlaneClasses(pair.Value))
                {
                    foreach (ApertureShadingGroup group in Groups(pair.Key, planeClass, specification, extensionBeyondJambs, maximumGap, headTolerance))
                    {
                        result.Add(group);
                    }
                }
            }

            // Output order is part of the contract: groups sort by their deterministic identity, so
            // two runs with the same apertures return the same sequence whatever the input order was.
            result.Sort((x, y) => x.GroupGuid.CompareTo(y.GroupGuid));
            return result;
        }

        /// <summary>
        /// Splits one panel's apertures into coplanar, same-normal classes. The class frame holder
        /// is the smallest-GUID member, so class membership is input-order independent.
        /// </summary>
        private static List<List<ApertureSolarTarget>> PlaneClasses(List<ApertureSolarTarget> panelTargets)
        {
            List<List<ApertureSolarTarget>> result = new List<List<ApertureSolarTarget>>();

            // Deterministic processing order: smallest GUID first.
            List<ApertureSolarTarget> ordered = new List<ApertureSolarTarget>(panelTargets);
            ordered.Sort((a, b) => a.ApertureGuid.CompareTo(b.ApertureGuid));

            foreach (ApertureSolarTarget target in ordered)
            {
                Plane plane = target.Plane;
                if (plane?.Normal == null)
                {
                    continue;
                }

                bool placed = false;
                foreach (List<ApertureSolarTarget> planeClass in result)
                {
                    Plane reference = planeClass[0].Plane;
                    if (reference?.Normal == null || !Compatible(reference, plane))
                    {
                        continue;
                    }

                    planeClass.Add(target);
                    placed = true;
                    break;
                }

                if (!placed)
                {
                    result.Add(new List<ApertureSolarTarget> { target });
                }
            }

            return result;
        }

        /// <summary>
        /// Coplanar within the SAM tolerances AND pointing the same way: normals parallel within the
        /// angle tolerance and origins within the distance tolerance of one another along the normal.
        /// </summary>
        private static bool Compatible(Plane a, Plane b)
        {
            Vector3D normalA = a.Normal.Unit;
            Vector3D normalB = b.Normal.Unit;

            if (normalA.DotProduct(normalB) < 1.0 - Core.Tolerance.Angle)
            {
                return false; // not parallel, or parallel but opposite
            }

            double offset = (a.Origin.ToVector3D() - b.Origin.ToVector3D()).DotProduct(normalA);
            return Math.Abs(offset) <= Core.Tolerance.MacroDistance;
        }

        private static List<ApertureShadingGroup> Groups(Guid panelGuid, List<ApertureSolarTarget> planeClass, AwningSpecification specification, double extensionBeyondJambs, double maximumGap, double headTolerance)
        {
            List<ApertureShadingGroup> result = new List<ApertureShadingGroup>();

            // The class frame: all members are coplanar with the same outward normal, so every
            // member's extent is readable in the smallest-GUID member's frame.
            Plane classPlane = planeClass[0].Plane;

            List<Member> members = new List<Member>();
            foreach (ApertureSolarTarget target in planeClass)
            {
                if (!TryExtent(target, classPlane, out double minX, out double maxX, out double maxY))
                {
                    continue;
                }

                members.Add(new Member(target, minX, maxX, maxY));
            }

            // Left-to-right, GUID as the final stable tie-break.
            members.Sort((a, b) =>
            {
                int compare = a.MinX.CompareTo(b.MinX);
                return compare != 0 ? compare : a.Target.ApertureGuid.CompareTo(b.Target.ApertureGuid);
            });

            // Contiguous runs: head alignment and maximum gap decide where a run ends.
            List<List<Member>> runs = new List<List<Member>>();
            List<Member> current = new List<Member>();
            double headMax = double.NaN;
            double runMaxX = double.NaN;

            foreach (Member member in members)
            {
                if (current.Count == 0)
                {
                    current.Add(member);
                    headMax = member.MaxY;
                    runMaxX = member.MaxX;
                    continue;
                }

                // The gap is measured from the run's RIGHTMOST edge so far, not from the previously
                // added member's: sorting is by left edge, so the last member added is not
                // necessarily the one that reaches furthest right.
                double gap = member.MinX - runMaxX;
                if (gap > maximumGap || Math.Abs(member.MaxY - headMax) > headTolerance)
                {
                    runs.Add(current);
                    current = new List<Member> { member };
                    headMax = member.MaxY;
                    runMaxX = member.MaxX;
                    continue;
                }

                current.Add(member);
                headMax = Math.Max(headMax, member.MaxY);
                runMaxX = Math.Max(runMaxX, member.MaxX);
            }

            if (current.Count != 0)
            {
                runs.Add(current);
            }

            foreach (List<Member> run in runs)
            {
                foreach (List<Member> groupMembers in SplitByWidth(run, specification, extensionBeyondJambs))
                {
                    result.Add(Build(panelGuid, groupMembers, extensionBeyondJambs));
                }
            }

            return result;
        }

        /// <summary>
        /// Width-valid subgroups of a contiguous run. The largest gap is the preferred split when
        /// both sides are valid; otherwise a stable greedy cut keeps each unit as wide as the
        /// product allows. A single member that alone exceeds the maximum width stays a
        /// one-member group — the product validation reports it rather than the grouping pretending
        /// it did not happen.
        /// </summary>
        private static List<List<Member>> SplitByWidth(List<Member> run, AwningSpecification specification, double extensionBeyondJambs)
        {
            if (Width(run, extensionBeyondJambs) <= specification.MaximumWidth * (1.0 + 1e-9))
            {
                return new List<List<Member>> { run };
            }

            // Preferred split: the largest gap, if both sides stay within the product width.
            int largestGapIndex = -1;
            double largestGap = double.NegativeInfinity;
            double leftMaxX = double.NegativeInfinity;
            for (int i = 0; i < run.Count - 1; i++)
            {
                // Again measured from everything to the left, not from the immediate predecessor.
                leftMaxX = Math.Max(leftMaxX, run[i].MaxX);
                double gap = run[i + 1].MinX - leftMaxX;
                if (gap > largestGap)
                {
                    largestGap = gap;
                    largestGapIndex = i;
                }
            }

            if (largestGapIndex >= 0)
            {
                List<Member> left = run.GetRange(0, largestGapIndex + 1);
                List<Member> right = run.GetRange(largestGapIndex + 1, run.Count - largestGapIndex - 1);
                if (Width(left, extensionBeyondJambs) <= specification.MaximumWidth * (1.0 + 1e-9)
                    && Width(right, extensionBeyondJambs) <= specification.MaximumWidth * (1.0 + 1e-9))
                {
                    List<List<Member>> result = SplitByWidth(left, specification, extensionBeyondJambs);
                    result.AddRange(SplitByWidth(right, specification, extensionBeyondJambs));
                    return result;
                }
            }

            // Stable widest-valid greedy split.
            List<List<Member>> greedy = new List<List<Member>>();
            List<Member> current = new List<Member>();
            foreach (Member member in run)
            {
                if (current.Count == 0)
                {
                    current.Add(member);
                    continue;
                }

                List<Member> grown = new List<Member>(current) { member };
                if (Width(grown, extensionBeyondJambs) <= specification.MaximumWidth * (1.0 + 1e-9))
                {
                    current = grown;
                    continue;
                }

                greedy.Add(current);
                current = new List<Member> { member };
            }

            if (current.Count != 0)
            {
                greedy.Add(current);
            }

            return greedy;
        }

        /// <summary>
        /// The awning width a set of members needs: their combined envelope plus both extensions.
        ///
        /// The extremes are taken over EVERY member, not off the ends of the list. Members are
        /// sorted by their left edge, which does not make the last one the rightmost: one aperture
        /// horizontally containing another would put the wider one first and the envelope would be
        /// measured short — understating the width is exactly the direction that lets an oversized
        /// unit pass the product check.
        /// </summary>
        private static double Width(List<Member> members, double extensionBeyondJambs)
        {
            double minX = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            foreach (Member member in members)
            {
                minX = Math.Min(minX, member.MinX);
                maxX = Math.Max(maxX, member.MaxX);
            }

            return maxX - minX + 2.0 * extensionBeyondJambs;
        }

        private static ApertureShadingGroup Build(Guid panelGuid, List<Member> members, double extensionBeyondJambs)
        {
            // The group frame is the leftmost member's own aperture frame: same axes, translated.
            Plane groupPlane = members[0].Target.Plane;

            double minX = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            foreach (Member member in members)
            {
                // Re-read every extent in the GROUP frame: the class frame and the group frame
                // share axes but not origins, so the stored class-frame numbers do not carry over.
                if (!TryExtent(member.Target, groupPlane, out double memberMinX, out double memberMaxX, out double memberMaxY))
                {
                    continue;
                }

                minX = Math.Min(minX, memberMinX);
                maxX = Math.Max(maxX, memberMaxX);
                maxY = Math.Max(maxY, memberMaxY);
            }

            Guid groupGuid = GroupGuid(panelGuid, members);
            return new ApertureShadingGroup(groupGuid, panelGuid, members.ConvertAll(x => x.Target), groupPlane, minX, maxX, maxY);
        }

        /// <summary>MD5 identity over the panel and the sorted membership — the same group always gets the same Guid.</summary>
        private static Guid GroupGuid(Guid panelGuid, List<Member> members)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("ApertureShadingGroup|");
            stringBuilder.Append(panelGuid);
            foreach (Member member in members)
            {
                stringBuilder.Append('|');
                stringBuilder.Append(member.Target.ApertureGuid);
            }

            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(stringBuilder.ToString())));
            }
        }

        /// <summary>The aperture's planar extent in a facade frame: x across, y up-slope.</summary>
        private static bool TryExtent(ApertureSolarTarget target, Plane plane, out double minX, out double maxX, out double maxY)
        {
            minX = maxX = maxY = double.NaN;

            Geometry.Planar.BoundingBox2D boundingBox2D = plane?.Convert(target?.Face3D)?.GetBoundingBox();
            if (boundingBox2D == null)
            {
                return false;
            }

            minX = boundingBox2D.Min.X;
            maxX = boundingBox2D.Max.X;
            maxY = boundingBox2D.Max.Y;
            return true;
        }

        private class Member
        {
            public Member(ApertureSolarTarget target, double minX, double maxX, double maxY)
            {
                Target = target;
                MinX = minX;
                MaxX = maxX;
                MaxY = maxY;
            }

            public ApertureSolarTarget Target { get; }

            public double MinX { get; }

            public double MaxX { get; }

            public double MaxY { get; }
        }
    }
}
