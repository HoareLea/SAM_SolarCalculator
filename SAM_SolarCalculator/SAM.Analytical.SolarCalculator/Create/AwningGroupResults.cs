// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// The grouped awning analysis end-to-end: ONE solar context for the selected apertures,
        /// deterministic grouping, and a product-constrained awning search per group.
        ///
        /// The expensive model context is built ONCE and reused by every group and every member
        /// aperture — the visibility cache spans the selection and each member keeps its correct
        /// cell offset. This is the production entry point behind the
        /// SAMAnalytical.RationaliseAwningGroup component.
        ///
        /// FIXED-VALUE VALIDATION. A fixed projection must be an allowed preset projection AND
        /// satisfy the product minimum-width rule for every group's width; a fixed tilt must lie in
        /// the product range; a fixed valance must be 0 or the preset standard depth. Invalid fixed
        /// values are REFUSED with an actionable message — they are requests, not search bounds.
        /// </summary>
        /// <param name="analyticalModel">The model.</param>
        /// <param name="apertureGuids">The apertures one or more awnings may span. Empty selection returns null.</param>
        /// <param name="year">Requested analysis year; resolved against the weather.</param>
        /// <param name="message">Null on success; an actionable sentence otherwise.</param>
        /// <param name="reusedPreviousCalculation">True when no ray casting was needed to set up.</param>
        /// <param name="weatherData">Weather. Null = the weather attached to the model.</param>
        /// <param name="desirabilityStrategy">Explicit weighting. Wins over the periods when supplied.</param>
        /// <param name="unwantedPeriod">Hours whose solar should be blocked. Null with a null strategy = the default brief.</param>
        /// <param name="wantedPeriod">Hours whose solar should be preserved.</param>
        /// <param name="specification">Product preset. Null = Dakar.</param>
        /// <param name="projection">Fixed projection [m], or null to search the valid preset projections.</param>
        /// <param name="tiltDegrees">Fixed deployment tilt [°], or null to select it by analysis.</param>
        /// <param name="riseAboveHead">Fixed rise above the head line [m].</param>
        /// <param name="extensionBeyondJambs">Fixed symmetric side extension [m].</param>
        /// <param name="valanceDepth">Fixed valance depth [m] (0 or the preset standard); null enables valance optimisation.</param>
        /// <param name="maximumGap">Largest horizontal gap between consecutive apertures that still shares one awning [m].</param>
        /// <param name="headTolerance">Largest head-level spread within one group [m].</param>
        /// <param name="gridSize">Aperture analysis-grid size, m.</param>
        /// <param name="sunAngleStep">Angular resolution used to group similar sun positions, degrees.</param>
        /// <param name="recalculate">Force a rebuild even when a previous calculation could be reused.</param>
        /// <param name="objective">The objective. Null for the Stage 9 default.</param>
        /// <param name="maximumEvaluations">Hard budget on distinct candidate evaluations per group.</param>
        public static List<GroupedAwningResult> AwningGroupResults(
            this AnalyticalModel analyticalModel,
            IEnumerable<Guid> apertureGuids,
            int year,
            out string message,
            out bool reusedPreviousCalculation,
            WeatherData weatherData = null,
            IDesirabilityStrategy desirabilityStrategy = null,
            AnalysisPeriod unwantedPeriod = null,
            AnalysisPeriod wantedPeriod = null,
            AwningSpecification specification = null,
            double? projection = null,
            double? tiltDegrees = null,
            double riseAboveHead = 0.0,
            double extensionBeyondJambs = 0.15,
            double? valanceDepth = 0.0,
            double maximumGap = GroupedShadingDevice.DefaultMaximumGap,
            double headTolerance = GroupedShadingDevice.DefaultHeadTolerance,
            double gridSize = 0.5,
            double sunAngleStep = 2.0,
            bool recalculate = false,
            ShadingObjective objective = null,
            int maximumEvaluations = 400)
        {
            return AwningGroupResults(
                analyticalModel, apertureGuids, year, out message, out reusedPreviousCalculation,
                weatherData, desirabilityStrategy, unwantedPeriod, wantedPeriod, specification,
                projection, tiltDegrees, riseAboveHead, extensionBeyondJambs, valanceDepth,
                maximumGap, headTolerance, gridSize, sunAngleStep, recalculate, objective, maximumEvaluations,
                mountingOffset: 0.0);
        }

        /// <summary>
        /// The mounting-offset overload. The original twenty-two-parameter signature above is retained
        /// exactly for binary compatibility and forwards here with MountingOffset = 0.0.
        /// </summary>
        /// <param name="analyticalModel">The model.</param>
        /// <param name="apertureGuids">The apertures one or more awnings may span. Empty selection returns null.</param>
        /// <param name="year">Requested analysis year; resolved against the weather.</param>
        /// <param name="message">Null on success; an actionable sentence otherwise.</param>
        /// <param name="reusedPreviousCalculation">True when no ray casting was needed to set up.</param>
        /// <param name="weatherData">Weather. Null = the weather attached to the model.</param>
        /// <param name="desirabilityStrategy">Explicit weighting. Wins over the periods when supplied.</param>
        /// <param name="unwantedPeriod">Hours whose solar should be blocked. Null with a null strategy = the default brief.</param>
        /// <param name="wantedPeriod">Hours whose solar should be preserved.</param>
        /// <param name="specification">Product preset. Null = Dakar.</param>
        /// <param name="projection">Fixed projection [m], or null to search the valid preset projections.</param>
        /// <param name="tiltDegrees">Fixed deployment tilt [°], or null to select it by analysis.</param>
        /// <param name="riseAboveHead">Fixed rise above the head line [m].</param>
        /// <param name="extensionBeyondJambs">Fixed symmetric side extension [m].</param>
        /// <param name="valanceDepth">Fixed valance depth [m] (0 or the preset standard); null enables valance optimisation.</param>
        /// <param name="maximumGap">Largest horizontal gap between consecutive apertures that still shares one awning [m].</param>
        /// <param name="headTolerance">Largest head-level spread within one group [m].</param>
        /// <param name="gridSize">Aperture analysis-grid size, m.</param>
        /// <param name="sunAngleStep">Angular resolution used to group similar sun positions, degrees.</param>
        /// <param name="recalculate">Force a rebuild even when a previous calculation could be reused.</param>
        /// <param name="objective">The objective. Null for the Stage 9 default.</param>
        /// <param name="maximumEvaluations">Hard budget on distinct candidate evaluations per group.</param>
        /// <param name="mountingOffset">Fixed horizontal outward distance from the aperture plane to the awning mounting line [m].</param>
        public static List<GroupedAwningResult> AwningGroupResults(
            this AnalyticalModel analyticalModel,
            IEnumerable<Guid> apertureGuids,
            int year,
            out string message,
            out bool reusedPreviousCalculation,
            WeatherData weatherData,
            IDesirabilityStrategy desirabilityStrategy,
            AnalysisPeriod unwantedPeriod,
            AnalysisPeriod wantedPeriod,
            AwningSpecification specification,
            double? projection,
            double? tiltDegrees,
            double riseAboveHead,
            double extensionBeyondJambs,
            double? valanceDepth,
            double maximumGap,
            double headTolerance,
            double gridSize,
            double sunAngleStep,
            bool recalculate,
            ShadingObjective objective,
            int maximumEvaluations,
            double mountingOffset)
        {
            message = null;
            reusedPreviousCalculation = false;
            specification = specification ?? AwningSpecification.Dakar;

            List<Guid> requested = new List<Guid>(apertureGuids ?? new List<Guid>());
            if (requested.Count == 0)
            {
                message = "No apertures were selected. Connect at least one aperture solar target.";
                return null;
            }

            // Static fixed-value validation first: these are properties of the request alone, so
            // they are settled before a solar context is paid for. The SAME check runs again inside
            // Optimise.RetractableAwningGroup — that method is public in its own right and must not
            // depend on this one having been called — and both refuse in the same words.
            if (!Optimise.ValidAwningInputs(specification, projection, tiltDegrees, riseAboveHead, extensionBeyondJambs, valanceDepth, mountingOffset, out message))
            {
                return null;
            }

            // ONE solar context for the whole selection.
            ApertureSolarContext context = ApertureSolarContext(analyticalModel, year, weatherData, requested, gridSize, sunAngleStep, recalculate);
            if (context == null)
            {
                message = ApertureSolarContextFailureReason(analyticalModel, requested, gridSize, weatherData)
                    ?? "The solar calculation could not be set up for this model.";
                return null;
            }

            reusedPreviousCalculation = context.ReusedPreviousCalculation;

            // Every requested aperture must actually be analysable: a silently missing member would
            // change the grouping and report a partial answer as a complete one.
            List<Guid> missing = new List<Guid>();
            foreach (Guid guid in requested)
            {
                if (context.Target(guid) == null)
                {
                    missing.Add(guid);
                }
            }

            if (missing.Count != 0)
            {
                message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} of the {1} selected apertures cannot be analysed (only apertures on sun-exposed external panels are targets): {2}",
                    missing.Count, requested.Count, string.Join(", ", missing));
                return null;
            }

            // The brief, resolved exactly as the single-aperture setup resolves it.
            IDesirabilityStrategy strategy = desirabilityStrategy;
            if (strategy == null)
            {
                strategy = unwantedPeriod == null && wantedPeriod == null
                    ? DefaultDesirabilityStrategy(context.Year, context.Location)
                    : new SeasonalDesirability(ReRoot(unwantedPeriod, context.Year), ReRoot(wantedPeriod, context.Year));
            }

            List<ApertureShadingGroup> groups = context.Targets.ApertureShadingGroups(specification, extensionBeyondJambs, maximumGap, headTolerance);

            List<GroupedAwningResult> result = new List<GroupedAwningResult>();
            foreach (ApertureShadingGroup group in groups)
            {
                // Fixed projection must satisfy the product minimum-width rule for THIS group.
                double width = group.Width + 2.0 * extensionBeyondJambs;
                if (projection.HasValue)
                {
                    if (!specification.IsValid(width, projection.Value, tiltDegrees ?? specification.MinimumTiltDegrees, out string widthMessage))
                    {
                        message = widthMessage;
                        return null;
                    }
                }

                List<ApertureDesirability> desirabilities = new List<ApertureDesirability>();
                List<int> offsets = new List<int>();
                foreach (ApertureSolarTarget target in group.Targets)
                {
                    desirabilities.Add(ApertureDesirability(target, context.SolarVisibilityCache, strategy, context.WeatherData));
                    offsets.Add(context.CellIndexOffset(target.ApertureGuid));
                }

                group.SetCellIndexOffsets(offsets);

                bool? hostBoundFits = HostBoundFits(analyticalModel, group, extensionBeyondJambs);

                GroupedAwningResult groupResult = Optimise.RetractableAwningGroup(
                    group, context.SolarVisibilityCache, desirabilities, context.ContextOccluders,
                    objective, specification, projection, tiltDegrees, riseAboveHead, extensionBeyondJambs,
                    valanceDepth, maximumEvaluations: maximumEvaluations, mountingOffset: mountingOffset,
                    maximumGap: maximumGap, headTolerance: headTolerance);

                if (groupResult == null)
                {
                    message = "The awning search could not be run for this group: the apertures, the solar calculation and the candidate geometry do not describe the same analysis points.";
                    return null;
                }

                AppendHostBoundWarning(groupResult, hostBoundFits, extensionBeyondJambs);
                result.Add(groupResult);
            }

            return result;
        }

        /// <summary>
        /// Whether the awning's back edge (aperture envelope plus both side extensions) fits on the
        /// host panel horizontally. Null when the panel geometry is not available — reported as a
        /// warning on the result, never as a pretended pass.
        /// </summary>
        private static bool? HostBoundFits(AnalyticalModel analyticalModel, ApertureShadingGroup group, double extensionBeyondJambs)
        {
            Panel panel = analyticalModel?.AdjacencyCluster?.GetObject<Panel>(group.PanelGuid);
            Face3D face3D = panel?.GetFace3D();
            Plane plane = group.Plane;
            if (face3D == null || plane == null)
            {
                return null;
            }

            Geometry.Planar.BoundingBox2D boundingBox2D = plane.Convert(face3D)?.GetBoundingBox();
            if (boundingBox2D == null)
            {
                return null;
            }

            double tolerance = 1e-6;
            return boundingBox2D.Min.X <= group.MinX - extensionBeyondJambs + tolerance
                && boundingBox2D.Max.X >= group.MaxX + extensionBeyondJambs - tolerance;
        }

        /// <summary>
        /// The host-bound result, attached to the group answer: a verified pass is silent, an
        /// overrun or an unverifiable fit is a warning rather than a pretended check.
        /// </summary>
        private static void AppendHostBoundWarning(GroupedAwningResult groupResult, bool? hostBoundFits, double extensionBeyondJambs)
        {
            if (groupResult == null || extensionBeyondJambs <= 0 || hostBoundFits == true)
            {
                return;
            }

            groupResult.AddHostBoundWarning(hostBoundFits == false
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The awning extends {0:0.##} m beyond the outermost jambs on each side, and the back edge overruns the host wall. Reduce _extensionBeyondJambs_ or split the apertures.",
                    extensionBeyondJambs)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The awning extends {0:0.##} m beyond the outermost jambs on each side; the host wall bounds could not be verified from the model geometry. Check the wall length before specifying.",
                    extensionBeyondJambs));
        }
    }
}
