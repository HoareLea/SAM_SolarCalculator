// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// A buildable shading device family: a small named parameter set with bounds, and the rule
    /// that turns those parameters into opaque faces in front of an aperture.
    ///
    /// Stage 8 rationalisation sweeps the parameters of a typology and scores each candidate
    /// against the Stage 6 field and the Stage 5 desirability, so a typology only has to describe
    /// WHAT it is, never how good it is.
    ///
    /// PERFORATED SCREENS ARE DELIBERATELY ABSENT. A screen's whole point is partial transmission,
    /// and the direct ray engine underneath every stage here is binary: a ray is blocked or it is
    /// not. The plausible-looking shortcut — an opaque face plus a porosity scalar applied to the
    /// energy — is wrong in a way that matters, because a real screen's effective transmission
    /// depends on the incidence angle and on the depth-to-opening ratio of each perforation, and it
    /// varies through the day by far more than the nominal open-area ratio suggests. It would
    /// produce numbers that look like a screen's and are not, in the metrics engineers would use to
    /// size one. Until the engine can carry angle-dependent transmission, this family is
    /// unsupported and says so, which is the honest answer.
    /// </summary>
    public interface IShadingTypology : IJSAMObject, ISolarObject
    {
        /// <summary>Human-readable family name, e.g. "Overhang".</summary>
        string Name { get; }

        /// <summary>Parameter names in a fixed order, for sweeps and reporting.</summary>
        List<string> ParameterNames { get; }

        /// <summary>Current value of a named parameter, NaN when the name is unknown.</summary>
        double GetParameter(string name);

        /// <summary>Sets a named parameter, clamped into its bounds. False when the name is unknown.</summary>
        bool SetParameter(string name, double value);

        /// <summary>Inclusive bounds of a named parameter. False when the name is unknown.</summary>
        bool TryGetBounds(string name, out double minimum, out double maximum);

        /// <summary>
        /// The device as opaque faces in front of the target, in the aperture's local frame
        /// (X across the facade, Y up-slope, Z outward). Deterministic: the same typology and
        /// parameters give the same elements with the same Guids.
        /// </summary>
        List<ShadingElement> ShadingElements(ApertureSolarTarget target);

        /// <summary>
        /// Material quantity as a fraction of the aperture's gross area — the complexity/cost term
        /// the fit score penalises. Total element area / aperture area.
        /// </summary>
        double MaterialFraction(ApertureSolarTarget target);
    }
}
