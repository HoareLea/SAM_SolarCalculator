# Stages 0–4 implementation — method, conventions and measured validation

Implementation record for the per-aperture irradiance vertical slice of
`ShadingOptimisation-Plan.md` (Stages 0–4). This document is the formulation reference and the
current source of truth for the numbers; the full validation suite and the assumptions register
remain Stage 11 scope.

Every number in §8 was measured on the build this document was committed with. If a change moves
them, replace them here — do not keep an older baseline for comparison.

## 1. Architecture

```
AnalyticalModel (+ WeatherData)
   └─ Create.ApertureSolarTargets            Stage 0  outward-normal-resolved aperture targets
   └─ Convert.ToSAM_OccluderLinkedFace3Ds             context (panels cut at apertures + shades)
   └─ Create.SunBins / SolarVisibilityCache  Stage 2  geometry × sun-group lit bitsets
   └─ Create.SkyVisibilityCache              Stage 3  per-cell SVF / horizon / ground visibility
   └─ AnalysisPeriod                         Stage 1  hour selection (weather timeline)
   └─ Query.CachedIrradiance                 Stage 4  arithmetic re-weighting → kWh/m² per cell
   └─ Modify.SimulateApertures               Stage 4  orchestration + AddResult<Aperture>
```

Both caches are stored on the `SolarModel` attached under `AnalyticalModelParameter.SolarModel`
(`SolarModelParameter.SolarVisibilityCache` / `SkyVisibilityCache`) and reused whenever their
identity matches. Changing the `AnalysisPeriod` or the weather file performs **no** geometric
recomputation.

### 1.1 Vocabulary: engineer-facing vs implementation

Per the §2.4 decision recorded in PR #12, the public entry points speak the engineer's vocabulary
and the implementation keeps its own. The mapping is fixed and one-to-one, so the Stage 10
components pass their inputs straight through without translating:

| Engineer-facing (public API, and the future Grasshopper input) | Implementation |
|---|---|
| `gridSize` — aperture analysis-grid size, m | `AnalysisCell`, `Query.AnalysisCells`, `SolarVisibilityCache.CellSize` |
| `sunAngleStep` — angular resolution for grouping similar sun positions, ° | `SunBin`, `Create.SunBins`, `SolarVisibilityCache.BinSizeDegrees` |
| `recalculate` — force the solar visibility calculation to rebuild | cache rebuild |
| `reusedPreviousCalculation` — diagnostic | cache-hit flag |
| sun group | sun bin |
| analysis grid / grid point | analysis cell |

"Cache", "bin" and "cell" are internal type-level vocabulary and must not appear in a Grasshopper
input, output or description.

## 2. Timelines, and the timestamp convention (the definitive statement)

Two distinct timelines exist and are never mixed silently:

1. the **weather timeline** — the hour `h` at which weather values are read
   (`WeatherData.GetWeatherHour(h)`);
2. the **sun-sampling timeline** — the instant `h + shift` at which the sun position is evaluated
   (group membership, incidence angle, Perez inputs).

`shift` is chosen by `SAM.Core.SolarCalculator.SunTimeConvention` and mapped by
`Query.TimeShiftInMinutes()`.

### 2.1 What SAM stores for EPW-imported hourly radiation

Verified against `SAM.Weather.Query.TryGetData` (SAM core repo), the EPW importer:

- reads the EPW **hour field (1–24)** and applies `hour = hour - 1`;
- reads the comma-split data line into a zero-based `values[]` array, where `values[i]` is EPW field
  `i + 1` (field 1, `Year`, lands at `values[0]`):
  - `values[13]` — EPW field 14, Global Horizontal Radiation — imported as `GlobalSolarRadiation`;
  - `values[14]` — EPW field 15, Direct Normal Radiation — **currently not imported**, so
    `DirectSolarRadiation` is left unpopulated;
  - `values[15]` — EPW field 16, Diffuse Horizontal Radiation — imported as `DiffuseSolarRadiation`.

EPW labels an interval by its **end**: hour field `H` carries values averaged over `(H−1):00 → H:00`.
After the decrement, the SAM timestamp is `H−1`, i.e. **the start of the interval the values describe**.

So, definitively:

| Question | Answer |
|---|---|
| What timestamp does SAM store? | The interval **start** (EPW hour field minus one). |
| What interval does that radiation represent? | The whole hour **from** that timestamp **to** timestamp + 1 h. |
| What representative sun position applies? | The **interval midpoint**, timestamp **+ 30 min**. |
| Why does `IntervalStart` imply +30 min? | Because the stored value is an hour-**average**, and the sun position that best represents an hour-average of beam radiation is the one at the middle of that hour. The label is at the start; the midpoint is 30 minutes later. |

`SunTimeConvention.IntervalStart` (+30 min) is therefore the **default** for `SimulateApertures`, and
it is a statement about the SAM/EPW weather timeline — not a correction, not a fudge factor.

### 2.2 TAS EDSL compatibility is a separate concept

