# Orbital Payload Calculator

<div align="center">
    
<img src="https://i.imgur.com/ryDQSmm.jpg" alt="Banner"/>
    
[![License](https://img.shields.io/github/license/Aebestach/OrbitalPayloadCalculator)](LICENSE)
[![Release](https://img.shields.io/github/v/release/Aebestach/OrbitalPayloadCalculator)](https://github.com/Aebestach/OrbitalPayloadCalculator/releases)

[English](README.md) | [中文](README_CN.md)

</div>

---

## Introduction

In Kerbal Space Program (KSP), designing rockets is often less ***engineering*** and more ***alchemy*** — slapping on fuel tanks, guessing stage counts, praying it doesn't explode at launch, and only realizing after orbit insertion that:

-   Either you have too little Delta-V, missing the Mun by a hair and watching your Kerbals cry;
-   Or you have absurdly too much Delta-V, arriving at the Mun with enough fuel to tour the solar system;
-   Most awkwardly, you finally reach orbit only to find the payload feels like a lead block, watching the fuel gauge drop bar by bar along with your heart.

Surprisingly, despite KSP being a decade old, few tools have specifically addressed this problem. As a rocket designer myself, I've felt the pain: constantly checking Delta-V charts and calculating payloads is brain-draining and error-prone. These needs and ***pain points*** led to the birth of **Orbital Payload Calculator**.

Now, **Orbital Payload Calculator** upgrades your ***rocket alchemy*** to ***rational engineering***.
It calculates payload mass and required Delta-V in advance, so you no longer pile on fuel by feel or carry extra ***emotional support fuel tanks***.

From now on, your rockets won't be ***almost there*** or ***overflowing with fuel***, but rather — just right, reaching orbit elegantly and heading to the Mun with style.

**Orbital Payload Calculator** is a utility mod for **Kerbal Space Program (KSP)** that estimates the maximum payload mass your rocket can deliver to a specific target orbit. It performs a simulation-based calculation to account for gravity, atmospheric drag, and steering losses, helping you design efficient launch vehicles without the guesswork.

It works in both the **Editor (VAB/SPH)** and in **Flight** (for vessels that are Landed or PreLaunch).

<div align="center">
    <img src="https://i.imgur.com/vLPrqJE.jpg" alt="UI Screenshot"/>
</div>


## Features

-   **Payload Estimation:** Calculates the maximum payload (tons) to your target orbit; iteratively matches available Delta-V against required Delta-V.
-   **Advanced Loss Modeling:** Ascent simulation based on actual atmospheric curves; Pessimistic / Normal / Optimistic modes; Advanced parameter overrides.
-   **Multi-Body & Rotation:** Supports any celestial body and launch latitude; accounts for prograde/retrograde launch.
-   **Staged Analysis:** Multi-stage support; automatic engine role classification (Main / Solid / Electric / Retro / Settling / EscapeTower); separation group detection; manual engine role overrides.
-   **Configurable Targets:** Apoapsis, periapsis, inclination, launch latitude; units m / km / Mm; automatic plane change Delta-V.
-   **Ideal Delta-V Model:** Surface-to-orbit ideal Delta-V (two-body, impulsive); auto-selection between energy-optimal and Hohmann models by orbit parameters.

**Technical implementation details** → [Technical Description](Technical%20Description_EN.md).

## Dependencies

-   **Click Through Blocker**

## Compatibility

1. **Celestial bodies & aerodynamics mods:** Stock bodies, RSS (Real Solar System), FAR (Ferram Aerospace Research), etc. are theoretically supported for calculation. Actual payload capacity depends on real flight trajectory; results are for reference only. Prediction accuracy is generally acceptable in most scenarios.
2. **Launch pad mods:** This mod has minimized the impact of **AlphaMensaes Modular Launch Pads** and similar launch pad mods on calculations (though some cases may still slip through). For more accurate payload estimates, **do not** include launch clamps, launch pads, or other rocket-unrelated dead weight in your vessel when calculating.

## Installation

1.  Download the latest release.
2.  Copy the `GameData/OrbitalPayloadCalculator` folder into your KSP installation’s `GameData` directory.
3.  Ensure **Click Through Blocker** is installed.

## Usage Guide

### Open the Calculator
-   **Hotkey:** Press **Left Alt + P** (default) to toggle the window.
-   **Toolbar:** Click the **Orbital Payload Calculator** icon in the AppLauncher.

### Workflow
1.  **Select Body:** Choose the celestial body you are launching from (e.g., Kerbin).
2.  **Set Orbit:** Enter your desired **Apoapsis**, **Periapsis**, **Inclination**, and **Launch Latitude**.
3.  **Launch Latitude:**
    -   In Editor: Enter the expected launch latitude manually (range -90° to 90°). Invalid values will be flagged.
    -   In Flight: Automatically reads the vessel's current latitude; displayed in degrees-minutes-seconds (DMS) with N/S.
4.  **Loss Settings:**
    -   **Pessimistic / Normal / Optimistic:** Choose one; all use the built-in ascent simulation. Normal is the default; Optimistic assumes a more optimized ascent; Pessimistic gives conservative, higher-loss estimates.
    -   **Advanced Settings:** Expandable anytime. Set turn speed, Cd coefficient (range 0.3–2.0), turn altitude, and gravity/atmosphere/attitude overrides; **Advanced parameters override Pessimistic/Normal/Optimistic**.
    -   **Treat cargo bay as fairing:** When enabled, cargo bay mass is excluded at its jettison stage; disable if the cargo bay stays on for orbit.
    -   **Attitude loss reference:** Excellent trajectory 10–30 m/s, average 30–80 m/s, aggressive turn 100–300 m/s.
5.  **Engine Classification:** In the stage breakdown, click **Engine Classification** to switch each engine's role (Main / Solid / Electric / Retro / Settling / Escape Tower (LES)); overrides are persisted.
6.  **Calculate:** Click the **Calculate** button.

### Payload Calculation Methods

-   **Incorporate Payload:**
   Treats the payload as part of the total rocket mass. As long as the estimated payload is greater than 0, the rocket still has transport capacity.

-   **Pure Rocket:**
   Sets the payload aside (does not affect rocket calculation) and calculates the rocket's own Delta-V directly to determine transport capacity.

<p align="center">
  <img src="https://i.imgur.com/ZbtSU3o.jpg" alt="Incorporate Payload" width="1000"/><br/>Incorporate Payload
</p>
<p align="center">
  <img src="https://i.imgur.com/IhsxXNS.jpg" alt="Pure Rocket" width="1000"/><br/>Pure Rocket
</p>

## Notes

-   **SSTO Support**
    -   **Pure Rocket SSTO:** Theoretically well-supported.
    -   **Air-breathing / Hybrid SSTO:** (e.g., RAPIER) Use for reference only, as propellants like IntakeAir (not stored in tanks) are not included in the calculation.

-   **Calculation Accuracy**
    -   Actual launches are affected by factors like gravity turn speed vs. start height, flight trajectory, vessel aerodynamics, and piloting style. Therefore, the calculated payload is a theoretical estimate.
    -   If actual flight results deviate from calculations, try optimizing gravity turn speed, turn height, and related parameters in MechJeb (MJ) to get closer to theoretical performance.

-   **Development Status**

    -   This mod is still being improved — it can help you calculate Delta-V, but it's not smart enough to save you from every ***aerodynamic disaster***.
    -   If it miscalculates, explodes, or behaves like an intern engineer, feedback and suggestions are welcome. Let's polish it into a true ***Chief Engineer*** together.
