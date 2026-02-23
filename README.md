# Orbital Payload Calculator

<div align="center">
    
<img src="https://imgur.com/ryDQSmm.jpg" alt="Banner"/>
    
[![License](https://img.shields.io/github/license/Aebestach/OrbitalPayloadCalculator)](LICENSE)
[![Release](https://img.shields.io/github/v/release/Aebestach/OrbitalPayloadCalculator)](https://github.com/Aebestach/OrbitalPayloadCalculator/releases)

[English](README.md) | [中文](README_CN.md)

</div>

---

## 📖 Introduction

In Kerbal Space Program (KSP), designing rockets is often less ***engineering*** and more ***alchemy*** — slapping on fuel tanks, guessing stage counts, praying it doesn't explode at launch, and only realizing after orbit insertion that:

*   Either you have too little Delta-V, missing the Mun by a hair and watching your Kerbals cry;
*   Or you have absurdly too much Delta-V, arriving at the Mun with enough fuel to tour the solar system;
*   Most awkwardly, you finally reach orbit only to find the payload feels like a lead block, watching the fuel gauge drop bar by bar along with your heart.

Surprisingly, despite KSP being a decade old, few tools have specifically addressed this problem. As a rocket designer myself, I've felt the pain: constantly checking Delta-V charts and calculating payloads is brain-draining and error-prone. These needs and ***pain points*** led to the birth of **Orbital Payload Calculator**.

Now, **Orbital Payload Calculator** upgrades your ***rocket alchemy*** to ***rational engineering***.
It calculates payload mass and required Delta-V in advance, so you no longer pile on fuel by feel or carry extra ***emotional support fuel tanks***.

From now on, your rockets won't be ***almost there*** or ***overflowing with fuel***, but rather — just right, reaching orbit elegantly and heading to the Mun with style.

**Orbital Payload Calculator** is a utility mod for **Kerbal Space Program (KSP)** that estimates the maximum payload mass your rocket can deliver to a specific target orbit. It performs a simulation-based calculation to account for gravity, atmospheric drag, and steering losses, helping you design efficient launch vehicles without the guesswork.

It works in both the **Editor (VAB/SPH)** and in **Flight** (for vessels that are Landed or PreLaunch).

<div align="center">
    <img src="https://imgur.com/w5oGoM5.jpg" alt="UI Screenshot"/>
</div>


## ✨ Features

*   **🚀 Payload Estimation**
    *   Calculates the maximum payload (in tons) capable of reaching your target orbit.
    *   Iteratively solves for the payload mass that matches your rocket's available Delta-V against the required Delta-V.
*   **📉 Advanced Loss Modeling**
    *   **Simulation-based:** Runs a time-stepped ascent simulation using the celestial body's actual atmospheric pressure and temperature curves.
    *   **Gravity & Drag:** Automatically estimates gravity losses and atmospheric drag based on vessel TWR and aerodynamics. CdA uses the same heuristic (Cd × √mass) in both Editor and Flight; user Cd or default 0.7/1.0.
    *   **Estimate Modes:** Mutually exclusive **Normal** and **Optimistic** modes; Advanced Settings parameters always override both. In **Optimistic** mode, the ascent simulation uses a more aggressive turn exponent (0.50) for earlier pitch-over, reducing modeled gravity loss.
*   **🌍 Multi-Body & Rotation**
    *   Supports any celestial body (Kerbin, Eve, Duna, modded planets, etc.).
    *   Accounts for planetary rotation (launching East vs. West) and launch latitude.
*   **🔄 Staged Analysis**
    *   Properly handles multi-stage vessels.
    *   Blends Vacuum and Sea-Level ISP for atmospheric stages based on pressure.
    *   **Separation Groups:** Detects decouplers in *all* stages after the bottom stage, so multi-pair boosters (SRBs or liquid) that separate at different times are correctly modeled. Each booster pair's dry mass is subtracted when its engines exhaust.
    *   View detailed breakdown per stage: Mass, Thrust, ISP, TWR, and Delta-V.
*   **🛠️ Configurable Targets**
    *   Set Target Apoapsis (Ap), Periapsis (Pe), Inclination, and Launch Latitude.
    *   Supports units: `m`, `km`, `Mm`.
    *   Automatically calculates Plane Change Delta-V if the target inclination is lower than the launch latitude.
*   **📐 Ideal Δv Model (Surface → Orbit)**
    *   Two-body, impulsive; ignores atmosphere and gravity loss.
    *   **Model A (energy-optimal):** Global minimum Δv; used for low orbits (α < 1.5) and low-eccentricity intermediate orbits.
    *   **Model B (Hohmann):** Structured burn sequence (burn1→burn2, or +burn3 for elliptical); used for high orbits (α > 2.0) and high-eccentricity intermediate orbits.
    *   Automatic selection by semi-major-axis ratio α = a/r₀ and eccentricity e; see [DATA_SOURCES](DATA_SOURCES.md).

## 📦 Dependencies

*   **Click Through Blocker** (Required)

## 📥 Installation

1.  Download the latest release.
2.  Copy the `GameData/OrbitalPayloadCalculator` folder into your KSP installation’s `GameData` directory.
3.  Ensure **Click Through Blocker** is installed.

## 🎮 Usage Guide

### Open the Calculator
*   **Hotkey:** Press **Left Alt + P** (default) to toggle the window.
*   **Toolbar:** Click the **Orbital Payload Calculator** icon in the AppLauncher.

### Workflow
1.  **Select Body:** Choose the celestial body you are launching from (e.g., Kerbin).
2.  **Set Orbit:** Enter your desired **Apoapsis**, **Periapsis**, **Inclination**, and **Launch Latitude**.
3.  **Launch Latitude:**
    *   In Editor: Enter the expected launch latitude manually (range -90° to 90°). Invalid values will be flagged.
    *   In Flight: Automatically reads the vessel's current latitude; displayed in degrees-minutes-seconds (DMS) with N/S.
4.  **Loss Settings:**
    *   **Normal / Optimistic:** Choose one; both use the built-in ascent simulation. Normal is default; Optimistic assumes a more optimized ascent profile.
    *   **Advanced Settings:** Expandable anytime. Set turn speed, Cd coefficient (drag coefficient, range 0.3–2.0), turn altitude, and gravity/atmosphere/attitude overrides; **Advanced parameters override Normal/Optimistic**.
    *   **Attitude loss reference:** Excellent trajectory 10–30 m/s, average 30–80 m/s, aggressive turn 100–300 m/s.
5.  **Calculate:** Click the **Calculate** button.

### Results Explained
*   **Vessel block** (top): Vessel name, wet mass, dry mass.
*   **Orbit block:** Launch body, apoapsis, periapsis, inclination, eccentricity.
*   **Estimated Payload:** The max tonnage you can add to the vessel's current payload.
*   **Required Delta-V:** Total Delta-V needed to reach orbit (Ideal Δv + Losses + Plane Change ± Rotation Assist). Ideal Δv is the theoretical minimum from surface to target orbit; see **Delta-V Details** for breakdown.
*   **Available Delta-V:** Your vessel's total Delta-V. In Flight, the **Delta-V Details** popup also shows ground altitude above this.
*   **Loss Breakdown:** Shows how much Delta-V is lost to Gravity, Drag, and Steering (Attitude).

### Payload Calculation Methods

*   **Incorporate Payload:**
   Treats the payload as part of the total rocket mass. As long as the estimated payload is greater than 0, the rocket still has transport capacity.

*   **Pure Rocket:**
   Sets the payload aside (does not affect rocket calculation) and calculates the rocket's own Delta-V directly to determine transport capacity.

<div style="display: flex; flex-direction: column; gap: 20px; justify-content: center; align-items: center;">
  <div style="text-align: center;">
    <img src="https://i.imgur.com/lf9kt8u.jpg" alt="Incorporate Payload" width="1000"/>
    <p align="center">Incorporate Payload</p>
  </div>
  <div style="text-align: center;">
    <img src="https://i.imgur.com/gFr2pXb.jpg" alt="Pure Rocket" width="1000"/>
    <p align="center">Pure Rocket</p>
  </div>
</div>

## 🧩 How It Works

1.  **Ideal Δv (Surface → Orbit):** Uses a hybrid model—**Model A** (energy-optimal lower bound) for near-surface and low-eccentricity mid orbits, **Model B** (Hohmann structure) for high and high-eccentricity orbits. Selection is automatic based on α = a/r₀ and eccentricity.
2.  **Vessel Analysis:** The mod scans your vessel (active or editor) to build a staging list, calculating mass, thrust, and ISP for each stage.
3.  **Ascent Simulation:** It simulates a gravity turn ascent:
    *   Vertical climb until a specific velocity/altitude.
    *   Gravity turn pitching over based on a power-law curve (exponent 0.50 in Optimistic, 0.58/0.70 in Normal).
    *   Drag: Cd coefficient is user-configurable or heuristic (0.7–1.0 × √mass); same in both Editor and Flight.
    *   Thrust and ISP vary dynamically with atmospheric pressure.
4.  **Binary Search:** It runs the simulation multiple times, adjusting the ***simulated payload mass*** until the Available Delta-V matches the Required Delta-V, finding the limit of your rocket's capacity.

## ⚠️ Notes

*   **SSTO Support**
    *   **Pure Rocket SSTO:** Theoretically well-supported.
    *   **Air-breathing / Hybrid SSTO:** (e.g., RAPIER) Use for reference only, as propellants like IntakeAir (not stored in tanks) are not included in the calculation.

*   **Calculation Accuracy**
    *   Actual launches are affected by factors like gravity turn speed vs. start height, flight trajectory, vessel aerodynamics, and piloting style. Therefore, the calculated payload is a theoretical estimate.
    *   If actual flight results deviate from calculations, try optimizing gravity turn speed, turn height, and related parameters in MechJeb (MJ) to get closer to theoretical performance.

*   **Compatibility & Testing**

    *   The current version has NOT been systematically tested with **FAR (Ferram Aerospace Research)**, **RSS (Real Solar System)**, **Principia**, or **Rescale** mods.
    *   You are welcome to test in these environments and provide feedback or suggestions.

*   **Development Status**

    *   This mod is still being improved — it can help you calculate Delta-V, but it's not smart enough to save you from every ***aerodynamic disaster***.
    *   If it miscalculates, explodes, or behaves like an intern engineer, feedback and suggestions are welcome. Let's polish it into a true ***Chief Engineer*** together.