TAS EDSL labels the same physical interval by its **end**. When a SAM `DateTime` grid is being driven
to line up hour-for-hour with a TAS hourly series, the matching sun sample is at timestamp **− 30 min**
(`SunTimeConvention.IntervalEnd`) — the same physical midpoint, reached from the other label
convention.

This, and only this, is what the legacy Grasshopper `_timeShift_ = −30` default encoded. It is a
**TAS-compatibility convention, not a generic weather correction**, and the two must not be
described interchangeably. `SunTimeConvention.OnTheHour` (0) samples the sun at the timestamp itself
and is offered for research and for timelines whose semantics are genuinely instantaneous.

Note the consequence: `IntervalStart` and `IntervalEnd` are a **full hour** apart. Choosing the wrong
one is a real, measurable error (see §8, T2 detection power: 58.7 % east/west direct asymmetry for a
30-minute error).

### 2.3 The shift lives in the cache

The shift is part of the `SolarVisibilityCache` identity. Sun groups are built from the sun positions
at `h + shift`; the evaluation reads the shift **back off the cache**. The two timelines therefore
cannot drift apart, and a convention change invalidates rather than silently reusing groups built on
the other timeline.

Both group construction (`Create.SunBins`) and group lookup (`Query.CachedIrradiance`) obtain the
solar angles from the **same** function, `Geometry.SolarCalculator.Query.TryGetSunAngles(Location,
DateTime)`, so the two are bit-identical computations rather than two derivations that merely agree
in exact arithmetic. Measured consequence: `MissedBinHours == 0` at every shift tested (§8).

`Innovative.Geometry.Angle.Degrees` **rounds to whole degrees**. All sun-angle reads use
`Angle.Radians`. Using `Degrees` injected up to 0.5° into group lookup, incidence angles and the
Perez inputs.

## 3. Conventions (measured, not assumed)

Sun vectors (`Query.SunDirection`, `Create.SunDirection(altitude, azimuth)`): the vector points
**sun → surface**, so `Z < 0` when the sun is up. Azimuth is compass degrees clockwise from north
(+Y). `Query.TryGetSunAngles(Vector3D, …)` is the exact inverse.

### 3.1 Two radiation convention systems, deliberately not interchangeable

**Legacy — `Geometry.SolarCalculator.Create.Radiation(SolarTimes, double tilt, double surfaceAzimuth, …)`.**
A runtime probe of the released code shows it (a) assumes the plane normal points *into* the
receiving side (`tilt_Temp = 180 − tilt`) and (b) rotates the solar azimuth by +90°
(`solarAzimuth = (rad + π/2)·180/π`), which mis-assigns the direct beam by one orientation on tilted
surfaces — a south wall at solar noon receives the east wall's beam. It also consumes SAM's
`CalculatedDirectSolarRadiation()` (global − diffuse, i.e. beam on **horizontal**) as if it were DNI.
**This is unchanged and will stay unchanged** — binary, source and behavioural compatibility — and is
frozen by `Legacy_Isotropic_GoldenValues_Frozen` and
`T3_Legacy_Overload_Is_Not_The_Reference_And_Still_Disagrees`.

**Corrected physical — `Geometry.SolarCalculator.Create.Radiation(SolarTimes, Plane outwardPlane, dni, dhi, ghi, SkyModel, skyViewFactorMultiplier, groundViewFactorMultiplier, albedo)`.**
Deliberately a different API *shape* (a `Plane`, not two doubles) so the two systems cannot be mixed
up at a call site:

- the receiving surface is its **outward-oriented plane**; tilt `β` from `cos β = normal · +Z`;
- solar azimuth used **unrotated** (NOAA, clockwise from north);
- `directNormalIrradiance` is **true DNI**, W/m²;
- frozen by `Corrected_Physical_GoldenValues_Frozen`.

The ambiguous double-based `SkyModel` overloads added earlier in this PR were removed (B7); that
shape should not be reintroduced.

### 3.2 Beam-horizontal is derived from an unambiguous source only (B6)

`Query.CachedIrradiance` reads the **raw** `WeatherHour.GlobalSolarRadiation` and
`WeatherHour.DiffuseSolarRadiation` properties and computes

```
beamHorizontal = max(0, GHI − DHI)
DNI            = beamHorizontal / max(sin(elevation), sin 5°)
```

It does **not** call `CalculatedGlobalSolarRadiation()` / `CalculatedDiffuseSolarRadiation()`, because
each of those falls back to `DirectSolarRadiation` when its own field is missing
(`global := direct + diffuse`, `diffuse := global − direct`) — which would reintroduce exactly the
ambiguity being removed. `DirectSolarRadiation` is **never** read on any path of the new workflow:
its semantics depend on who created the `WeatherData` (beam-horizontal from a SAM-internal producer,
true DNI from an EPW field-14 reader), so consuming it risks dividing a genuine DNI by
`sin(elevation)` a second time. An hour missing either raw field is counted in `MissingWeatherHours`
rather than silently reinterpreted.

The legacy `Weather.SolarCalculator.Create.Radiation(WeatherData, DateTime, Plane, …)` path still
consumes `CalculatedDirectSolarRadiation()` exactly as before; that behaviour is pinned by
`B6_Legacy_Radiation_Path_Still_Consumes_The_Direct_Field_Unchanged`.

