// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// The state of one aperture's shading answer, as a value rather than as a balloon on a node.
    ///
    /// Runtime warnings are the right tool for ONE window and the wrong one for ten: the messages
    /// pile up on a single component and stop saying which aperture each belongs to. The runtime
    /// messages stay — they are what makes a problem impossible to miss — but the same information
    /// is also available per aperture, so a ten-window run can be read as a table.
    /// </summary>
    public enum ShadingDesignStatus
    {
        Undefined,

        /// <summary>A device is recommended and nothing needs checking.</summary>
        Ok,

        /// <summary>Measured, and building nothing wins. A successful answer, not a failure.</summary>
        NoShading,

        /// <summary>A device is recommended, but something about it needs checking — usually its spacing against the analysis grid.</summary>
        Warning,

        /// <summary>Nothing could be measured. A fault, never to be read as "no shading needed".</summary>
        NotEvaluated,
    }

    public static partial class Query
    {
        /// <summary>The short status word shown in a batch table.</summary>
        public static string StatusText(this ShadingDesignStatus status)
        {
            switch (status)
            {
                case ShadingDesignStatus.Ok: return "OK";
                case ShadingDesignStatus.NoShading: return "NO SHADE";
                case ShadingDesignStatus.Warning: return "WARNING";
                case ShadingDesignStatus.NotEvaluated: return "NOT EVALUATED";
                default: return "UNDEFINED";
            }
        }

        /// <summary>
        /// The status of an optimised result, given whatever the resolution check said about the
        /// winning device.
        ///
        /// The order matters. "Could not be evaluated" outranks everything, because every other
        /// reading is derived from numbers that do not exist. "No shading" outranks the resolution
        /// warning, because a device that is not being recommended cannot be too finely spaced.
        /// </summary>
        public static ShadingDesignStatus DesignStatus(this OptimisedShadingResult optimisedShadingResult, ShadingResolutionState resolutionState = ShadingResolutionState.Resolved)
        {
            if (optimisedShadingResult == null || optimisedShadingResult.Termination == ShadingOptimisationTermination.EvaluationFailed)
            {
                return ShadingDesignStatus.NotEvaluated;
            }

            if (optimisedShadingResult.RecommendsNoShading)
            {
                return ShadingDesignStatus.NoShading;
            }

            return resolutionState == ShadingResolutionState.Resolved
                ? ShadingDesignStatus.Ok
                : ShadingDesignStatus.Warning;
        }

        /// <summary>
        /// The engineering story of a rationalised design in one line: what to build, how big, what
        /// it achieves and what it costs in wanted solar.
        ///
        /// Deliberately NOT led by the objective score. The score is a single number that mixes
        /// benefit, harm and material through weightings the reader may not have chosen, and two
        /// designs a fraction of a percent apart in score can differ completely in what they
        /// physically do. The four numbers an engineer defends a design with are the two percentages
        /// and the two energies, so those are what the line says.
        /// </summary>
        /// <param name="optimisedShadingResult">The winning result.</param>
        /// <param name="azimuth">Aperture azimuth, degrees. NaN to leave it out.</param>
        public static string DesignSummary(this OptimisedShadingResult optimisedShadingResult, double azimuth = double.NaN)
        {
            if (optimisedShadingResult == null)
            {
                return "No result.";
            }

            StringBuilder stringBuilder = new StringBuilder();
            AppendAzimuth(stringBuilder, azimuth);

            if (optimisedShadingResult.Termination == ShadingOptimisationTermination.EvaluationFailed)
            {
                stringBuilder.Append("Not evaluated | no candidate could be measured on this window");
                return stringBuilder.ToString();
            }

            if (optimisedShadingResult.RecommendsNoShading)
            {
                stringBuilder.Append("No shading | nothing beats leaving this window unshaded");
                if (!double.IsNaN(optimisedShadingResult.ObjectiveScore))
                {
                    stringBuilder.AppendFormat(CultureInfo.InvariantCulture, " | best candidate {0} scored {1:0.#} kWh", optimisedShadingResult.TypologyName, optimisedShadingResult.ObjectiveScore);
                }

                return stringBuilder.ToString();
            }

            stringBuilder.Append(optimisedShadingResult.TypologyName);

            string parameters = ParameterText(optimisedShadingResult.ParameterNames, optimisedShadingResult.GetParameter);
            if (parameters != null)
            {
                stringBuilder.Append(" | ");
                stringBuilder.Append(parameters);
            }

            AppendPercentage(stringBuilder, optimisedShadingResult.UnwantedSolarBlocked, "unwanted blocked");
            AppendPercentage(stringBuilder, optimisedShadingResult.WantedSolarRetained, "wanted retained");

            // Named for the physical quantity, not for its role in the objective. "46 kWh benefit"
            // reads as a saving; it is the unwanted beam this device intercepts, which is a
            // different claim and the only one the measurement supports.
            AppendEnergy(stringBuilder, optimisedShadingResult.UnwantedSolarIntercepted, "unwanted solar intercepted");
            AppendEnergy(stringBuilder, optimisedShadingResult.WantedSolarBlocked, "wanted solar blocked");

            return stringBuilder.ToString();
        }

        /// <summary>
        /// The measured answer in one line: the unshaded baseline, what the device stops, and the
        /// two percentages the design is judged on. The null device reads honestly rather than as a
        /// failure — 0 % blocked and 100 % retained is exactly what building nothing achieves.
        /// </summary>
        /// <param name="shadingPerformance">The verified performance.</param>
        /// <param name="azimuth">Aperture azimuth, degrees. NaN to leave it out.</param>
        public static string VerificationSummary(this ShadingPerformance shadingPerformance, double azimuth = double.NaN)
        {
            if (shadingPerformance == null)
            {
                return "Not verified.";
            }

            StringBuilder stringBuilder = new StringBuilder();
            AppendAzimuth(stringBuilder, azimuth);

            stringBuilder.Append(shadingPerformance.TypologyName == "NoShading" ? "No shading" : shadingPerformance.TypologyName);

            AppendEnergy(stringBuilder, shadingPerformance.AdmittedDirectEnergy, "baseline direct solar");
            AppendEnergy(stringBuilder, shadingPerformance.DirectSolarIntercepted, "intercepted");
            AppendPercentage(stringBuilder, shadingPerformance.UnwantedSolarBlocked, "unwanted blocked");
            AppendPercentage(stringBuilder, shadingPerformance.WantedSolarRetained, "wanted retained");
            AppendPercentage(stringBuilder, shadingPerformance.DirectShadingEfficiency, "direct shading efficiency");

            if (Math.Abs(shadingPerformance.UnattributedInterceptedEnergy) > 1e-9)
            {
                stringBuilder.AppendFormat(CultureInfo.InvariantCulture, " | {0:0.#} kWh UNATTRIBUTED", shadingPerformance.UnattributedInterceptedEnergy);
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// The interception split, so the headline energies add up on the canvas instead of looking
        /// as though some has gone missing:
        ///
        ///   61.3 kWh intercepted = 46 unwanted + 0 wanted + 15.3 neither
        ///
        /// The third term is the beam the brief claimed neither way — under the default seasonal
        /// brief, spring and autumn. It is reported unconditionally, including when it is zero,
        /// because a residual that only appears when it is awkward teaches the reader nothing.
        /// See <see cref="ShadingPerformance.AdmittedNeutralEnergy"/> for the general treatment.
        /// </summary>
        /// <param name="shadingPerformance">The verified performance.</param>
        public static string AccountingSummary(this ShadingPerformance shadingPerformance)
        {
            if (shadingPerformance == null)
            {
                return "Not verified.";
            }

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendFormat(CultureInfo.InvariantCulture,
                "Admitted without the device: {0:0.#} kWh = {1:0.#} unwanted + {2:0.#} wanted + {3:0.#} neither",
                shadingPerformance.AdmittedDirectEnergy, shadingPerformance.AdmittedUnwantedEnergy,
                shadingPerformance.AdmittedWantedEnergy, shadingPerformance.AdmittedNeutralEnergy);

            stringBuilder.AppendLine();
            stringBuilder.AppendFormat(CultureInfo.InvariantCulture,
                "Intercepted by the device:   {0:0.#} kWh = {1:0.#} unwanted + {2:0.#} wanted + {3:0.#} neither",
                shadingPerformance.DirectSolarIntercepted, shadingPerformance.UnwantedSolarIntercepted,
                shadingPerformance.WantedSolarBlocked, shadingPerformance.NeutralSolarIntercepted);

            return stringBuilder.ToString();
        }

        /// <summary>Parameter values with the units they are actually in, or null when there are none.</summary>
        public static string ParameterText(IEnumerable<string> names, Func<string, double> value)
        {
            List<string> nameList = names == null ? new List<string>() : new List<string>(names);
            if (nameList.Count == 0)
            {
                return null;
            }

            StringBuilder stringBuilder = new StringBuilder();
            foreach (string name in nameList)
            {
                double parameter = value(name);
                if (double.IsNaN(parameter))
                {
                    continue;
                }

                if (stringBuilder.Length != 0)
                {
                    stringBuilder.Append(", ");
                }

                switch (name)
                {
                    case "Depth":
                    case "Projection":
                    case "RiseAboveHead":
                    case "ExtensionBeyondJambs":
                    case "ValanceDepth":
                        stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:0.##} m", name, parameter);
                        break;
                    case "TiltDegrees":
                        stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:0.#}°", name, parameter);
                        break;
                    default:
                        stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:0}", name, parameter);
                        break;
                }
            }

            return stringBuilder.Length == 0 ? null : stringBuilder.ToString();
        }

        private static void AppendAzimuth(StringBuilder stringBuilder, double azimuth)
        {
            if (!double.IsNaN(azimuth))
            {
                stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0}° | ", azimuth);
            }
        }

        /// <summary>
        /// A percentage, or "n/a" when the underlying ratio has no denominator. Never 0 % or 100 %
        /// by default — an unavailable metric that reads as a number is worse than no metric.
        /// </summary>
        private static void AppendPercentage(StringBuilder stringBuilder, double ratio, string label)
        {
            stringBuilder.Append(" | ");
            if (double.IsNaN(ratio))
            {
                stringBuilder.Append("n/a ").Append(label);
                return;
            }

            stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#}% {1}", 100.0 * ratio, label);
        }

        private static void AppendEnergy(StringBuilder stringBuilder, double kWh, string label)
        {
            stringBuilder.Append(" | ");
            if (double.IsNaN(kWh))
            {
                stringBuilder.Append("n/a ").Append(label);
                return;
            }

            stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} kWh {1}", kWh, label);
        }
    }
}
