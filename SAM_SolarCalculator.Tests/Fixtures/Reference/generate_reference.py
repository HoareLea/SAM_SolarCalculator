# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors
#
# INDEPENDENT REFERENCE GENERATOR -- Stage 11 Gate 1.
#
# Run ONCE, OFFLINE, with the Ladybug Tools Python. Ladybug is NOT a runtime or test dependency of
# SAM: this script writes reference-results.json, that file is committed, and the C# test
# IndependentReferenceTests asserts against it. Nothing in CI runs Python.
#
#   & "C:\Program Files\ladybug_tools\python\python.exe" generate_reference.py
#
# WHAT IS AND IS NOT INDEPENDENT HERE.
#   INDEPENDENT: the solar position. Ladybug's Sunpath is a completely separate implementation of
#     solar geometry (its own equation of time, declination and hour angle) from SAM's. Agreement
#     between them is real evidence about both.
#   INDEPENDENT: the transposition geometry. cos(incidence) is recomputed here from Ladybug's own
#     sun vectors and the surface normals, and the annual sum is accumulated independently.
#   SHARED BY DESIGN: the GHI/DHI series, which is exported from the model so both sides read
#     byte-identical radiation. Comparing two different weather files would measure the files.
#   SHARED BY DESIGN: the beam-normal decomposition rule DNI = max(0, GHI - DHI) / sin(altitude).
#     That is SAM's documented modelling choice, so it is applied identically here to isolate the
#     geometry. Its low-sun clamp IS measured separately (see "unclamped" below), which is the
#     honest way to report a modelling choice rather than validate it against itself.
#
# NOT COVERED HERE: diffuse and ground-reflected transposition. Ladybug's Python API exposes no
# Perez tilted-surface transposition (ladybug.skymodel provides decomposition -- DISC/DIRINT -- not
# transposition), and the Radiance gendaymtx route validates a sky matrix rather than SAM's own
# Perez implementation, so it would not be comparing like with like. Recorded as a remaining gap.

import csv
import json
import math
import os
import sys
from datetime import datetime

from ladybug.location import Location
from ladybug.sunpath import Sunpath

HERE = os.path.dirname(os.path.abspath(__file__))


def read_site():
    site = {}
    with open(os.path.join(HERE, "site.csv"), newline="") as handle:
        for row in csv.DictReader(handle):
            site[row["key"]] = row["value"]
    return site


def read_hours():
    rows = []
    with open(os.path.join(HERE, "hours.csv"), newline="") as handle:
        for row in csv.DictReader(handle):
            rows.append({
                "hoy": int(row["hourOfYear"]),
                "ghi": float(row["globalHorizontal_Wm2"]),
                "dhi": float(row["diffuseHorizontal_Wm2"]),
                "sam_alt": float(row["samAltitude_deg"]),
                "sam_azi": float(row["samAzimuth_deg"]),
            })
    return rows


def read_apertures():
    rows = []
    with open(os.path.join(HERE, "sam-apertures.csv"), newline="") as handle:
        for row in csv.DictReader(handle):
            rows.append({
                "guid": row["apertureGuid"],
                "azimuth": float(row["azimuth_deg"]),
                "tilt": float(row["tilt_deg"]),
                "gross_area": float(row["grossArea_m2"]),
                "sampled_area": float(row["sampledArea_m2"]),
                "sam_direct_kwh": float(row["directEnergy_kWh"]),
            })
    return rows


def surface_normal(azimuth_deg, tilt_deg):
    """Outward normal in SAM's frame: X east, Y north, Z up. Azimuth clockwise from north."""
    a = math.radians(azimuth_deg)
    t = math.radians(tilt_deg)
    return (math.sin(a) * math.sin(t), math.cos(a) * math.sin(t), math.cos(t))


def sun_vector(altitude_deg, azimuth_deg):
    """Unit vector from the surface toward the sun, same frame."""
    alt = math.radians(altitude_deg)
    azi = math.radians(azimuth_deg)
    return (math.cos(alt) * math.sin(azi), math.cos(alt) * math.cos(azi), math.sin(alt))


