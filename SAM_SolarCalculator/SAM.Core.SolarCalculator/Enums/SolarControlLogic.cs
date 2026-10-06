// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.ComponentModel;

namespace SAM.Core.SolarCalculator
{
    /// <summary>
    /// How the solar criterion and the (optional) outdoor-temperature criterion are combined into a
    /// single "shading is wanted this hour" decision.
    ///
    /// A criterion that is not in use takes NO part in the combination: it neither blocks an AND nor
    /// satisfies an OR. Leaving the temperature threshold empty therefore reduces both logics to the
    /// solar criterion alone, which is the predictable default.
    /// </summary>
    [Description("Solar Control Logic")]
    public enum SolarControlLogic
    {
        [Description("Undefined")] Undefined,

        /// <summary>Every criterion in use must be met (bright ENOUGH and warm enough).</summary>
        [Description("All criteria in use must be met")] And,

        /// <summary>Any criterion in use is enough (bright enough OR warm enough).</summary>
        [Description("Any criterion in use is enough")] Or,
    }
}