## 4. Perez 1990 formulation with component-aware obstruction

Per weather hour `h`, per cell (Perez et al. 1990, Solar Energy 44(5), 271–289):

```
epsilon = ((DHI + DNI)/DHI + 1.041*theta_z^3) / (1 + 1.041*theta_z^3)     [theta_z in radians]
delta   = m * DHI / I0n,   m = Kasten-Young air mass, I0n = 1367*(1+0.033*cos(2*pi*doy/365))
F1 = max(0, f11 + f12*delta + f13*theta_z),  F2 = f21 + f22*delta + f23*theta_z   [8-bin table]
a = max(0, cos(thetaI)),   b = max(cos 85 deg, cos theta_z)

beam    = DNI * a * lit(cell, group(h))
diffuse = DHI * [ (1-F1) * SVF(cell)                       <- isotropic component
                + F1 * (a/b) * lit(cell, group(h))         <- circumsolar: sun-direction lit bit
                + F2 * sin(beta) * HVF(cell) ]             <- horizon: horizon-band visibility
ground  = GHI * albedo * GVF(cell)                         <- isotropic ground
```

- `SVF(cell)` — cosine-weighted sky view factor from the Tregenza-145 patch ray-cast
  (`(1/π) · Σ_visible cos θ ω`); unobstructed vertical = 0.5 analytically, 0.4979–0.5008 measured.
- `HVF(cell)` — cosine-weighted visible fraction of the lowest (0–12°) sky band, normalised to 1
  when the whole band is visible.
- `GVF(cell)` — cosine-weighted ground view factor over the mirrored ground dome.
- `lit(cell, group)` — the direct-beam cache bit; it is also the circumsolar obstruction state.
- The composed diffuse is clamped ≥ 0: Perez's `(1 − F1)` can go negative under very clear skies,
  and a composed component cannot be physically negative.

Note that in the **cached** path `SVF` and `GVF` are **absolute** view factors (they already contain
the `(1 ± cos β)/2` geometric factor). In the **standalone** `Create.Radiation` overload the
`skyViewFactorMultiplier` / `groundViewFactorMultiplier` arguments are **relative** multipliers
applied on top of `(1 ± cos β)/2`. Passing 1 there is therefore equivalent to the cached path's
unobstructed case; passing an absolute view factor (e.g. `SkyVisibilityCache.SkyViewFactor(...)`,
~0.5 on an unobstructed vertical surface) into this multiplier double-applies the geometric factor
and understates diffuse by roughly half. The two are cross-checked in §8, T3.

`SkyModel.Isotropic` in the evaluation path uses `diffuse = DHI · SVF(cell)` — the same
obstruction-aware form factor; the legacy `cos²(tilt/2)` value is recovered exactly for unobstructed
cells. Whole-hour integration: `W/m² · 1 h / 1000 = kWh/m²`, applied exactly once; aperture totals
`kWh = Σ_cell kWh/m² × cell area`; averages divide by the cell-covered area.

## 5. Hour accounting

Every hour of the `AnalysisPeriod` lands in exactly one of four buckets, and the last three are
reported on both `CachedIrradianceResult` and `ApertureIrradianceResult`:

| Bucket | Meaning |
|---|---|
| `EvaluatedHours` (`DateTimes.Count`) | Integrated. |
| `MissedBinHours` | Sun position fell outside the cached sun groups. **Always 0** for a consistent cache/timeline pair; a non-zero value is a configuration defect and must be investigated, not ignored. |
| `BelowHorizonHours` | Sun below `minHorizonAngle` (default `Core.Tolerance.Angle` = 2°). Includes all night hours. |
| `MissingWeatherHours` | No weather hour, or a missing raw GHI/DHI field, or degenerate Perez inputs. |

`Evaluated + MissedBin + BelowHorizon + MissingWeather == period hour count`, asserted at every shift
tested (§8).

**Known consequence of the horizon gate.** An hour whose sun is below 2° is skipped *whole* — its
diffuse and ground-reflected energy is not integrated either. This matches the released engine
(`Modify.Simulate` applies the same `minHorizonAngle` gate), and the cost is measured rather than
assumed: on ModelB's real EPW weather at the default +30 min convention it is **0.19 % of annual GHI
and 0.30 % of annual DHI, over 405 hours carrying energy** (§8).

## 6. Weather precedence

`Modify.SimulateApertures` resolves weather as: **supplied `WeatherData` → the model's own
`AnalyticalModelParameter.WeatherData` → `null` return.** The rule lives in the API, not in each
component, so the Stage 10 component turns the `null` into an actionable runtime error rather than
reimplementing the precedence. Pinned by `T4_Supplied_Weather_Takes_Precedence_Over_The_Model_Weather`.

Explicit hours of the year are modelled by `AnalysisPeriod(year, IEnumerable<int> hoursOfYear)` and
exposed by `ExplicitHoursOfYear`, so the agreed Stage 10 rule (explicit HOYs override the
AnalysisPeriod, with a runtime remark) needs no API change.

## 7. Cache identity (invalidation rules)

