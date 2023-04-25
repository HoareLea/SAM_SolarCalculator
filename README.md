# SAM_SolarCalculator

<a href="https://github.com/HoareLea/SAM_Excel"><img src="https://github.com/HoareLea/SAM/blob/master/Grasshopper/SAM.Core.Grasshopper/Resources/SAM_Small.png" align="left" hspace="10" vspace="6"></a>

**SAM** is part of SAM Toolkit that is designed to help engneers to create Analytical Model. Welcome and let's make the opensource journey continue. :handshake:

`SAM_SolarCalculator` is a module of the Sustainable Analytical Model (SAM) software developed by [Hoare Lea](https://hoarelea.com/). It provides functionality for simulating solar radiation on building surfaces and generating energy models.

## Features

- Calculation of solar irradiance on surfaces
- Calculation of solar shading and self-shading effects
- Generation of energy models for buildings based on the calculated solar irradiance

## Resources
* [Wiki](https://github.com/HoareLea/SAM/wiki)

## Installing

To install **SAM** from .exe just download and run [latest installer](https://github.com/HoareLea/SAM_Deploy/releases) otherwise rebuild using VS [SAM](https://github.com/HoareLea/SAM)

`SAM_SolarCalculator` is a C# project that can be compiled and integrated into larger projects. It has the following dependencies:

- [SAM.Core](https://github.com/HoareLea/SAM), the core library of the SAM software
- [SAM.Geometry](https://github.com/HoareLea/SAM.Geometry), the geometry module of the SAM software

## Usage

`SAM_SolarCalculator` provides a `Simulate` class with methods for simulating solar irradiance and generating energy models. 

```csharp
public static IEnumerable<Surface> Simulate(this IEnumerable<Surface> surfaces, DateTime dateTime, double timeZone = 0, double inclination = 0, double azimuth = 0, double tolerance = Tolerance.Angle, double areaAdjustment = 1, double altitude = double.NaN, double latitude = double.NaN, double longitude = double.NaN, double diffusedFraction = 0.3, double directFraction = 0.7)
```

```csharp
public static EnergyBase Simulate(this Space space, double solarReflectance, double visibleReflectance, double[] assemblyThicknesses, IEnumerable<Panel> panels, Material sunMaterial, Material skyMaterial, double gridSize = 1, double tolerance = Tolerance.MicroDistance, double offset = 0.1, double factor = 2.5)
```

## Examples

Examples of how to use `SAM_SolarCalculator` can be found in the [Examples](https://github.com/HoareLea/SAM_SolarCalculator/tree/master/Examples) directory. 

## Licence ##

SAM is free software licenced under GNU Lesser General Public Licence - [https://www.gnu.org/licenses/lgpl-3.0.html](https://www.gnu.org/licenses/lgpl-3.0.html)  
Each contributor holds copyright over their respective contributions.
The project versioning (Git) records all such contribution source information.
See [LICENSE](https://github.com/HoareLea/SAM_Template/blob/master/LICENSE) and [COPYRIGHT_HEADER](https://github.com/HoareLea/SAM/blob/master/COPYRIGHT_HEADER.txt).
