# Orbital Payload Calculator

<div align="center">

[![License](https://img.shields.io/github/license/Aebestach/OrbitalPayloadCalculator)](LICENSE)
[![Release](https://img.shields.io/github/v/release/Aebestach/OrbitalPayloadCalculator)](https://github.com/Aebestach/OrbitalPayloadCalculator/releases)

[English](README.md) | [中文](README-CN.md)

</div>

---

## 📖 Introduction

**Orbital Payload Calculator** is a utility mod for **Kerbal Space Program (KSP)** that estimates the maximum payload mass your rocket can deliver to a specific target orbit. It performs a simulation-based calculation to account for gravity, atmospheric drag, and steering losses, helping you design efficient launch vehicles without the guesswork.

It works in both the **Editor (VAB/SPH)** and in **Flight** (for vessels that are Landed or PreLaunch).

## ✨ Features

*   **🚀 Payload Estimation**
    *   Calculates the maximum payload (in tons) capable of reaching your target orbit.
    *   Iteratively solves for the payload mass that matches your rocket's available Delta-V against the required Delta-V.
*   **📉 Advanced Loss Modeling**
    *   **Simulation-based:** Runs a time-stepped ascent simulation using the celestial body's actual atmospheric pressure and temperature curves.
    *   **Gravity & Drag:** Automatically estimates gravity losses and atmospheric drag based on vessel TWR and aerodynamics.
    *   **Optimistic Mode:** Option to assume an aggressive/optimized ascent profile for lower loss estimates.
*   **🌍 Multi-Body & Rotation**
    *   Supports any celestial body (Kerbin, Eve, Duna, modded planets, etc.).
    *   Accounts for planetary rotation (launching East vs. West) and launch latitude.
*   **🔄 Staged Analysis**
    *   Properly handles multi-stage vessels.
    *   Blends Vacuum and Sea-Level ISP for atmospheric stages based on pressure.
    *   View detailed breakdown per stage: Mass, Thrust, ISP, TWR, and Delta-V.
*   **🛠️ Configurable Targets**
    *   Set Target Apoapsis (Ap), Periapsis (Pe), and Inclination.
    *   Supports units: `m`, `km`, `Mm`.
    *   Automatically calculates Plane Change Delta-V if the target inclination is lower than the launch latitude.

## 📦 Dependencies

*   **Click Through Blocker** (Required)

## 📥 Installation

1.  Download the latest release.
2.  Copy the `GameData/OrbitalPayloadCalculator` folder into your KSP installation’s `GameData` directory.
3.  Ensure **Click Through Blocker** is installed.

## 🎮 Usage Guide

### Open the Calculator
*   **Hotkey:** Press **Left Alt + P** (default) to toggle the window.
*   **Toolbar:** Click the **Orbital Payload Calculator** icon in the AppLauncher (if enabled).

### Workflow
1.  **Select Body:** Choose the celestial body you are launching from (e.g., Kerbin).
2.  **Set Orbit:** Enter your desired **Apoapsis**, **Periapsis**, and **Inclination**.
3.  **Launch Latitude:**
    *   In Editor: Enter the expected launch latitude manually.
    *   In Flight: It defaults to the vessel's current latitude.
4.  **Loss Settings:**
    *   **Auto-estimate:** Recommended. Uses the built-in ascent simulation.
    *   **Optimistic:** Check this if you fly a very efficient gravity turn.
    *   **Manual Overrides:** Uncheck "Auto" to manually enter Delta-V reserves for Gravity, Atmosphere, and Attitude losses.
5.  **Calculate:** Click the **Calculate** button.

### Results Explained
*   **Estimated Payload:** The max tonnage you can add to the vessel's current payload.
*   **Required Δv:** Total Delta-V needed to reach orbit (Orbital Speed + Losses + Plane Change + Rotation Assist).
*   **Available Δv:** Your vessel's total Delta-V.
*   **Loss Breakdown:** Shows how much Δv is lost to Gravity, Drag, and Steering (Attitude).

## ⚙️ Configuration

*   **Font Size:** You can adjust the UI font size (13-20) directly in the window header. Click **Save** to apply.
*   **Settings Storage:** Font size preference is saved in Unity's `PlayerPrefs`.

## 🧩 How It Works

1.  **Vessel Analysis:** The mod scans your vessel (active or editor) to build a staging list, calculating mass, thrust, and ISP for each stage.
2.  **Ascent Simulation:** It simulates a gravity turn ascent:
    *   Vertical climb until a specific velocity/altitude.
    *   Gravity turn pitching over based on a power-law curve.
    *   Drag is calculated using estimated CdA (Drag Coefficient * Area) derived from mass.
    *   Thrust and ISP vary dynamically with atmospheric pressure.
3.  **Binary Search:** It runs the simulation multiple times, adjusting the "simulated payload mass" until the Available Δv matches the Required Δv, finding the limit of your rocket's capacity.

## ⚠️ Notes

*   **SSTO support:**
    *   **Pure rocket SSTOs** (e.g., Nerv + Liquid Fuel) are fully supported.
    *   **Air-breathing / hybrid SSTOs** (e.g., RAPIER) should be used as reference only, as propellants like IntakeAir are not stored in tanks and are not included in the calculation.