Both caches are at **schema version 2**. Weather data and `AnalysisPeriod` are deliberately **not**
part of either identity. Sun groups are solar geometry only — the representative direction is the
angular **group centre**, with no DNI weighting — so the direct-beam cache never depends on the
weather file.

`SolarVisibilityCache.GetIdentity()`:

```
schemaVersion(2) ; "CellRaycast" ; contextGeometryHash ; targetGeometryHash ;
gridSize ; sunAngleStep ; minHorizonAngle ;
tolerance_Area ; tolerance_Snap ; tolerance_Angle ; tolerance_Distance ;
latitude ; longitude ; timeZoneOffset ; sunPositionShiftInMinutes ; year ; cellCount
```

`SkyVisibilityCache.GetIdentity()`:

```
schemaVersion(2) ; "PatchRaycast" ; skyPatchSubdivision ; contextGeometryHash ; targetGeometryHash ;
gridSize ; tolerance_Area ; tolerance_Snap ; tolerance_Angle ; tolerance_Distance ; cellCount
```

Two different hashes, for two different reasons:

- **`Query.GeometryHash(occluders)` — ORDER-INDEPENDENT.** Occlusion is a set property: which faces
  block a ray does not depend on the order they were enumerated in, and nothing in either cache is
  indexed by occluder position. Per-face tolerance-rounded vertex sets are sorted, then the face
  hashes are sorted, then SHA-256.
- **`Query.TargetHash(analysisCells)` — ORDER- AND ORIENTATION-SENSITIVE.** The lit bitsets and the
  view-factor arrays are indexed by cell **position**, and a flipped cell normal reverses the
  front-facing test, the incidence angle and every view factor. Each cell contributes its
  tolerance-rounded plane normal plus its vertex rings rotated to start at their lexicographically
  smallest rounded vertex — so a re-wound but geometrically identical polygon does **not** invalidate,
  while a flipped face never collides with the original.

| Change | Reuse? |
|---|---|
| `AnalysisPeriod` | ✅ reuse |
| `WeatherData` (same site, year, convention) | ✅ reuse |
| Occluder enumeration order | ✅ reuse |
| Analysis polygon re-wound (start index shifted) | ✅ reuse |
| Analysis target / cell **order** | ❌ rebuild |
| Analysis face **flipped** | ❌ rebuild |
| Time zone (incl. UTC+05:30 vs UTC+05:00) | ❌ rebuild |
| `SunTimeConvention` / explicit shift | ❌ rebuild |
| Occluder or target geometry, `gridSize`, `sunAngleStep`, tolerances, location, year | ❌ rebuild |
| `recalculate: true` | ❌ rebuild (manual override) |

## 8. Measured validation (this build)

Debug and Release: **77 / 77 passing**, ~1 min 7 s each.

### 8.1 T1 — evaluated-hour conservation across sun-position shifts

Free-standing south-facing 1 m² cell, London, 2018, full year (8760 h), `minHorizonAngle` 2°,
`sunAngleStep` 2°, every hour of weather populated. "Expected" is computed independently from
`SolarTimes`, without touching any cache.

| shift (min) | expected | evaluated | missed group | below horizon | missing weather | sum | GHI dropped by the horizon gate |
|---:|---:|---:|---:|---:|---:|---:|---:|
| −60 | 4186 | 4186 | 0 | 4574 | 0 | 8760 | 2.39 % |
| −30 | 4264 | 4264 | 0 | 4496 | 0 | 8760 | 0.66 % |
| 0 | 4186 | 4186 | 0 | 4574 | 0 | 8760 | 0.21 % |
| **+30 (default, IntervalStart)** | 4264 | 4264 | 0 | 4496 | 0 | 8760 | 0.73 % |
| +60 | 4186 | 4186 | 0 | 4574 | 0 | 8760 | 2.44 % |

Expected == evaluated **exactly** at every shift; nothing is lost to a group miss. ±60 min reproduces
the shift-0 counts because a whole-hour offset maps the hourly sample grid onto itself. The dropped-GHI
column is inflated here by the synthetic year being cloudless on every day, which over-weights low-sun
hours; on ModelB's real EPW weather the same measurement is **0.60 % of GHI / 0.97 % of DHI at shift 0**
and **0.19 % / 0.30 % at the +30 default**.

### 8.2 T2 — east/west symmetry, and its detection power

Unobstructed free-standing 1 m² vertical surfaces, London, 2018, weather driven by
`sin(elevation)` at the sampled instant so the series is symmetric in **solar** time (a clock-time
profile would only be symmetric about 12:00, while solar noon drifts ±16 min with the equation of
time).

| orientation | direct | diffuse | ground | total | sunlit h |
|---|---:|---:|---:|---:|---:|
| north | 105.677 | 166.939 | 161.436 | 434.052 | 722 |
| east | 883.092 | 249.510 | 162.374 | 1294.976 | 2108 |
| south | 1353.597 | 311.572 | 161.436 | 1826.605 | 3464 |
| west | 881.344 | 249.343 | 162.374 | 1293.061 | 2078 |

