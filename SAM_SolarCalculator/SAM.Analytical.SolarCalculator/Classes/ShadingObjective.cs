// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Which admitted energy the material cost is measured against. The cost term has to be an
    /// ENERGY so it can be added to the benefit and harm terms; this says which one.
    /// </summary>
    public enum MaterialCostReference
    {
        /// <summary>
        /// The whole admitted direct beam. The Stage 9 default. Non-zero whenever any device could
        /// do anything at all, so material always carries a real cost.
        /// </summary>
        AdmittedDirectEnergy,

        /// <summary>
        /// The admitted UNWANTED beam — what Stage 8's ShadingFitScore uses. Retained for
        /// comparability with Stage 8 results, but note that it collapses to zero on an aperture
        /// with no unwanted solar, taking the whole cost term with it.
        /// </summary>
        AdmittedUnwantedEnergy,
    }

    /// <summary>
    /// The Stage 9 objective: the explicit statement of what "better" means, kept separate from the
    /// search so the two can be reasoned about independently.
    ///
    /// <code>
    ///   Benefit = UnwantedSolarIntercepted                       [kWh]
    ///   Harm    = WantedSolarBlocked                             [kWh]
    ///   Cost    = MaterialFraction x ReferenceEnergy             [kWh]
    ///
    ///   Score   = Benefit - WantedSolarPenalty x Harm - MaterialPenalty x Cost    [kWh]
    /// </code>
    ///
    /// SIGNS. All three components are POSITIVE quantities and are named for what they are rather
    /// than which way they point. Benefit is added; Harm and Cost are subtracted. Adding Harm would
    /// reward a device for destroying the winter sun, which is the single easiest error to make
    /// here and the reason the term is called WantedSolarBlocked and not something neutral.
    ///
    /// UNITS. Every term is kWh, so the score is kWh and the two penalties are dimensionless
    /// weights. WantedSolarPenalty is an exchange rate: how many kWh of unwanted solar blocked is
    /// worth one kWh of wanted solar lost. MaterialPenalty is the share of the aperture's admitted
    /// beam a designer is willing to forgo per unit of MaterialFraction.
    ///
    /// NORMALISATION. The cost is scaled by an admitted energy rather than left as a bare fraction,
    /// which makes the whole objective scale linearly with the site's radiation. Two identical
    /// buildings under weather differing only in magnitude therefore get the same device, and the
    /// cost-to-benefit weighting stays a property of the brief instead of a property of how sunny
    /// it is. Measured in MaterialPenaltyTests: a 300x change in radiation moves the energy-scaled
    /// cost/benefit ratio by 0 %, and a bare-fraction cost/benefit ratio by 300x.
    ///
    /// WHY THE REFERENCE ENERGY DIFFERS FROM STAGE 8. Stage 8's ShadingFitScore scales material by
    /// AdmittedUnwantedEnergy. That is dimensionally fine and behaves well whenever there IS
    /// unwanted solar, but it goes to zero exactly when there is none — and an aperture with no
    /// unwanted solar is the case where Stage 9 most needs the cost term, because it is the case
    /// where the right answer is "build nothing". Stage 9 therefore defaults to
    /// AdmittedDirectEnergy. Stage 8's own behaviour is left unchanged.
    ///
    /// ZERO DENOMINATORS. The score is an absolute energy, so it needs no denominator and stays
    /// defined when there is no unwanted or no wanted solar. The reported PERCENTAGES on
    /// ShadingPerformance keep their own NaN-on-zero-denominator rule; nothing here converts a NaN
    /// percentage into a number.
    ///
    /// THE NULL DEVICE. Building nothing scores exactly 0: no benefit, no harm, no cost. Any
    /// candidate scoring below zero is worse than leaving the aperture alone, which is what lets
    /// Stage 9 answer "no shading is worth building" instead of returning the least-bad geometry.
    /// </summary>
    /// <summary>How a penalty value sits against the objective's declared domain.</summary>
    public enum PenaltyValidity
    {
        /// <summary>Inside the range this objective was designed and tested for.</summary>
        Recommended,

        /// <summary>Mathematically valid and meaningful, but outside normal engineering use. The answer is sound; check it is the question you meant to ask.</summary>
        Extreme,

        /// <summary>Outside the valid domain. The objective would reward the wrong thing, or is not a number at all.</summary>
        Invalid,
    }

    public class ShadingObjective : IJSAMObject, ISolarObject
    {
        // ------------------------------------------------------------------- the domain ----

        /// <summary>
        /// THE VALID DOMAIN OF BOTH PENALTIES: finite and &gt;= 0. There is no upper bound, and
        /// inventing one would be a UI convenience dressed up as physics.
        ///
        /// WHY ZERO IS THE FLOOR, AND WHY IT IS A HARD ONE. Both penalties multiply a POSITIVE
        /// quantity that is SUBTRACTED from the score:
        ///
        ///   Score = UnwantedSolarIntercepted - lambda x WantedSolarBlocked - mu x Cost
        ///
        /// A negative lambda turns "- lambda x WantedSolarBlocked" into an ADDITION, so the search
        /// is paid to destroy the winter sun the brief asked it to preserve, and the deeper the
        /// device the better it scores — the objective no longer expresses any brief a person would
        /// write. A negative mu likewise pays for material, and since Cost rises without limit as
        /// the device grows while benefit saturates, the search runs to the largest device its
        /// bounds allow. Neither is a strange-but-defensible weighting; each inverts the meaning of
        /// its own term. They are refused rather than clamped, because silently reading -2 as 0
        /// would answer a question nobody asked.
        ///
        /// If a project genuinely wants to MAXIMISE solar gain, that is a different brief, and it is
        /// expressed the way the model already supports: swap the wanted and unwanted periods, or
        /// supply a desirability strategy whose weights carry the sign. The penalties stay weights.
        ///
        /// NaN and infinity are refused for the ordinary reason: every candidate's score would be
        /// NaN or -infinity, no candidate could beat the null device, and the run would report "no
        /// shading is worth building here" — a confident engineering answer it never earned.
        /// </summary>
        public const double MinimumPenalty = 0.0;

        /// <summary>
        /// Above this, a penalty is <see cref="PenaltyValidity.Extreme"/>: still valid, still
        /// meaningful, but far outside normal use and worth saying so.
        ///
        /// The number is read off the behaviour rather than chosen. Both penalties are exchange
        /// rates against the SAME unit (kWh of unwanted solar intercepted), so a value of 10 means
        /// "one kWh of wanted solar lost must be repaid by ten kWh of unwanted solar blocked", or
        /// "a device covering the whole aperture must repay ten times the aperture's entire admitted
        /// beam". Measured on the controlled north window (MaterialPenaltyTests): material penalties
        /// of 5 and above already return NO SHADE with the best candidate scoring -4.8 kWh, i.e. the
        /// answer has stopped depending on the value and only its magnitude changes. A weighting
        /// that can no longer change the recommendation is past the point of being a design
        /// parameter, and 10 is comfortably beyond where that sets in for both penalties.
        /// </summary>
        public const double ExtremePenalty = 10.0;

        /// <summary>
        /// Where a penalty value sits: refused, unusual, or normal. Used by the Grasshopper
        /// components so the same rule is stated in one place and cannot drift between them.
        /// </summary>
        public static PenaltyValidity Validity(double penalty)
        {
            if (double.IsNaN(penalty) || double.IsInfinity(penalty) || penalty < MinimumPenalty)
            {
                return PenaltyValidity.Invalid;
            }

            return penalty > ExtremePenalty ? PenaltyValidity.Extreme : PenaltyValidity.Recommended;
        }

        private double wantedSolarPenalty = 1.0;
        private double materialPenalty = 0.1;
        private MaterialCostReference materialCostReference = MaterialCostReference.AdmittedDirectEnergy;

        public ShadingObjective()
        {
        }

        public ShadingObjective(double wantedSolarPenalty, double materialPenalty = 0.1, MaterialCostReference materialCostReference = MaterialCostReference.AdmittedDirectEnergy)
        {
            this.wantedSolarPenalty = wantedSolarPenalty;
            this.materialPenalty = materialPenalty;
            this.materialCostReference = materialCostReference;
        }

        public ShadingObjective(ShadingObjective shadingObjective)
        {
            if (shadingObjective != null)
            {
                wantedSolarPenalty = shadingObjective.wantedSolarPenalty;
                materialPenalty = shadingObjective.materialPenalty;
                materialCostReference = shadingObjective.materialCostReference;
            }
        }

        public ShadingObjective(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>
        /// Lambda: kWh of unwanted solar blocked considered worth one kWh of wanted solar lost.
        ///
        /// Valid: finite, &gt;= 0 (see <see cref="MinimumPenalty"/>). Default 1.0 — an even trade,
        /// which is the only value that assumes nothing about the brief. Recommended working range
        /// 0.5 to 2.0: below 1 the search buys deeper devices because winter loss is cheap, above 1
        /// shallower ones because it is dear. Raising it does NOT change any measured energy; it
        /// changes which measured design wins.
        /// </summary>
        public double WantedSolarPenalty { get { return wantedSolarPenalty; } }

        /// <summary>
        /// Mu: share of the reference energy charged per unit of MaterialFraction.
        ///
        /// Valid: finite, &gt;= 0 (see <see cref="MinimumPenalty"/>). Default 0.1 — a mild
        /// preference for the leaner of two near-equal designs, small enough not to override the
        /// energy answer. 0 sizes on energy alone and tends to return the largest device that still
        /// helps at all. Raising it shrinks the recommended device and eventually returns NO SHADE,
        /// which is a real answer and not a failure.
        /// </summary>
        public double MaterialPenalty { get { return materialPenalty; } }

        /// <summary>Where <see cref="WantedSolarPenalty"/> sits against the declared domain.</summary>
        public PenaltyValidity WantedSolarPenaltyValidity { get { return Validity(wantedSolarPenalty); } }

        /// <summary>Where <see cref="MaterialPenalty"/> sits against the declared domain.</summary>
        public PenaltyValidity MaterialPenaltyValidity { get { return Validity(materialPenalty); } }

        public MaterialCostReference MaterialCostReference { get { return materialCostReference; } }

        /// <summary>The admitted energy the material cost is measured against, kWh.</summary>
        public double ReferenceEnergy(ShadingPerformance performance)
        {
            if (performance == null)
            {
                return double.NaN;
            }

            return materialCostReference == MaterialCostReference.AdmittedUnwantedEnergy
                ? performance.AdmittedUnwantedEnergy
                : performance.AdmittedDirectEnergy;
        }

        /// <summary>Benefit: the unwanted beam the candidate intercepts, kWh. Always &gt;= 0.</summary>
        public double Benefit(ShadingPerformance performance)
        {
            return performance == null ? double.NaN : performance.UnwantedSolarIntercepted;
        }

        /// <summary>Harm: the wanted beam the candidate destroys, kWh. Always &gt;= 0.</summary>
        public double Harm(ShadingPerformance performance)
        {
            return performance == null ? double.NaN : performance.WantedSolarBlocked;
        }

        /// <summary>Cost: material as a share of the aperture, priced in kWh. Always &gt;= 0.</summary>
        public double Cost(ShadingPerformance performance)
        {
            if (performance == null)
            {
                return double.NaN;
            }

            double materialFraction = performance.MaterialFraction;
            if (double.IsNaN(materialFraction))
            {
                // An unmeasurable device is not a free one, but nothing here can price it, so it is
                // reported as no cost rather than guessed at. Callers comparing a device with a
                // known material quantity against one without are comparing unlike things.
                materialFraction = 0.0;
            }

            double reference = ReferenceEnergy(performance);
            return double.IsNaN(reference) ? 0.0 : materialFraction * reference;
        }

        /// <summary>The scalar objective, kWh. Higher is better; 0 is exactly "build nothing".</summary>
        public double Score(ShadingPerformance performance)
        {
            if (performance == null)
            {
                return double.NaN;
            }

            return Benefit(performance) - wantedSolarPenalty * Harm(performance) - materialPenalty * Cost(performance);
        }

        /// <summary>
        /// The same objective over a GROUP performance: benefit, harm and cost read from the group's
        /// summed energies and its ONE shared material charge — never from per-window costs summed.
        /// </summary>
        public double Score(GroupedShadingPerformance performance)
        {
            if (performance == null)
            {
                return double.NaN;
            }

            return Benefit(performance) - wantedSolarPenalty * Harm(performance) - materialPenalty * Cost(performance);
        }

        /// <summary>The admitted energy the group material cost is measured against, kWh.</summary>
        public double ReferenceEnergy(GroupedShadingPerformance performance)
        {
            if (performance == null)
            {
                return double.NaN;
            }

            return materialCostReference == MaterialCostReference.AdmittedUnwantedEnergy
                ? performance.AdmittedUnwantedEnergy
                : performance.AdmittedDirectEnergy;
        }

        /// <summary>Benefit: the unwanted beam the shared device intercepts across the group, kWh.</summary>
        public double Benefit(GroupedShadingPerformance performance)
        {
            return performance == null ? double.NaN : performance.UnwantedSolarIntercepted;
        }

        /// <summary>Harm: the wanted beam the shared device destroys across the group, kWh.</summary>
        public double Harm(GroupedShadingPerformance performance)
        {
            return performance == null ? double.NaN : performance.WantedSolarBlocked;
        }

        /// <summary>Cost: the ONE shared material charge priced in kWh.</summary>
        public double Cost(GroupedShadingPerformance performance)
        {
            if (performance == null)
            {
                return double.NaN;
            }

            double materialFraction = performance.MaterialFraction;
            if (double.IsNaN(materialFraction))
            {
                materialFraction = 0.0;
            }

            double reference = ReferenceEnergy(performance);
            return double.IsNaN(reference) ? 0.0 : materialFraction * reference;
        }

        /// <summary>The same objective over a complete SCHEME performance.</summary>
        public double Score(ShadingSchemePerformance performance)
        {
            if (performance == null)
            {
                return double.NaN;
            }

            return Benefit(performance) - wantedSolarPenalty * Harm(performance) - materialPenalty * Cost(performance);
        }

        /// <summary>The admitted energy the scheme material cost is measured against, kWh.</summary>
        public double ReferenceEnergy(ShadingSchemePerformance performance)
        {
            if (performance == null)
            {
                return double.NaN;
            }

            return materialCostReference == MaterialCostReference.AdmittedUnwantedEnergy
                ? performance.AdmittedUnwantedEnergy
                : performance.AdmittedDirectEnergy;
        }

        /// <summary>Benefit: the unwanted beam the scheme intercepts, kWh.</summary>
        public double Benefit(ShadingSchemePerformance performance)
        {
            return performance == null ? double.NaN : performance.UnwantedSolarIntercepted;
        }

        /// <summary>Harm: the wanted beam the scheme destroys, kWh.</summary>
        public double Harm(ShadingSchemePerformance performance)
        {
            return performance == null ? double.NaN : performance.WantedSolarBlocked;
        }

        /// <summary>Cost: the scheme material charge priced in kWh.</summary>
        public double Cost(ShadingSchemePerformance performance)
        {
            if (performance == null)
            {
                return double.NaN;
            }

            double materialFraction = performance.MaterialFraction;
            if (double.IsNaN(materialFraction))
            {
                materialFraction = 0.0;
            }

            double reference = ReferenceEnergy(performance);
            return double.IsNaN(reference) ? 0.0 : materialFraction * reference;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("WantedSolarPenalty")) { wantedSolarPenalty = jObject["WantedSolarPenalty"]?.GetValue<double>() ?? 1.0; }
            if (jObject.ContainsKey("MaterialPenalty")) { materialPenalty = jObject["MaterialPenalty"]?.GetValue<double>() ?? 0.1; }
            if (jObject.ContainsKey("MaterialCostReference"))
            {
                Enum.TryParse(jObject["MaterialCostReference"]?.GetValue<string>(), out materialCostReference);
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            jObject.Add("WantedSolarPenalty", wantedSolarPenalty);
            jObject.Add("MaterialPenalty", materialPenalty);
            jObject.Add("MaterialCostReference", materialCostReference.ToString());
            return jObject;
        }
    }
}
