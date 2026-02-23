# Estimated vs Calculated Data in Orbital Payload Calculator

This document describes which values in the calculator are **calculated from physics** (and automatically adjust per celestial body) versus which are **estimated** using heuristics or empirical formulas.

---

## Calculated Values (Auto-Adjusted by Celestial Body)

These values are derived from orbital mechanics and the celestial body's physical properties. They are precise rather than estimated.

| Data | Formula / Method | Body Properties Used |
|------|-----------------|----------------------|
| **Ideal Δv from Surface** | Two models, auto-selected by α = a/r₀ and e = (rAp−rPe)/(rAp+rPe). **Model A (α < 1.5 or α ≤ 2 & e < 0.1):** `√(2μ × (1/r₀ − 1/(rPe+rAp)))`. **Model B (else):** Hohmann burn1+burn2, or +burn3 for elliptical | `gravParameter`, `Radius`, rPe, rAp |
| **Orbital Speed** | `v = √(μ × (2/rPe − 1/a))` | `gravParameter`, `Radius`, orbit altitudes |
| **Plane Change ΔV** | `2v × sin(θ/2)` | Orbital speed, launch latitude, target inclination |
| **Rotation Bonus/Loss** | Equatorial speed + latitude correction | `rotationPeriod`, `Radius` |
| **Stage ΔV** | Tsiolkovsky rocket equation | Mass, propellant, Isp |
| **Atmospheric ISP Blend** | Samples `GetPressure(h)` curve with velocity weighting | `atmosphereDepth`, `atmospherePressureSeaLevel` |
| **Gravity Loss (in simulation)** | Time-stepped `g = μ/R²` per step | `gravParameter`, `Radius` |
| **Atmospheric Density (in simulation)** | `ρ = p/(R_air × T)` | `GetPressure(h)`, `GetTemperature(h)` |
| **Default Orbit Altitude** | Atmosphere top + 10,000 m | `atmosphereDepth` |
| **Separation Groups (booster dry mass)** | Decoupler scan over all stages `maxPropStageNum-1` … 0; drop when engines exhaust | Part hierarchy, `inverseStage`, `ModuleDecouple` |

### Ideal Δv Model Detail (Surface → Orbit)

Two-body, impulsive; no atmosphere, no gravity loss. Symbols: μ = gravParameter, r₀ = body radius, rPe/rAp = periapsis/apocentre, a = (rPe+rAp)/2, **α = a/r₀**, **e = (rAp−rPe)/(rAp+rPe)**.

**Model selection (α, e):**

- **α < 1.5** → Model A (energy-optimal lower bound)
- **1.5 ≤ α ≤ 2.0**: if e < 0.1 → Model A; if e ≥ 0.1 → Model B
- **α > 2.0** → Model B (Hohmann structure)

**Model A:** Global minimum Δv. Elliptical: `√(2μ × (1/r₀ − 1/(rPe+rAp)))`. Circular: same with rPe=rAp=r.

**Model B:** Structured burns. Circular (rPe=rAp=r): burn1 = √(μ/r₀)·√(2r/(r₀+r)), burn2 = √(μ/r)·(1−√(2r₀/(r₀+r))). Elliptical: burn1+burn2 at rPe, then burn3 at apoapsis. Both models converge to √(2μ/r₀) (escape speed) as r→∞.

See [IDEAL_DV_MODELS.md](IDEAL_DV_MODELS.md) for full derivation and formulas.

---

## Estimated Values (Heuristics / Empirical Formulas)

These values use heuristics or fitted formulas because precise inputs are unavailable or costly to compute.

| Data | Estimation Method | Notes |
|------|-------------------|-------|
| **CdA (Drag Area)** | Both Editor and Flight: `Cd × √(wet mass)` with user Cd (0.3–2.0) or default 0.7/1.0 | Cd coefficient × √mass heuristic |
| **Gravity Loss (no simulation)** | `FallbackEstimate` empirical formula | Used when thrust/Isp data is missing |
| **Atmospheric Loss (no simulation)** | `atmoA + atmoB` empirical formula | Fitted using gN, pN, dN body scales |
| **Attitude Loss** | `baseA + baseB × incFactor` | Empirical coefficients; typical reference in table below |
| **Turn Start Speed** | `80 × gN^0.25 × (...)` | Gravity and atmosphere scaling |
| **Turn Start Altitude** | `atmoHeight × (0.01 + 0.004×ln(1+pN))` | Heuristic turn altitude |
| **Atmospheric ISP Blend Weights** | `1 - 0.5×i/N` | Approximate "time spent at each altitude" |
| **Turn Exponent (gravity turn)** | Optimistic: 0.50; Normal: 0.58 (bottom-stage sim) or 0.70 (loss model) | Empirical; controls pitch-over rate |

### Typical Attitude Loss Reference

| Situation | Typical Attitude Loss |
|-----------|------------------------|
| Excellent trajectory | 10–30 m/s |
| Average trajectory | 30–80 m/s |
| Aggressive turn | 100–300 m/s |

---

## Parameter Priority

**Advanced Settings** (gravity/atmosphere/attitude overrides, turn speed, Cd coefficient, turn altitude) **always override** the Normal / Optimistic estimate button defaults.

---

## Summary

- **Calculated:** Ideal Δv from surface (Model A/B), orbital speed, plane change ΔV, rotation bonus, stage ΔV, gravity field, atmospheric pressure/temperature/density, atmosphere-blended Isp. All driven by body data and change per celestial body.
- **Estimated:** Cd coefficient and CdA, gravity/atmospheric/attitude losses (especially in fallback and attitude terms), turn start speed/altitude, atmosphere blend weights. These rely on heuristics or empirical rules.

- **Editor & Flight:** CdA uses the same heuristic; no geometry-based CdA.
