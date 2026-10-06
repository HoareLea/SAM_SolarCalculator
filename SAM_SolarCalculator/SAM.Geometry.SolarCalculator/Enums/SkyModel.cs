// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.ComponentModel;

namespace SAM.Geometry.SolarCalculator
{
    /// <summary>Sky luminance/radiance distribution model used for diffuse irradiance.</summary>
    [Description("Sky Model")]
    public enum SkyModel
    {
        [Description("Undefined")] Undefined,
        [Description("Isotropic (Liu-Jordan)")] Isotropic,
        [Description("Perez et al. 1990 all-weather anisotropic")] PerezAnisotropic,
    }
}
