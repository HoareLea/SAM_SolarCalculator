# SAM.SolarCalculator.Tests

Macro / integration tests (xUnit, **.NET 8**) that exercise the solar-coverage
pipeline against **real exported AnalyticalModels**, not synthetic data. They
guard the SAM-vs-TAS solar benchmark: if a future change to the import, aperture
handling, surface selection or alignment quietly breaks the agreement, a test
fails so the regression is caught.

> These tests protect the **code**, not your day-to-day Grasshopper modelling.
> They only matter when someone modifies the solar-calculation code.

## How to run

From the repository root, or anywhere:

```bash
dotnet test SAM_SolarCalculator/SAM_SolarCalculator.Tests
```

Runs in ~1 second. **Green = the validated behaviour still holds.** A failure
prints which assertion broke (e.g. surface counts, or the SAM-vs-TAS delta).

## What is covered

| Test | Asserts |
|------|---------|
| `Fixtures_have_mismatched_surface_sets` | The original asymmetric case: TAS = 36 surfaces, SAM = 8 (motivates the rest). |
| `UseModelSolarModel_recomputes_coverage_on_the_same_surfaces` | `SolarSimulation(_useModelSolarModel_=true)` recomputes on the TAS model's exact 36 surfaces, geometry preserved 1:1. |
| `ClassifyPanels_accounts_for_every_Model_A_surface` | The "why are surfaces dropped" diagnostic accounts for every Model A surface. |
| `ToSAM_SolarModel_includeApertures_matches_TAS_surface_set_one_to_one` | Coverage path includes window apertures → 36 surfaces (8 + 14 openings + 14 panes); the default panels-only path stays at 8. |
| `Default_path_now_covers_the_full_TAS_equivalent_surface_set` | A standalone coverage run produces a result for all 36 surfaces. |
| `WithShade_SAM_matches_TAS_within_tolerance` | **The engine benchmark.** With matched shading, after aligning the models, all 36 TAS surfaces match SAM 1:1, the 28 `PanelType.Shade` occluders stay unmatched, and `overallMeanAbsDelta < 0.02` (the validated live run measured **0.0088** ≈ 0.9% agreement). |
| `KolobrzegRegressionTests.Fixture_Loads_With_The_Three_Studied_Apertures_And_Their_Expected_Geometry` | **Real-project fixture integrity.** The Kołobrzeg office model loads, carries the three studied aperture GUIDs with their expected geometry (~0.90 × 2.25, ~1.20 × 1.39, ~0.60 × 1.39 m) and reproduces the historical 10 / 9 / 6-cell pattern at the 0.5 m default grid. |
| `KolobrzegRegressionTests.The_Half_Metre_Default_Is_Coarser_Than_The_Recommended_Grid_And_Nothing_Is_Changed_By_The_Guidance` | PR 2 guidance on the real set: the recommendation is 0.25 m from the actual geometry, one shared number governed by the smallest aperture; 0.5 m warns as coarser; the supplied grid and calculation are untouched. |
| `KolobrzegRegressionTests.The_Smallest_Aperture_At_Half_A_Metre_Is_A_Coarse_Grid_Sampling_Artefact` (LongRunning) | **The coarse-grid artefact.** The smallest aperture's 0.5 m winner (a thin single vertical fin claiming ~88 % blocked) is re-measured on finer grids where the same physical device blocks ~0 % — a plausible-looking recommendation that was a sampling artefact. |
| `KolobrzegRegressionTests.Family_Recommendations_Are_Stable_At_Design_Grade_Grids_Where_The_Coarse_Grid_Differs` (LongRunning) | **Family/decision stability.** Across 0.5 / 0.25 / 0.2 m, the small aperture's winner family differs at the coarse grid and stabilises at HorizontalLouvres from 0.25 m; the mid and tall apertures stay HorizontalLouvres throughout. Exact geometry is deliberately not asserted for the tall aperture. |
| `KolobrzegRegressionTests.The_Coarse_Grid_Winner_Rides_The_Resolution_Cap_And_Carries_The_Guidance_Warning` (LongRunning) | **PR 2 resolution-cap warning, natural case.** The 0.5 m winner genuinely rides the grid-narrowed count maximum (1) and triggers the cap warning; the fine-grid winners sit below their caps and carry none. |

## Fixtures (`*.sam`)

Real models exported from SAM, stored in SAM's native compressed `.sam` (zip)
format — ~40× smaller than raw JSON, loaded via `Convert.ToSAM<AnalyticalModel>`.
(Only `.sam` is auto-detected; a `.zip` or extension-less file will **not** load.)

| File | What it is |
|------|------------|
| `ModelA.sam` / `ModelB-SolarSimulation.sam` | Original asymmetric case (TAS shaded vs SAM with no shading context). |
| `ModelA-NoShade.sam` / `ModelB-NoShadeSolarSimulation.sam` | No-shade-both-sides control pair. |
| `ModelA-WithShade.sam` | TAS import (`FromTBD`, `_importSurfaceShades_=true`), 36 surfaces, source `TAS`. |
| `ModelB-WithShadeSolarSimulation.sam` | SAM `SolarSimulation` output, 64 surfaces (8 + 28 windows + 28 Shade), drawn ~+25 m away. |
| `KolobrzegOffice.sam` | **A real office project in Kołobrzeg, Poland** (weather `POL_ZP_Kolobrzeg`, IMGW, embedded). A real-project validation fixture, not a synthetic scenario: it preserves the grid-resolution study's apertures (0.90 × 2.25 m, 1.20 × 1.39 m, 0.60 × 1.39 m on one WSW façade) and the coarse-grid sampling artefact they exposed. It must **not** be used to tune the optimiser, grid rules or guidance constants specifically to this project. |

To refresh a fixture, re-export the relevant node and **save with the `.sam`
extension**. The Model A side must come straight from `FromTBD(_importSurfaceShades_=true)`
(source `TAS`), **not** through `SolarSimulation`.

## CI (current status & best practice)

These tests are **not yet run automatically on PRs** — `build.yml` only *builds*
the solutions. They run only when invoked with `dotnet test`.

**Recommended next step:** add a `dotnet test` step to `.github/workflows/build.yml`
so the suite runs on every PR and a regression turns the build red. Sketch:

```yaml
      - name: Test
        run: dotnet test SAM_SolarCalculator/SAM_SolarCalculator.Tests -c Debug --nologo
```

Until then, run `dotnet test` locally before merging any change to the solar code.