(kWh/m², synthetic cloudless year — the magnitudes are not a London climate statement.)

**East/west asymmetry: direct 0.198 %, total 0.148 %.** The residual is the hourly sample grid not
being centred on solar noon. Gate: 1 %.

Detection power, same geometry and the same (shift-0 symmetric) weather, sun sampled at a wrong offset:

| sun sampled at | east/west direct asymmetry | east | west |
|---:|---:|---:|---:|
| 0 min (reference) | 0.198 % | — | — |
| −60 min | 100.29 % | 1356.7 | 450.5 |
| −30 min | 58.65 % | 1164.61 | 636.41 |
| +30 min | 58.20 % | 635.41 | 1157.02 |
| +60 min | 100.08 % | 450.0 | 1351.44 |

A half-hour timestamp-midpoint error is ~300× the symmetric residual, and its **sign** is checked too
(sampling late favours the west). Mirroring is caught separately by asymmetric weather
(morning-heavy: east/west = 4.008; afternoon-heavy: west/east = 3.992), azimuth rotation by the
orientation ordering (south > east ≈ west > north) plus direct azimuth assertions
(21 Jun London: 07:00 az 85.6°, 12:00 az 178.9°, 17:00 az 273.6°), and sun-vector direction by a
single-hour lit check (21 Jun 07:00: east cell lit, west cell not).

**The sunlit-hour count is reported but deliberately not used as a symmetry gate.** It is a binary
in-front-of/behind decision quantised onto whole hours, so it measures where the sample grid sits
relative to solar noon and nothing else — the table above shows it *falls* from 1.43 % to 0.38 %
under a 30-minute error. Gating on it would add no protection.

### 8.3 T3 — corrected standalone radiation vs cached irradiation

140 (probe hour × sky condition × orientation) cases: 7 hours spanning winter/equinox/summer and
morning/noon/afternoon, 4 sky conditions spanning the Perez clearness bins from overcast (ε ≈ 1) to
clear (ε > 6), 5 orientations (N/E/S/W/roof).

Three implementations compared: the Plane/outward-normal `Create.Radiation(…, SkyModel.PerezAnisotropic)`,
`Query.CachedIrradiance`, and an **independent** Perez 1990 implementation written from the paper
inside the test file (its own clearness/brightness formulation and F1/F2 table).

| comparison | worst absolute disagreement |
|---|---|
| standalone vs cached, direct | 4.547e-13 W/m² |
| standalone vs cached, diffuse | 5.684e-14 W/m² |
| standalone vs cached, ground | 0 W/m² |
| both vs the independent Perez implementation (total) | 2.274e-13 W/m² |

This exercises DNI reconstruction, incidence angle, the direct term, F1/F2 composition, the
isotropic, circumsolar, horizon-brightening and ground-reflected terms, and the W/m² → kWh/m² unit
conversion. The **legacy** overload is explicitly not used as the reference; its disagreement is
pinned instead (summer noon, south vertical: corrected beam 376.45 W/m², legacy 7.03 W/m²).

Ray-cast Tregenza-145 view factors against the closed form (single hour, 700/200 W/m²):

| orientation | SVF analytic | SVF Tregenza | GVF analytic | GVF Tregenza | HVF | diffuse Δ | ground Δ |
|---|---:|---:|---:|---:|---:|---:|---:|
| north | 0.5 | 0.4979 | 0.5 | 0.4979 | 1 | 0.187 % | 0.418 % |
| east | 0.5 | 0.5008 | 0.5 | 0.5008 | 1 | 0.070 % | 0.161 % |
| south | 0.5 | 0.4979 | 0.5 | 0.4979 | 1 | 0.084 % | 0.418 % |
| west | 0.5 | 0.5008 | 0.5 | 0.5008 | 1 | 0.072 % | 0.161 % |
| roof | 1.0 | 1.0055 | 0 | 0 | 1 | 0.154 % | — |

The 145-patch quadrature reproduces the closed-form view factors to better than 1 % (roof SVF
overshoots to 1.0055 — a quadrature artefact of the patch set, not an obstruction effect). Direct
beam never passes through the sky cache and is bit-identical with or without it.

### 8.4 T4 — cache identity

All negative and positive cases in the §7 table are asserted. Selected evidence:

- reordered analysis cells: target hash `97aa1cc2…` vs `b58919d3…` → no match;
- flipped analysis face: target hash `06074177…` vs `46490b95…` → no match, and the flip is
  load-bearing (215 lit (group × cell) pairs south-facing vs 25 flipped);
- time zone: identity `…;51.5074;-0.1278;0;0;2018;1` vs `…;51.5074;-0.1278;5.5;0;2018;1` → no match,
  and UTC+05:30 does not collide with UTC+05:00;
- reordered occluders with **different** `LinkedFace3D` Guids: identical context hash → reuse;
- re-wound analysis polygon: identical target hash → reuse.

### 8.5 B6 — genuine DNI in `DirectSolarRadiation`

Same weather (GHI/DHI from a `sin(elevation)` profile) in three variants: no `DirectSolarRadiation`
field, the field populated with a **genuine DNI**, and the field populated with a 9999 sentinel.
Free-standing south 1 m², full year:

