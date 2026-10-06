// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Weather;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Verifies one shading SCHEME: builds ONE solar context over the scheme's aperture set,
        /// resolves the brief once, builds the scheme's COMPLETE element set once, and measures
        /// every member aperture against that complete set — not against its own device alone.
        ///
        /// THE COMPLETE-SET MEASUREMENT IS THE POINT. The shared-elements overload of
        /// Create.ShadingPerformance (the one the grouped awning already uses) takes an explicit
        /// element list; passing the WHOLE scheme's elements to every member means an overhang over
        /// window A that overhangs window B is credited on B. A sum of independent per-window
        /// verifications cannot see that, and the difference is the cross-shading finding the
        /// comparison exists to surface. No new physics is written — the same accounting runs over a
        /// larger candidate set.
        ///
        /// Never silently verifies a correct scheme against the wrong aperture set: any scope
        /// mismatch is refused with a message naming the differing GUIDs.
        /// </summary>
        /// <param name="analyticalModel">The model.</param>
        /// <param name="scheme">The complete shading proposal.</param>
        /// <param name="targets">The scope targets — must equal the scheme's aperture set exactly.</param>
        /// <param name="year">Requested analysis year; resolved against the weather.</param>
        /// <param name="message">Null on success; an actionable sentence otherwise.</param>
        /// <param name="weatherData">Weather. Null = the weather attached to the model.</param>
        /// <param name="desirabilityStrategy">Explicit weighting. Wins over the periods when supplied.</param>
        /// <param name="unwantedPeriod">Hours whose solar should be blocked. Null with a null strategy = the default brief.</param>
        /// <param name="wantedPeriod">Hours whose solar should be preserved.</param>
        /// <param name="gridSize">Aperture analysis-grid size, m.</param>
        /// <param name="sunAngleStep">Angular resolution used to group similar sun positions, degrees.</param>
        /// <param name="recalculate">Force a rebuild even when a previous calculation could be reused.</param>
        public static SAM.Analytical.SolarCalculator.VerifiedShadingSchemeResult VerifiedShadingSchemeResult(
            this AnalyticalModel analyticalModel,
            ShadingScheme scheme,
            IEnumerable<ApertureSolarTarget> targets,
            int year,
            out string message,
            WeatherData weatherData = null,
            IDesirabilityStrategy desirabilityStrategy = null,
            AnalysisPeriod unwantedPeriod = null,
            AnalysisPeriod wantedPeriod = null,
            double gridSize = 0.5,
            double sunAngleStep = 2.0,
            bool recalculate = false)
        {
            message = null;

            List<ApertureSolarTarget> targetList = Materialise(targets);
            List<Guid> scope = SchemeScope(targetList, scheme, out string scopeMessage);
            if (scopeMessage != null)
            {
                message = scopeMessage;
                return null;
            }

            if (Query.GridSizeValidity(gridSize, out string gridSizeMessage) != GridSizeValidity.Valid)
            {
                message = gridSizeMessage;
                return null;
            }

            if (sunAngleStep <= 0 || double.IsNaN(sunAngleStep))
            {
                message = "The sun-angle step must be greater than zero.";
                return null;
            }

            ApertureSolarContext context = ApertureSolarContext(analyticalModel, year, weatherData, scope, gridSize, sunAngleStep, recalculate);
            if (context == null)
            {
                message = ApertureSolarContextFailureReason(analyticalModel, scope, gridSize, weatherData)
                    ?? "The solar calculation could not be set up for this model.";
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

            List<ApertureDesirability> desirabilities = new List<ApertureDesirability>();
            foreach (ApertureSolarTarget target in context.Targets)
            {
                ApertureDesirability desirability = ApertureDesirability(target, context.SolarVisibilityCache, strategy, context.WeatherData);
                if (desirability == null)
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The desirability weighting could not be built for aperture {0}.", target.ApertureGuid);
                    return null;
                }

                desirabilities.Add(desirability);
            }

            ShadingAnalysisSignature signature = context.ShadingAnalysisSignature(strategy);
            return VerifyCore(scheme, context, targetList, desirabilities, signature, out message);
        }

        /// <summary>
        /// The prepared-context overload: verifies against a context that is already built, so a
        /// comparison verifies every scheme against the SAME visibility cache, cell ordering and
        /// brief without paying for the solar context once per scheme. A distinct overload — never
        /// an optional parameter appended to the model-level method (§14.1).
        /// </summary>
        public static SAM.Analytical.SolarCalculator.VerifiedShadingSchemeResult VerifiedShadingSchemeResult(
            this ShadingScheme scheme,
            ApertureSolarContext context,
            IEnumerable<ApertureSolarTarget> targets,
            IEnumerable<ApertureDesirability> desirabilities,
            ShadingAnalysisSignature signature,
            out string message)
        {
            message = null;
            if (scheme == null || context == null)
            {
                message = "A scheme and a solar context are required.";
                return null;
            }

            List<ApertureSolarTarget> targetList = Materialise(targets);
            List<Guid> scope = SchemeScope(targetList, scheme, out string scopeMessage);
            if (scopeMessage != null)
            {
                message = scopeMessage;
                return null;
            }

            foreach (Guid apertureGuid in scope)
            {
                if (context.Target(apertureGuid) == null)
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "Aperture {0} is not one of the analysable apertures of this model. Only apertures on sun-exposed external panels are analysed; an aperture on a panel shared by two spaces is internal and is never a target.",
                        apertureGuid);
                    return null;
                }
            }

            List<ApertureDesirability> desirabilityList = new List<ApertureDesirability>();
            foreach (ApertureSolarTarget target in context.Targets)
            {
                ApertureDesirability desirability = null;
                if (desirabilities != null)
                {
                    foreach (ApertureDesirability candidate in desirabilities)
                    {
                        if (candidate != null && candidate.ApertureGuid == target.ApertureGuid)
                        {
                            desirability = candidate;
                            break;
                        }
                    }
                }

                if (desirability == null)
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "No desirability weighting was supplied for aperture {0}.", target.ApertureGuid);
                    return null;
                }

                desirabilityList.Add(desirability);
            }

            return VerifyCore(scheme, context, targetList, desirabilityList, signature, out message);
        }

        /// <summary>
        /// The shared verification core. Measures every member aperture against the COMPLETE scheme
        /// element set through the existing shared-elements accounting — no new physics.
        /// </summary>
        private static SAM.Analytical.SolarCalculator.VerifiedShadingSchemeResult VerifyCore(
            ShadingScheme scheme,
            ApertureSolarContext context,
            List<ApertureSolarTarget> targets,
            List<ApertureDesirability> desirabilities,
            ShadingAnalysisSignature signature,
            out string message)
        {
            message = null;

            // THE COMPLETE ELEMENT SET, built once, re-identified with scheme-scoped GUIDs.
            List<ShadingElement> elements = scheme.SchemeElements(targets);

            // The placement roll-up needs element ownership, whatever the member loop finds.
            Dictionary<Guid, Guid> elementOwners = scheme.ElementOwners(targets);

            List<string> warnings = new List<string>(scheme.Warnings);
            ShadingDesignStatus status = scheme.Status;
            bool notEvaluated = false;

            // The member loop runs over the SCHEME's scope — never over the whole context. A context
            // shared by a comparison may cover exactly these apertures (the normal case), but the
            // contract is "verify a correct scheme against its own scope", and a context covering
            // more must not silently measure extra apertures.
            List<ApertureSolarTarget> members = new List<ApertureSolarTarget>();
            foreach (Guid apertureGuid in scheme.ApertureGuids)
            {
                ApertureSolarTarget target = context.Target(apertureGuid);
                if (target == null)
                {
                    status = Worse(status, ShadingDesignStatus.NotEvaluated);
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "Aperture {0} is not part of the solar context, so the scheme could not be measured against it.", apertureGuid));
                    notEvaluated = true;
                    continue;
                }

                members.Add(target);
            }

            List<ShadingPerformance> performances = new List<ShadingPerformance>();
            foreach (ApertureSolarTarget target in members)
            {
                ApertureDesirability desirability = desirabilities.Find(x => x != null && x.ApertureGuid == target.ApertureGuid);
                int cellIndexOffset = context.CellIndexOffset(target.ApertureGuid);
                if (desirability == null || cellIndexOffset < 0)
                {
                    status = Worse(status, ShadingDesignStatus.NotEvaluated);
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "Aperture {0} could not be prepared for verification.", target.ApertureGuid));
                    notEvaluated = true;
                    continue;
                }

                // The WHOLE scheme, not this member's own device: every member aperture sees every
                // scheme element as a candidate occluder, so cross-shading is measured, not assumed.
                ShadingPerformance performance = ShadingPerformance(
                    target, context.SolarVisibilityCache, desirability, context.ContextOccluders,
                    elements, scheme.Name, cellIndexOffset);

                if (performance == null)
                {
                    status = Worse(status, ShadingDesignStatus.NotEvaluated);
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "The scheme could not be measured on aperture {0}: the aperture, the solar calculation and the scheme geometry do not describe the same analysis points.", target.ApertureGuid));
                    notEvaluated = true;
                    continue;
                }

                if (Math.Abs(performance.UnattributedInterceptedEnergy) > 1e-9)
                {
                    // Trap 2: the residual stays a fault signal. A base-admitted ray stopped by a
                    // scheme element is always attributed, so a non-zero residual means the baseline
                    // and the traced geometry disagree — report, never smooth.
                    status = Worse(status, ShadingDesignStatus.Warning);
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0:0.###} kWh was stopped on aperture {1} by something that is not part of the scheme. The unshaded baseline and the traced geometry disagree; treat the totals with care.",
                        performance.UnattributedInterceptedEnergy, target.ApertureGuid));
                }

                performances.Add(performance);
            }

            if (notEvaluated && performances.Count == 0)
            {
                message = "The scheme could not be measured on any aperture of the scope.";
                return null;
            }

            // Material: the sum of the DISTINCT scheme element areas (scheme-scoped GUIDs are
            // already one entry per physical element), never the per-member material fractions,
            // which the shared-elements overload computes as (whole scheme / one window) and are
            // meaningless at scheme level.
            double physicalDeviceArea = 0;
            bool materialAvailable = true;
            foreach (ShadingElement element in elements)
            {
                double area = element.Area;
                if (double.IsNaN(area) || double.IsInfinity(area) || area < 0)
                {
                    materialAvailable = false;
                    continue;
                }

                physicalDeviceArea += area;
            }

            double totalGrossArea = 0;
            foreach (ApertureSolarTarget target in targets)
            {
                double grossArea = target.GrossArea;
                if (double.IsNaN(grossArea) || grossArea <= 0)
                {
                    materialAvailable = false;
                }
                else
                {
                    totalGrossArea += grossArea;
                }
            }

            ShadingSchemePerformance schemePerformance = new ShadingSchemePerformance(performances, physicalDeviceArea, materialAvailable, totalGrossArea, elementOwners);

            double verifiedScore = scheme.DesignObjective == null ? double.NaN : scheme.DesignObjective.Score(schemePerformance);

            List<LinkedFace3D> elementFaces = new List<LinkedFace3D>();
            foreach (ShadingElement element in elements)
            {
                LinkedFace3D linkedFace3D = element.LinkedFace3D;
                if (linkedFace3D != null)
                {
                    elementFaces.Add(linkedFace3D);
                }
            }

            string schemeGeometryHash = Geometry.SolarCalculator.Query.GeometryHash(elementFaces, Core.Tolerance.Distance);

            return new SAM.Analytical.SolarCalculator.VerifiedShadingSchemeResult(
                scheme, schemePerformance, signature, status, warnings,
                SAM.Analytical.SolarCalculator.VerifiedShadingSchemeResult.Summary(scheme, schemePerformance),
                schemeGeometryHash, verifiedScore, context.ReusedPreviousCalculation);
        }

        private static List<ApertureSolarTarget> Materialise(IEnumerable<ApertureSolarTarget> targets)
        {
            List<ApertureSolarTarget> result = new List<ApertureSolarTarget>();
            if (targets == null)
            {
                return result;
            }

            foreach (ApertureSolarTarget target in targets)
            {
                if (target != null)
                {
                    result.Add(target);
                }
            }

            return result;
        }

        /// <summary>
        /// The scope checks: the supplied target set must equal the scheme aperture set in BOTH
        /// directions, contain no duplicate, cover every device's aperture and every grouped
        /// device's members. Anything else is refused with a message naming the offending GUID.
        /// </summary>
        private static List<Guid> SchemeScope(List<ApertureSolarTarget> targets, ShadingScheme scheme, out string message)
        {
            message = null;
            if (scheme == null)
            {
                message = "No shading scheme was supplied.";
                return null;
            }

            List<Guid> scope = new List<Guid>();
            foreach (ApertureSolarTarget target in targets)
            {
                if (!scope.Contains(target.ApertureGuid))
                {
                    scope.Add(target.ApertureGuid);
                }
            }
            scope.Sort();

            if (scope.Count != targets.Count)
            {
                message = "A target was supplied twice. A duplicated aperture would double-count its energy in every scheme equally, which hides rather than cancels the error.";
                return null;
            }

            List<Guid> schemeGuids = new List<Guid>(scheme.ApertureGuids);
            schemeGuids.Sort();
            scope.Sort();

            if (scope.Count != schemeGuids.Count)
            {
                List<Guid> missing = schemeGuids.FindAll(x => !scope.Contains(x));
                List<Guid> extra = scope.FindAll(x => !schemeGuids.Contains(x));
                message = string.Format(CultureInfo.InvariantCulture,
                    "The supplied targets ({0} apertures) do not match the scheme's aperture set ({1} apertures). In the targets but not the scheme: {2}. In the scheme but not the targets: {3}. A scheme is verified only against its own scope — never silently against a different one.",
                    scope.Count, schemeGuids.Count, GuidsText(extra), GuidsText(missing));
                return null;
            }

            for (int i = 0; i < scope.Count; i++)
            {
                if (scope[i] != schemeGuids[i])
                {
                    List<Guid> missing = schemeGuids.FindAll(x => !scope.Contains(x));
                    List<Guid> extra = scope.FindAll(x => !schemeGuids.Contains(x));
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The supplied targets do not match the scheme's aperture set. In the targets but not the scheme: {0}. In the scheme but not the targets: {1}. A scheme is verified only against its own scope.",
                        GuidsText(extra), GuidsText(missing));
                    return null;
                }
            }

            foreach (ShadingDevice device in scheme.Devices)
            {
                if (!scope.Contains(device.ApertureGuid))
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The scheme carries a device for aperture {0}, which is outside the supplied scope. A scheme is verified only against its own scope.",
                        device.ApertureGuid);
                    return null;
                }
            }

            foreach (GroupedShadingDevice device in scheme.GroupedDevices)
            {
                foreach (Guid member in device.ApertureGuids)
                {
                    if (!scope.Contains(member))
                    {
                        message = string.Format(CultureInfo.InvariantCulture,
                            "The scheme carries a grouped device covering aperture {0}, which is outside the supplied scope. A scheme is verified only against its own scope.",
                            member);
                        return null;
                    }
                }
            }

            return scope;
        }

        private static string GuidsText(List<Guid> guids)
        {
            List<string> parts = new List<string>();
            foreach (Guid guid in guids ?? new List<Guid>())
            {
                parts.Add(guid.ToString());
            }

            return "[" + string.Join(", ", parts) + "]";
        }
    }
}