def main():
    site = read_site()
    hours = read_hours()
    apertures = read_apertures()

    latitude = float(site["latitude"])
    longitude = float(site["longitude"])
    time_zone = float(site["timeZoneOffsetHours"])
    year = int(site["year"])
    shift_minutes = float(site["sunPositionShiftMinutes"])
    min_elevation_deg = float(site["minimumSinElevationDegrees"])

    location = Location(site.get("name", "site"), latitude=latitude, longitude=longitude,
                        time_zone=time_zone, elevation=float(site["elevation"]))
    sunpath = Sunpath.from_location(location)

    print("site: %s  lat %.4f  lon %.4f  tz %+.2f  year %d" % (
        site.get("name", ""), latitude, longitude, time_zone, year))
    print("hours: %d   apertures: %d" % (len(hours), len(apertures)))

    # ---------------------------------------------------------------- sun position ----
    alt_errors = []
    azi_errors = []
    compared = 0
    lb_altitude = {}
    lb_azimuth = {}

    for row in hours:
        # Ladybug's hour-of-year and SAM's are the same convention: hoy 0 is 1 Jan 00:00 local
        # standard time. The half-hour shift is SAM's interval-start convention and is applied on
        # both sides so the two are placed at the same instant.
        hoy = row["hoy"] + shift_minutes / 60.0
        sun = sunpath.calculate_sun_from_hoy(hoy)
        lb_altitude[row["hoy"]] = sun.altitude
        lb_azimuth[row["hoy"]] = sun.azimuth

        if math.isnan(row["sam_alt"]):
            continue

        alt_errors.append(sun.altitude - row["sam_alt"])

        # Azimuth is only meaningful when the sun is up, and wraps at 360.
        if sun.altitude > 0.0:
            d = (sun.azimuth - row["sam_azi"] + 180.0) % 360.0 - 180.0
            azi_errors.append(d)

        compared += 1

    def stats(values):
        if not values:
            return None
        n = len(values)
        mean = sum(values) / n
        mae = sum(abs(v) for v in values) / n
        rmse = math.sqrt(sum(v * v for v in values) / n)
        return {"count": n, "bias": mean, "mae": mae, "rmse": rmse,
                "max_abs": max(abs(v) for v in values)}

    sun_position = {
        "altitude_deg": stats(alt_errors),
        "azimuth_deg_sun_above_horizon": stats(azi_errors),
    }

    print("sun altitude: MAE %.6f deg, max %.6f deg over %d hours" % (
        sun_position["altitude_deg"]["mae"], sun_position["altitude_deg"]["max_abs"], compared))
    print("sun azimuth : MAE %.6f deg, max %.6f deg" % (
        sun_position["azimuth_deg_sun_above_horizon"]["mae"],
        sun_position["azimuth_deg_sun_above_horizon"]["max_abs"]))

    # ------------------------------------------------- direct beam on each surface ----
    min_sin = math.sin(math.radians(min_elevation_deg))

    aperture_results = []
    for aperture in apertures:
        normal = surface_normal(aperture["azimuth"], aperture["tilt"])

        clamped = 0.0    # SAM's rule, using Ladybug's sun positions
        unclamped = 0.0  # the same, without the low-sun clamp, to measure what the clamp costs

        for row in hours:
            altitude = lb_altitude[row["hoy"]]
            if altitude <= 0.0:
                continue

            beam_horizontal = max(0.0, row["ghi"] - row["dhi"])
            if beam_horizontal <= 0.0:
                continue

            s = sun_vector(altitude, lb_azimuth[row["hoy"]])
            cos_incidence = normal[0] * s[0] + normal[1] * s[1] + normal[2] * s[2]
            if cos_incidence <= 0.0:
                continue

            sin_alt = math.sin(math.radians(altitude))
            clamped += (beam_horizontal / max(sin_alt, min_sin)) * cos_incidence / 1000.0
            unclamped += (beam_horizontal / sin_alt) * cos_incidence / 1000.0

        # SAM reports an ENERGY over the sampled area, so the reference is scaled the same way.
        area = aperture["sampled_area"]
        reference_kwh = clamped * area
        unclamped_kwh = unclamped * area
        sam_kwh = aperture["sam_direct_kwh"]

        aperture_results.append({
            "guid": aperture["guid"],
            "azimuth_deg": aperture["azimuth"],
            "tilt_deg": aperture["tilt"],
            "sampled_area_m2": area,
            "sam_direct_kwh": sam_kwh,
            "reference_direct_kwh": reference_kwh,
            "reference_direct_unclamped_kwh": unclamped_kwh,
            "absolute_error_kwh": sam_kwh - reference_kwh,
            "relative_error_percent": 100.0 * (sam_kwh - reference_kwh) / reference_kwh if reference_kwh else None,
            "low_sun_clamp_effect_percent": 100.0 * (reference_kwh - unclamped_kwh) / unclamped_kwh if unclamped_kwh else None,
        })

        print("az %5.0f  SAM %9.2f kWh   reference %9.2f kWh   %+7.3f %%   (clamp effect %+.3f %%)" % (
            aperture["azimuth"], sam_kwh, reference_kwh,
            aperture_results[-1]["relative_error_percent"],
            aperture_results[-1]["low_sun_clamp_effect_percent"]))

    # --------------------------------------------------------- horizontal reference ----
    # No horizontal aperture exists in the fixture, so this validates the transposition on a
    # horizontal plane against the measured GHI itself: the beam component of a horizontal surface
    # must reproduce the beam-horizontal series it was derived from. It is a closure check on the
    # decomposition rather than a second opinion about it, and is labelled as such.
    horizontal_beam = sum(max(0.0, r["ghi"] - r["dhi"]) for r in hours) / 1000.0
    horizontal_from_dni = 0.0
    for row in hours:
        altitude = lb_altitude[row["hoy"]]
        if altitude <= 0.0:
            continue
        beam_horizontal = max(0.0, row["ghi"] - row["dhi"])
        if beam_horizontal <= 0.0:
            continue
        sin_alt = math.sin(math.radians(altitude))
        horizontal_from_dni += (beam_horizontal / max(sin_alt, min_sin)) * sin_alt / 1000.0

    # ------------------------------------------- what the low-sun clamp is actually for ----
    # The clamp is not an approximation that loses accuracy; it is a guard on a DERIVED quantity.
    # DNI = beam_horizontal / sin(altitude) is exact only if beam_horizontal is exact, and as the
    # sun approaches the horizon the division amplifies ordinary weather-file noise without limit.
    # Measuring the largest unclamped DNI the file would produce says whether the guard is earning
    # its keep: the solar constant is about 1361 W/m2 and nothing at ground level can exceed it.
    SOLAR_CONSTANT = 1361.0
    max_unclamped_dni = 0.0
    max_clamped_dni = 0.0
    hours_above_solar_constant = 0
    hours_below_min_elevation_with_beam = 0

    for row in hours:
        altitude = lb_altitude[row["hoy"]]
        if altitude <= 0.0:
            continue
        beam_horizontal = max(0.0, row["ghi"] - row["dhi"])
        if beam_horizontal <= 0.0:
            continue

        sin_alt = math.sin(math.radians(altitude))
        unclamped_dni = beam_horizontal / sin_alt
        clamped_dni = beam_horizontal / max(sin_alt, min_sin)

        max_unclamped_dni = max(max_unclamped_dni, unclamped_dni)
        max_clamped_dni = max(max_clamped_dni, clamped_dni)

        if unclamped_dni > SOLAR_CONSTANT:
            hours_above_solar_constant += 1
        if altitude < min_elevation_deg:
            hours_below_min_elevation_with_beam += 1

    clamp = {
        "rule": "DNI = max(0, GHI - DHI) / max(sin(altitude), sin(%g deg))" % min_elevation_deg,
        "max_unclamped_dni_wm2": max_unclamped_dni,
        "max_clamped_dni_wm2": max_clamped_dni,
        "solar_constant_wm2": SOLAR_CONSTANT,
        "hours_where_unclamped_dni_exceeds_solar_constant": hours_above_solar_constant,
        "hours_with_beam_below_minimum_elevation": hours_below_min_elevation_with_beam,
    }

    print("low-sun clamp: unclamped DNI peaks at %.0f W/m2 (solar constant %.0f); %d hours would exceed it" % (
        max_unclamped_dni, SOLAR_CONSTANT, hours_above_solar_constant))

    result = {
        "generated_utc": datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
        "tool": "Ladybug Tools",
        "tool_python": sys.version.split()[0],
        "ladybug_core_sunpath": "ladybug.sunpath.Sunpath",
        "procedure": (
            "Sun positions recomputed independently by ladybug.sunpath.Sunpath at hour-of-year + "
            "30 min. Direct beam on each surface accumulated independently from those sun vectors "
            "and the exported GHI/DHI series, using SAM's documented DNI decomposition so that the "
            "comparison isolates solar geometry and transposition. The low-sun clamp is reported "
            "separately as a measured modelling effect, not validated against itself."
        ),
        "site": {
            "name": site.get("name"),
            "latitude": latitude,
            "longitude": longitude,
            "time_zone_hours": time_zone,
            "elevation_m": float(site["elevation"]),
            "year": year,
            "weather_hours": len(hours),
            "sun_position_shift_minutes": shift_minutes,
            "minimum_elevation_deg": min_elevation_deg,
        },
        "sun_position_error": sun_position,
        "apertures": aperture_results,
        "low_sun_clamp": clamp,
        "horizontal_beam_closure": {
            "measured_beam_horizontal_kwh_m2": horizontal_beam,
            "reconstructed_from_dni_kwh_m2": horizontal_from_dni,
            "relative_error_percent": 100.0 * (horizontal_from_dni - horizontal_beam) / horizontal_beam,
        },
        "not_covered": [
            "Diffuse-sky transposition (Perez on a tilted surface): Ladybug's Python API exposes "
            "decomposition, not transposition, so no like-for-like independent comparison is "
            "available from this reference. Remains a manual external check.",
            "Ground-reflected transposition, for the same reason.",
            "Context-obstructed apertures: the reference has no geometry engine here, so shading by "
            "surrounding surfaces is validated analytically in C# instead.",
        ],
    }

    out = os.path.join(HERE, "reference-results.json")
    with open(out, "w") as handle:
        json.dump(result, handle, indent=2)

    print("wrote %s" % out)


if __name__ == "__main__":
    main()