| variant | direct | diffuse | ground |
|---|---:|---:|---:|
| no direct field | 1308.8754 | 273.0351 | 152.6685 |
| genuine DNI in the field | 1308.8754 | 273.0351 | 152.6685 |
| 9999 sentinel in the field | 1308.8754 | 273.0351 | 152.6685 |

Bit-identical (asserted to 12 decimal places). The legacy path is unaffected and still consumes the
field: legacy beam 4.3927 W/m² with the field absent (derived 500 W/m²) vs 1.0806 W/m² with the field
set to 123 W/m² — exactly the 123/500 ratio.

### 8.6 ModelB annual per-aperture averages (13 apertures, 0.5 m grid, attached EPW weather)

| orientation | corrected (IntervalStart +30, this build) | pre-correction (shift 0, `Angle.Degrees` rounding present) |
|---|---:|---:|
| south | **767.6** | 778.9 |
| north | **350.0** | 357.6 |
| east | **615.3** | 736.5 |
| west | **630.9** | 547.3 |

kWh/m². The old numbers are superseded and are shown only to record the size of the correction. The
east/west pair is the diagnostic one: it was 736.5 / 547.3 — a 30 % split with no physical
justification on this near-symmetric building — and is now 615.3 / 630.9, consistent with the
independent T2 symmetry result.

Shaded vs unshaded whole-model total: **18 646 / 19 745 kWh = 94.4 %**.

### 8.7 Sun-group angular resolution (`sunAngleStep`) bias

Synthetic south window + overhang, 4186 daylight hours, compared per-hour against the exact sampled
`Simulate_Coverage` baseline (1651 ms for 8760 hours):

| `sunAngleStep` | groups | build (ms) | MAE | DNI-weighted MAE |
|---:|---:|---:|---:|---:|
| 1° | 1336 | 1557 | 0.0125 | 0.0116 |
| **2° (default)** | **666** | **1587** | **0.0154** | **0.0145** |
| 5° | 240 | 1340 | 0.0293 | 0.0298 |

The 2° default stays within the plan's 2 % accuracy gate. Build time is dominated by the per-group
geometric pass, so it does not fall linearly with group count on a model this small.

### 8.8 Performance and reuse (ModelB, 13 apertures, 135 cells @ 0.5 m, 36 occluders)

| operation | measured |
|---|---:|
| first call — build both caches + evaluate a full year | 2607 ms |
| second call — different `AnalysisPeriod`, caches reused | 298 ms |

Reuse behaviour end-to-end, on the model: `AnalysisPeriod` change → reused; `WeatherData` swap →
reused; `gridSize` change → rebuild; `recalculate: true` → rebuild; `SunTimeConvention` change →
rebuild.

### 8.9 SAM-vs-TAS regression

`WithShade_SAM_matches_TAS_within_tolerance`: 36 surfaces matched 1:1, 5371 overlapping hours,
**overall mean absolute delta 0.00880** (gate < 0.02). Unchanged by this batch — the coverage
benchmark does not go through the new radiation path.

### 8.10 Other invariants

- Conservation: complementary half-year periods sum to the full year to within 1e-9 relative.
- Component isolation: a small plate over the midday solstice sun — isotropic diffuse ratio 0.803,
  Perez diffuse ratio 0.598 (the circumsolar term is removed by the lit bit, not by SVF scaling).
- Grid convergence: 0.25 m vs 0.5 m aperture totals differ by less than 2 %.

## 9. Known limitations (Stage 0–4 scope)

- No inter-reflection between surfaces (context blocks, never bounces).
- Isotropic ground; the ground view factor is a cosine-weighted patch sum.
- Hourly weather only. `AnalysisPeriod.Timestep != 1` now **throws at construction** rather than
  being silently ignored.
- Sun groups are built per (location, year, convention); a period outside the cache year is re-rooted
  by `SimulateApertures`, preserving hour-of-year structure.
- The 2° `minHorizonAngle` gate skips near-horizon hours whole, including their diffuse and
  ground-reflected energy — measured at 0.19 % of annual GHI on ModelB (§5, §8.1).
- The Tregenza-145 quadrature reproduces analytic view factors to ~0.5 %, and can slightly exceed 1.0
  for an unobstructed up-facing cell (§8.3).
- Interior partitions (panels shared by two spaces) are excluded from the context, as in the existing
  engine; a neighbouring building must be modelled as shade panels to occlude. Apertures on two-space
  panels are rejected even when explicitly selected.
- Group quantisation means any future per-element attribution inherits `sunAngleStep` and `gridSize`
  resolution (§10).

## 10. Forward compatibility with the Stage 8 first-hit requirement

The PR #12 requirement to retain *which* shading element intercepted a direct solar path was checked
against the structures built here. **Nothing in the Stage 0–4 design discards information that would
be expensive to recover**, and the change is localised:

