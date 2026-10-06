// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// The per-DEVICE operation profile of one physical shading device spanning a GROUP of
        /// apertures.
        ///
        /// THE AGREEMENT GATE COMES FIRST. Every member profile must agree on the year, the
        /// sun-position shift and the whole control rule (solar threshold, temperature threshold,
        /// logic AND the wind limit), and must belong to exactly one member aperture of the group.
        /// Any disagreement is REFUSED — combining schedules built under different rules or
        /// timelines would produce a number nobody can interpret, so nothing is averaged, blended
        /// or silently merged.
        ///
        /// THE DEVICE GEOMETRY IS BUILT ONCE. The shared element set comes from the group frame
        /// (the same entry point the grouped optimisation uses), so one canopy spans the members
        /// instead of a copy being rebuilt around each window. Every member aperture is then
        /// measured against the SAME element Guids through the shared operation-profile core, with
        /// its own cell window and shared-cache offset.
        ///
        /// ENERGY AGGREGATION IS THE EXISTING GROUPED ACCOUNTING. The per-member controlled
        /// performances are handed to <see cref="GroupedShadingPerformance"/>, which performs the
        /// summed energies and the one shared material charge. The operation profile adds the
        /// device hour unions (demand / deployed / wind-retracted) and the summed canopy and
        /// valance figures — nothing here re-implements an aggregation the grouped performance
        /// already owns.
        /// </summary>
        /// <param name="group">The aperture group the device spans.</param>
        /// <param name="solarControlProfiles">One control profile per member aperture. Their ShadeOn hours are the deployment; the unions are computed from them.</param>
        /// <param name="solarVisibilityCache">Visibility with context only, spanning the model's shared cell space.</param>
        /// <param name="contextOccluders">Existing context (takes effect through solarVisibilityCache).</param>
        /// <param name="typology">The device. Only RetractableAwning builds shared elements over the group frame.</param>
        /// <param name="weatherData">Hourly weather on the same timeline.</param>
        /// <param name="message">Null on success; an actionable sentence otherwise.</param>
        /// <param name="cellIndexOffsets">Per-member first cell indices into the shared cell space, in member order. Null = use the offsets stored on the group.</param>
        public static GroupedShadingOperationProfile GroupedShadingOperationProfile(
            this ApertureShadingGroup group,
            IEnumerable<SolarControlProfile> solarControlProfiles,
            SolarVisibilityCache solarVisibilityCache,
            List<LinkedFace3D> contextOccluders,
            IShadingTypology typology,
            WeatherData weatherData,
            out string message,
            IEnumerable<int> cellIndexOffsets = null)
        {
            message = null;

            if (group == null || typology == null || solarVisibilityCache == null || weatherData == null || solarControlProfiles == null)
            {
                message = "The group, the device, the visibility cache, the weather and one control profile per member aperture are all required.";
                return null;
            }

            // One physical device is only meaningful as shared geometry for the awning family. The
            // same limitation as GroupedShadingDevice: a generic family has no group-level build.
            if (!(typology is RetractableAwning awning))
            {
                message = "A grouped shading operation is only supported for the RetractableAwning typology; a generic family has no group-level geometry to share.";
                return null;
            }

            List<ShadingElement> sharedElements = awning.ShadingElements(group.Plane, group.MinX, group.MaxX, group.MaxY);
            if (sharedElements == null)
            {
                message = "The shared awning geometry could not be built from the group frame.";
                return null;
            }

            List<SolarControlProfile> profiles = new List<SolarControlProfile>(solarControlProfiles);
            profiles.RemoveAll(x => x == null);

            // ---- the agreement gate. One schedule per member, no extras, one timeline, one rule.
            List<Guid> memberGuids = group.ApertureGuids;
            if (profiles.Count != memberGuids.Count)
            {
                message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The group has {0} member apertures but {1} control profiles were supplied. Supply exactly one control profile per member aperture.",
                    memberGuids.Count, profiles.Count);
                return null;
            }

            SolarControlProfile reference = profiles[0];
            HashSet<Guid> seenApertures = new HashSet<Guid>();
            foreach (SolarControlProfile profile in profiles)
            {
                if (!memberGuids.Contains(profile.ApertureGuid))
                {
                    message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Control profile {0} does not belong to any member aperture of this group. Refusing to merge a schedule from outside the device.",
                        profile.ApertureGuid);
                    return null;
                }

                if (!seenApertures.Add(profile.ApertureGuid))
                {
                    message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Two control profiles were supplied for aperture {0}. Supply exactly one control profile per member aperture.",
                        profile.ApertureGuid);
                    return null;
                }

                if (profile.Year != reference.Year)
                {
                    message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Member profiles disagree on the weather year ({0} vs {1}). Refusing to merge schedules from different years.",
                        profile.Year, reference.Year);
                    return null;
                }

                if (Math.Abs(profile.TimeShiftInMinutes - reference.TimeShiftInMinutes) > 1e-6)
                {
                    message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Member profiles disagree on the sun-position shift ({0} vs {1} minutes). Refusing to merge schedules from different timelines.",
                        profile.TimeShiftInMinutes, reference.TimeShiftInMinutes);
                    return null;
                }

                if (!EquivalentSettings(profile.Settings, reference.Settings, out string settingsDifference))
                {
                    message = "Member profiles disagree on the control rule (" + settingsDifference + "). Refusing to merge schedules built under different rules.";
                    return null;
                }
            }

            if (reference.Year != solarVisibilityCache.Year)
            {
                message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The control profiles are built on year {0} but the visibility cache on year {1}. The schedule and the energy accounting must sit on one timeline.",
                    reference.Year, solarVisibilityCache.Year);
                return null;
            }

            if (Math.Abs(reference.TimeShiftInMinutes - solarVisibilityCache.SunPositionShiftInMinutes) > 1e-6)
            {
                message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The control profiles use a sun-position shift of {0} minutes but the visibility cache uses {1}. The schedule and the energy accounting must sit on one timeline.",
                    reference.TimeShiftInMinutes, solarVisibilityCache.SunPositionShiftInMinutes);
                return null;
            }

            // ---- member offsets, from the parameter or the group itself.
            List<int> offsets = cellIndexOffsets == null ? group.CellIndexOffsets : new List<int>(cellIndexOffsets);
            if (offsets.Count != group.Targets.Count)
            {
                message = "One cell-index offset per member aperture is required.";
                return null;
            }

            // ---- measure every member against the SAME element set.
            Dictionary<Guid, SolarControlProfile> profileMap = new Dictionary<Guid, SolarControlProfile>();
            foreach (SolarControlProfile profile in profiles)
            {
                profileMap[profile.ApertureGuid] = profile;
            }

            double deviceArea = 0;
            foreach (ShadingElement element in sharedElements)
            {
                double area = element.Area;
                if (!double.IsNaN(area))
                {
                    deviceArea += area;
                }
            }

            List<ShadingOperationProfile> members = new List<ShadingOperationProfile>();
            List<ShadingPerformance> performances = new List<ShadingPerformance>();
            List<ApertureSolarTarget> targets = group.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                ApertureSolarTarget target = targets[i];
                int offset = i < offsets.Count ? offsets[i] : -1;
                if (target == null || offset < 0 || !profileMap.TryGetValue(target.ApertureGuid, out SolarControlProfile profile))
                {
                    message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Member aperture {0} cannot be measured: it has no control profile or no resolved offset into the shared cell space.",
                        target?.ApertureGuid ?? Guid.Empty);
                    return null;
                }

                double grossArea = target.GrossArea;
                double materialFraction = double.IsNaN(grossArea) || grossArea <= 0 ? double.NaN : deviceArea / grossArea;

                ShadingOperationProfile member = ShadingOperationProfileCore(
                    target, profile, solarVisibilityCache, sharedElements, typology.Name, materialFraction,
                    Parameters(typology), weatherData, offset,
                    out ShadingPerformance controlledPerformance, out ShadingPerformance _);

                if (member == null || controlledPerformance == null)
                {
                    message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Member aperture {0} could not be measured against the shared device: the profile, the cache and the target do not describe one calculation.",
                        target.ApertureGuid);
                    return null;
                }

                members.Add(member);
                performances.Add(controlledPerformance);
            }

            GroupedShadingPerformance groupedPerformance = new GroupedShadingPerformance(
                group.GroupGuid, group.PanelGuid, typology.Name, performances, deviceArea, group.TotalGrossArea);

            return new GroupedShadingOperationProfile(
                group.GroupGuid, group.PanelGuid, typology.Name,
                reference.Year, reference.TimeShiftInMinutes, reference.Settings,
                members, groupedPerformance);
        }

        /// <summary>The same, without the failure message.</summary>
        public static GroupedShadingOperationProfile GroupedShadingOperationProfile(
            this ApertureShadingGroup group,
            IEnumerable<SolarControlProfile> solarControlProfiles,
            SolarVisibilityCache solarVisibilityCache,
            List<LinkedFace3D> contextOccluders,
            IShadingTypology typology,
            WeatherData weatherData,
            IEnumerable<int> cellIndexOffsets = null)
        {
            return GroupedShadingOperationProfile(group, solarControlProfiles, solarVisibilityCache, contextOccluders, typology, weatherData, out string _, cellIndexOffsets);
        }

        /// <summary>
        /// The per-DEVICE operation profile of a GROUPED SHADING DEVICE — the Grasshopper hand-off
        /// SAMAnalytical.RationaliseAwningGroup.groupedShadingDevices → SAMAnalytical.ShadingOperation._shadingDevice.
        ///
        /// THE DEVICE DEFINES THE GROUP. The member scope, the member order and the group identity
        /// are taken from the device, never re-derived from whatever targets happen to be wired:
        ///
        ///   1. The supplied targets must BE the device's member apertures — the same set, matched
        ///      by aperture Guid, in any order. A missing, an additional or an unrelated aperture is
        ///      refused with the differing Guids named.
        ///   2. The group frame is re-established from the device's members through the one
        ///      deterministic grouping algorithm, under the device's OWN recorded grouping
        ///      criteria (product preset, side extension, maximum gap and head tolerance), and
        ///      the re-established group's deterministic Guid must EQUAL the device's GroupGuid.
        ///      A device whose group can no longer be re-established — the model changed — is
        ///      refused with the reason, never silently re-grouped into a different physical unit.
        ///
        /// Everything after the gate is the EXISTING grouped operation: one shared canopy built
        /// once from the group frame, one control profile per member (same year, sun-position
        /// shift and control rule — a disagreement is refused, never averaged), the device headline
        /// the union of the member schedules, and the energy summed through the existing grouped
        /// accounting. The device's typology is used as supplied; a non-awning typology keeps the
        /// existing refusal of the group-level path.
        /// </summary>
        /// <param name="groupedShadingDevice">One physical device spanning a deterministic aperture group, from the grouped awning analysis.</param>
        /// <param name="apertureSolarTargets">The targets as SUPPLIED (wired). Scope-checked against the device's member apertures; the measurement itself uses the context's own targets.</param>
        /// <param name="solarControlProfiles">One control profile per member aperture. Their ShadeOn hours ARE the deployment; the unions are computed from them.</param>
        /// <param name="context">The prepared solar context. Its weather, visibility cache, cell space and per-aperture offsets are used.</param>
        /// <param name="message">Null on success; an actionable sentence otherwise.</param>
        public static GroupedShadingOperationProfile GroupedShadingOperationProfile(
            this GroupedShadingDevice groupedShadingDevice,
            IEnumerable<ApertureSolarTarget> apertureSolarTargets,
            IEnumerable<SolarControlProfile> solarControlProfiles,
            ApertureSolarContext context,
            out string message)
        {
            message = null;

            if (groupedShadingDevice == null || context == null)
            {
                message = "The grouped shading device and the solar context are required.";
                return null;
            }

            if (groupedShadingDevice.Typology == null)
            {
                message = "The grouped shading device carries no typology.";
                return null;
            }

            List<Guid> memberGuids = groupedShadingDevice.ApertureGuids;
            if (memberGuids.Count == 0)
            {
                message = "The grouped shading device carries no member apertures.";
                return null;
            }

            // The device may not list the same aperture twice. The scope gate below is a SET
            // comparison, so a duplicated member would slip past it — and every later step (one
            // control profile per member, one measurement per member) assumes one entry per
            // window. Refuse the malformed device here rather than double-count it downstream.
            HashSet<Guid> distinctMembers = new HashSet<Guid>();
            foreach (Guid memberGuid in memberGuids)
            {
                if (!distinctMembers.Add(memberGuid))
                {
                    message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "The grouped shading device lists member aperture {0} twice. One physical device spans each of its windows exactly once.",
                        memberGuid);
                    return null;
                }
            }

            // ---- the scope gate: the wired targets must BE the device's member group. ----------
            List<Guid> supplied = new List<Guid>();
            foreach (ApertureSolarTarget target in apertureSolarTargets ?? new List<ApertureSolarTarget>())
            {
                if (target == null)
                {
                    continue;
                }

                if (supplied.Contains(target.ApertureGuid))
                {
                    message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Aperture {0} was supplied twice. One physical device is measured against each of its windows exactly once.",
                        target.ApertureGuid);
                    return null;
                }

                supplied.Add(target.ApertureGuid);
            }

            List<Guid> missing = memberGuids.FindAll(x => !supplied.Contains(x));
            List<Guid> additional = supplied.FindAll(x => !memberGuids.Contains(x));
            if (missing.Count != 0 || additional.Count != 0)
            {
                message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The supplied targets do not match the grouped device's member apertures. Not supplied but in the device: {0}. Supplied but not in the device: {1}. A grouped device is measured only against its own member group — wire exactly the windows SAMAnalytical.RationaliseAwningGroup grouped, in any order.",
                    GuidsText(missing), GuidsText(additional));
                return null;
            }

            // ---- every member must be analysable in this model. --------------------------------
            List<ApertureSolarTarget> members = new List<ApertureSolarTarget>();
            foreach (Guid guid in memberGuids)
            {
                ApertureSolarTarget member = context.Target(guid);
                if (member == null)
                {
                    message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Aperture {0} is not one of the sun-exposed external apertures of this model.", guid);
                    return null;
                }

                members.Add(member);
            }

            // ---- re-establish the device's own group, under the criteria it records; never
            // ---- guess a different one.
            double extensionBeyondJambs = groupedShadingDevice.Typology is RetractableAwning awning ? awning.GetParameter("ExtensionBeyondJambs") : 0.0;

            List<ApertureShadingGroup> groups = members.ApertureShadingGroups(
                groupedShadingDevice.Specification, extensionBeyondJambs,
                groupedShadingDevice.MaximumGap, groupedShadingDevice.HeadTolerance);
            if (groups == null || groups.Count != 1)
            {
                message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The member apertures of this grouped device do not form ONE group from the current geometry ({0} groups result). The model changed since the device was designed — or, for a device saved before grouping criteria were recorded, the group was formed under non-default tolerances. Re-run SAMAnalytical.RationaliseAwningGroup and wire its current groupedShadingDevices.",
                    groups?.Count ?? 0);
                return null;
            }

            ApertureShadingGroup group = groups[0];
            if (group.GroupGuid != groupedShadingDevice.GroupGuid)
            {
                message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The group re-established from the supplied apertures ({0}) is not the group this device was designed for ({1}). The model changed since the device was designed. Re-run SAMAnalytical.RationaliseAwningGroup and wire its current groupedShadingDevices.",
                    group.GroupGuid, groupedShadingDevice.GroupGuid);
                return null;
            }

            List<int> offsets = new List<int>();
            foreach (Guid guid in group.ApertureGuids)
            {
                offsets.Add(context.CellIndexOffset(guid));
            }

            return GroupedShadingOperationProfile(
                group, solarControlProfiles, context.SolarVisibilityCache, context.ContextOccluders,
                groupedShadingDevice.Typology, context.WeatherData, out message, offsets);
        }

        /// <summary>The same, without the failure message.</summary>
        public static GroupedShadingOperationProfile GroupedShadingOperationProfile(
            this GroupedShadingDevice groupedShadingDevice,
            IEnumerable<ApertureSolarTarget> apertureSolarTargets,
            IEnumerable<SolarControlProfile> solarControlProfiles,
            ApertureSolarContext context)
        {
            return GroupedShadingOperationProfile(groupedShadingDevice, apertureSolarTargets, solarControlProfiles, context, out string _);
        }

        private static string GuidsText(IEnumerable<Guid> guids)
        {
            List<string> parts = new List<string>();
            foreach (Guid guid in guids ?? new List<Guid>())
            {
                parts.Add(guid.ToString());
            }

            return "[" + string.Join(", ", parts) + "]";
        }

        /// <summary>
        /// Whether two control rules are the same rule, NaN-aware: an unused criterion (NaN) on one
        /// side matches only an unused criterion on the other. This is what makes the grouped
        /// agreement gate able to refuse a group whose members carry different wind limits.
        /// </summary>
        private static bool EquivalentSettings(SolarControlSettings first, SolarControlSettings second, out string difference)
        {
            difference = null;

            if (first == null || second == null)
            {
                if (first == null && second == null)
                {
                    return true;
                }

                difference = "one member has no rule";
                return false;
            }

            if (first.ControlLogic != second.ControlLogic)
            {
                difference = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} vs {1} logic", first.ControlLogic, second.ControlLogic);
                return false;
            }

            if (!Nearly(first.MinimumApertureIrradiance, second.MinimumApertureIrradiance))
            {
                difference = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "solar thresholds {0:0.##} vs {1:0.##} W/m²", first.MinimumApertureIrradiance, second.MinimumApertureIrradiance);
                return false;
            }

            if (!Nearly(first.MinimumOutdoorTemperature, second.MinimumOutdoorTemperature))
            {
                difference = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "temperature thresholds {0} vs {1} °C", FormatThreshold(first.MinimumOutdoorTemperature), FormatThreshold(second.MinimumOutdoorTemperature));
                return false;
            }

            if (!Nearly(first.MaximumWindSpeed, second.MaximumWindSpeed))
            {
                difference = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "wind limits {0} vs {1} m/s", FormatThreshold(first.MaximumWindSpeed), FormatThreshold(second.MaximumWindSpeed));
                return false;
            }

            return true;
        }

        private static string FormatThreshold(double value)
        {
            return double.IsNaN(value) ? "none" : value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool Nearly(double first, double second)
        {
            if (double.IsNaN(first))
            {
                return double.IsNaN(second);
            }

            return !double.IsNaN(second) && Math.Abs(first - second) <= 1e-9;
        }
    }
}
