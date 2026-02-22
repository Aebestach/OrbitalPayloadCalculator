# Estimated vs Calculated Data in Orbital Payload Calculator

This document describes which values in the calculator are **calculated from physics** (and automatically adjust per celestial body) versus which are **estimated** using heuristics or empirical formulas.

---

## Calculated Values (Auto-Adjusted by Celestial Body)

These values are derived from orbital mechanics and the celestial body's physical properties. They are precise rather than estimated.

| Data | Formula / Method | Body Properties Used |
|------|-----------------|----------------------|
| **Orbital Speed** | \(v = \sqrt{\mu \cdot (2/r_{Pe} - 1/a)}\) | `gravParameter`, `Radius`, orbit altitudes |
| **Plane Change ΔV** | \(2v \sin(\theta/2)\) | Orbital speed, launch latitude, target inclination |
| **Rotation Bonus/Loss** | Equatorial speed + latitude correction | `rotationPeriod`, `Radius` |
| **Stage ΔV** | Tsiolkovsky rocket equation | Mass, propellant, Isp |
| **Atmospheric ISP Blend** | Samples `GetPressure(h)` curve with velocity weighting | `atmosphereDepth`, `atmospherePressureSeaLevel` |
| **Gravity Loss (in simulation)** | Time-stepped: \(g = \mu/R^2\) per step | `gravParameter`, `Radius` |
| **Atmospheric Density (in simulation)** | \(\rho = p/(R_{air} \cdot T)\) | `GetPressure(h)`, `GetTemperature(h)` |
| **Default Orbit Altitude** | Atmosphere top + 10,000 m | `atmosphereDepth` |

---

## Estimated Values (Heuristics / Empirical Formulas)

These values use heuristics or fitted formulas because precise inputs are unavailable or costly to compute.

| Data | Estimation Method | Notes |
|------|-------------------|-------|
| **CdA (Drag Area)** | `0.7~1.0 × √(wet mass)` | Editor has no part geometry; mass approximates shape (see `LossModel.cs:78-80`) |
| **Gravity Loss (no simulation)** | `FallbackEstimate` empirical formula | Used when thrust/Isp data is missing |
| **Atmospheric Loss (no simulation)** | `atmoA + atmoB` empirical formula | Fitted using gN, pN, dN body scales |
| **Attitude Loss** | `baseA + baseB × incFactor` | Uses empirical coefficients in both simulation and fallback |
| **Turn Start Speed** | \(80 × gN^{0.25} × (...)\) | Gravity and atmosphere scaling |
| **Turn Start Altitude** | `atmoHeight × (0.01 + 0.004×ln(1+pN))` | Heuristic turn altitude |
| **Atmospheric ISP Blend Weights** | `1 - 0.5×i/N` | Approximate "time spent at each altitude" |

---

## Summary

- **Calculated:** Orbital speed, plane change ΔV, rotation bonus, stage ΔV, gravity field, atmospheric pressure/temperature/density, atmosphere-blended Isp. All driven by body data and change per celestial body.
- **Estimated:** CdA, gravity/atmospheric/attitude losses (especially in fallback and attitude terms), turn start speed/altitude, atmosphere blend weights. These rely on heuristics or empirical rules.

Even when the time-stepped simulation runs, drag uses **actual atmospheric density** from `GetPressure`/`GetTemperature`, but the **CdA** (drag coefficient × frontal area) is still estimated from wet mass, because true part geometry is unavailable in the editor.