- `Weather.SolarCalculator.Query.CellVisibility` already calls
  `Geometry.Object.Spatial.Query.IntersectionTuples(segment3D, candidates, false, tolerance)` on
  `LinkedFace3D` candidates that carry their `Guid`. That helper's third argument is `sort`: passing
  `true` returns the intersections nearest-first, so `tuples[0].Item1.Guid` **is** the first-hit
  element. Today the method returns `bool[]` and throws that identity away at the last step.
- Retaining it means returning `int[]` (an index into a per-cache occluder table, sentinel for
  visible) instead of `bool[]`, and widening `SolarVisibilityCache`'s `ulong[][]` bitset to an index
  array. That is a storage and signature change, not an algorithmic one.
- Two constraints are inherent rather than introduced here, and match §2.5 of the plan: attribution
  needs the **ray path**, so it is only available in the ray-cast (cell) mode and not in the exact
  polygon-clipping mode; and it lives on the analysis grid, so an element narrower than `gridSize`
  will be under-attributed.
- One additional constraint this implementation adds: first-hit identity would be recorded **per sun
  group**, not per hour, so attribution resolution is bounded by `sunAngleStep` as well. At the 2°
  default that is the same quantisation already measured in §8.7.

No action is required now; this is recorded so the Stage 5–8 work does not have to rediscover it.

## 11. Compatibility statement

| Kind | Status |
|---|---|
| Binary | **No released public signature removed, renamed or changed.** The legacy `Geometry.SolarCalculator.Create.Radiation(SolarTimes, double tilt, double surfaceAzimuth, …)` overload, the misspelled `calctulateRadiation` parameter on all three `Modify.Simulate` overloads, `Weather.SolarCalculator.Create.Radiation(WeatherData, DateTime, Plane, …)` and `Query.SunDirection(Location, DateTime, bool)` all keep their exact signatures. |
| Source | No existing enum value renumbered. New enums (`SkyModel`, `SkyPatchSubdivision`, `SunTimeConvention`, `AnalysisPeriodPreset`) are additive. |
| Behavioural | Mostly unchanged — with four **deliberate defect fixes** listed below. |

Behavioural changes to *released* code, all of them intentional fixes with regression tests, none of
them a convention change:

