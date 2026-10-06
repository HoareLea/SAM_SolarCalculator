// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.ComponentModel;

namespace SAM.Geometry.SolarCalculator
{
    /// <summary>Angular subdivision of the sky dome into directional patches.</summary>
    [Description("Sky Patch Subdivision")]
    public enum SkyPatchSubdivision
    {
        [Description("Undefined")] Undefined,
        [Description("Tregenza 145 patches (7 bands + zenith)")] Tregenza145,
        [Description("Reinhart MF:4 577 patches")] Reinhart577,
    }
}