1. **Fractional time zones are no longer truncated.** `Query.SunDirection(Location, DateTime, bool)`
   and `Weather.SolarCalculator.Create.Radiation(WeatherData, …)` used
   `System.Convert.ToInt32(Core.Query.Double(uTC))`. `Convert.ToInt32(double)` rounds to the nearest
   integer with ties resolved to even (banker's rounding), not truncation, so it turned UTC+05:30
   into **UTC+06:00** (5.5 ties to the even neighbour, 6), not UTC+05:00 as originally stated here —
   the **± 30-minute error magnitude is unchanged**, only its direction. Both call sites now route
   through `Create.SolarTimes(Location, DateTime)`, which carries the fractional offset. **Results
   are bit-identical for every whole-hour time zone** and change only where the old value was wrong.
   Pinned by `SunDirection_FractionalTimeZone_Preserved`.
2. **`Modify.Simulate(…, merge: true)` returns the merged results.** It previously built the merged
   list, attached it, and then returned the *un-merged* one. Pinned by
   `Simulate_MergeTrue_Returns_Merged_Results`.
3. **`SunExposureFace3Ds` returns null instead of throwing** when the plane search yields nothing.
   Pinned by `SunExposureFace3Ds_NullPlane_Returns_Null` (a genuinely degenerate/collinear `Face3D`
   whose `GetPlane()` returns null; the earlier, non-throwing empty-list guard is a separate case,
   pinned by `SunExposureFace3Ds_NullExposureList_Returns_Null`).
4. **An unresolved Location timezone is no longer silently treated as UTC+00:00.**
   `Geometry.SolarCalculator.Query.TimeZoneOffset(Location)` now returns `double.NaN` when the
   Location has no TimeZone parameter, or the TimeZone string does not resolve to a known
   `SAM.Core.UTC` value — previously it returned `0`, indistinguishable from a genuine UTC+00:00.
   `Create.SolarTimes(Location, DateTime)` detects the NaN and returns null;
   `Modify.SimulateApertures` detects it explicitly and returns null rather than running the
   geometric pass against a fabricated Greenwich offset. Genuine UTC+00:00 (including the ordinarily-
   written `"UTC+00:00"` / `"UTC-00:00"` strings, which `SAM.Core.Query.UTC(string)` does not itself
   recognise — see below) still resolves to `0.0`. Pinned by `TimeZoneOffset_GenuineUTC0_Is_Accepted_Not_NaN`
   and `TimeZoneOffset_Unresolvable_Returns_NaN_And_SolarTimes_Fails_Safely`.

   Two SAM.Core gaps were found while implementing this and are **not fixed in this repository**
   (SAM.Core is a separate repository/build, referenced here as a prebuilt `SAM.Core.dll`):
   - `SAM.Core.Query.UTC(double)` / `Query.Double(UTC)` omit `UTC.Minus0900` (UTC−09:00) despite the
     enum member existing — an Alaska-style EPW location resolves to `Undefined` instead. Worked
     around locally in `Query.TimeZoneOffset` by normalising the ordinary `"UTC+00:00"` / `"UTC-00:00"`
     strings before calling into SAM.Core (see next point), but the −09:00 case has no such
     workaround here since it is a genuinely missing value, not a formatting mismatch — a real
     UTC−09:00 Location still resolves to NaN until SAM.Core adds the mapping.
   - `SAM.Core.Query.UTC(string)` only recognises the zero offset written with the Unicode plus-minus
     sign (`UTC.PlusMinus0000`'s `Description`, `"UTC±00:00"`) — the ordinarily-written `"UTC+00:00"`
     / `"UTC-00:00"` do not match and fall through to `Undefined`. `Query.TimeZoneOffset` normalises
     these two specific strings to `"UTC±00:00"` before calling `Core.Query.UTC`, so this repository
     is not affected, but any other SAM.Core caller passing an ordinarily-written zero-offset string
     directly to `Core.Query.UTC` still gets `Undefined`.

   Both are prepared as a local commit on branch `fix/utc-minus-0900-mapping` in the sibling SAM.Core
   working copy (not pushed, not part of this PR) — see that commit for the exact diff.

What is explicitly **not** changed: the legacy isotropic radiation formula (inward-normal tilt, +90°
azimuth rotation) and its consumption of `CalculatedDirectSolarRadiation()`. Both are frozen by
golden-value tests. The corrected physical conventions live only in the new `Plane`-based overload
and the cache evaluation path.

Types and members introduced **within this PR** (`ApertureIrradianceResult`, `SimulateApertures`,
`ApertureSolarTargets`, the caches) are not yet released, so they were renamed freely to the agreed
engineer-facing vocabulary in this batch — including `Create.Radiation(SolarTimes, Plane, …,
SkyModel, …)`'s `skyViewFactor`/`groundViewFactor` parameters, renamed to
`skyViewFactorMultiplier`/`groundViewFactorMultiplier` (source-only; no numerical change) to make
their RELATIVE-multiplier semantics explicit against the cache path's ABSOLUTE view factors (§4).
That freedom ends when this PR merges.

## 12. Appendix — traps found the hard way

Kept because each of these cost real time and none of them is visible from the code that suffers from
them.

- **`Innovative.Geometry.Angle.Degrees` rounds to whole degrees.** `Angle.Radians` is full precision.
  Every sun-angle read must use `Radians`. This is the single easiest way to reintroduce a ±0.5°
  error into group lookup, incidence and the Perez inputs.
- **Two sun-angle derivations that "obviously agree" are not bit-identical.** `SunDirection` →
  `TryGetSunAngles` and reading `SolarTimes` directly differ in the last bits, which is enough to put
  an hour on the wrong side of a threshold. Use the single shared
  `Query.TryGetSunAngles(Location, DateTime)`.
- **`WeatherHour.Calculated*SolarRadiation()` are not neutral accessors** — each falls back to a
  *different* field. Read the raw properties when the derivation must be unambiguous (§3.2).
- **`Core.Tolerance.Angle` is 0.0349066 rad = 2°**, not an epsilon. It was the default
  `minHorizonAngle` throughout the library, so "the default tolerance" silently discards the lowest
  2° of sky. `SimulateApertures`'s `minHorizonAngle` default is now the independently-named
  `Modify.DefaultMinHorizonAngle` (same 0.0349066 rad value, zero numerical change) precisely so a
  future change to the generic geometry tolerance cannot silently retune this physics gate; the
  pre-existing `Modify.Simulate` / `Create.SunBins` overloads elsewhere in the library still alias
  `Core.Tolerance.Angle` directly and were left unchanged (out of scope for this correction batch).
- **Windows PowerShell 5.1 `Get-Content` / `Set-Content` corrupts UTF-8** (ANSI round-trip mojibake;
  already repaired once, commit `cfdc393`). Use the editor tooling, or
  `[IO.File]::ReadAllText(p, UTF8)` / `WriteAllText(p, s, New-Object UTF8Encoding($false))`.
  PowerShell here-strings also do not survive being passed to `git commit -m`; use `git commit -F`.
- **Namespace shadowing.** Inside `SAM.Core.SolarCalculator` the local `Query` partial shadows
  `SAM.Core.Query` (use `Core.Query.FullTypeName`); `Convert` is shadowed by the SAM `Convert`
  classes in several projects (use `System.Convert`); and in
  `Weather.SolarCalculator.Create` the `SolarVisibilityCache` *method* shadows the *type* (use
  `global::SAM.Weather.SolarCalculator.SolarVisibilityCache`).
- **`SAM.Geometry.SolarCalculator.csproj` used to exclude `Enums/**` from compile.** That exclusion
  was removed; do not reinstate it. The main libraries target netstandard2.0, so no
  `Math.Round → long` implicit casts and no newer `string.Join` overloads.
- **Test fixtures.** `ModelB-NoShadeSolarSimulation.sam` (13 apertures, carries `WeatherData`);
  `ModelB-WithShadeSolarSimulation.sam` (same building translated +25 m in X, **no** weather — attach
  the NoShade model's `WeatherData`). Load via `SAM.Core.Convert.ToSAM<AnalyticalModel>(path)`.
