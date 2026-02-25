# Orbital Payload Calculator Technical Description

This document consolidates the core technical specifications of Orbital Payload Calculator, including: estimated vs calculated data, surface-to-orbit ideal Delta-V models, and engine classification and identification.

---

# Part 1: Estimated vs Calculated Data

This section describes which values in the calculator are **calculated from physics** (and automatically adjust per celestial body) versus which are **estimated** using heuristics or empirical formulas.

## 1.1 Calculated Values (Auto-Adjusted by Celestial Body)

These values are derived from orbital mechanics and the celestial body's physical properties. They are precise rather than estimated.

| Data | Formula / Method | Body Properties Used |
|------|-----------------|----------------------|
| **Ideal Delta-V from Surface** | Two models, auto-selected by $\alpha = a/r_0$ and $e = (r_{\mathrm{Ap}}-r_{\mathrm{Pe}})/(r_{\mathrm{Ap}}+r_{\mathrm{Pe}})$. **Model A** (α < 1.5 or α ≤ 2 and e < 0.1): $\sqrt{2\mu(1/r_0 - 1/(r_{\mathrm{Pe}}+r_{\mathrm{Ap}}))}$. **Model B (else):** Hohmann burn1+burn2, or +burn3 for elliptical | `gravParameter`, `Radius`, rPe, rAp |
| **Orbital Speed** | $v = \sqrt{\mu(2/r_{\mathrm{Pe}} - 1/a)}$ | `gravParameter`, `Radius`, orbit altitudes |
| **Plane Change Delta-V** | $2v \sin(\theta/2)$ | Orbital speed, launch latitude, target inclination |
| **Rotation Bonus/Loss** | Equatorial speed + latitude correction | `rotationPeriod`, `Radius` |
| **Stage Delta-V** | Tsiolkovsky rocket equation | Mass, propellant, Isp |
| **Atmospheric ISP Blend** | Bottom-stage time-stepped simulation samples `GetPressure(h)`; Isp interpolated from `engine.atmosphereCurve` by instantaneous pressure | `atmosphereDepth`, `atmospherePressureSeaLevel` |
| **Gravity Loss (in simulation)** | Time-stepped $g = \mu/R^2$ per step | `gravParameter`, `Radius` |
| **Atmospheric Density (in simulation)** | $\rho = p/(R_{\mathrm{air}} \cdot T)$ | `GetPressure(h)`, `GetTemperature(h)` |
| **Default Orbit Altitude** | Atmosphere top + 10,000 m | `atmosphereDepth` |
| **Separation Groups (booster dry mass)** | Decoupler scan over all stages `maxPropStageNum-1` … 0; drop when engines exhaust | Part hierarchy, `inverseStage`, `ModuleDecouple` |

### Ideal Delta-V Model Detail (Surface → Orbit)

Two-body, impulsive; no atmosphere, no gravity loss. Symbols: $\mu$ = gravParameter, $r_0$ = body radius, $r_{\mathrm{Pe}}$/$r_{\mathrm{Ap}}$ = periapsis/apocentre, $a = (r_{\mathrm{Pe}}+r_{\mathrm{Ap}})/2$, $\alpha = a/r_0$, $e = (r_{\mathrm{Ap}}-r_{\mathrm{Pe}})/(r_{\mathrm{Ap}}+r_{\mathrm{Pe}})$.

**Model selection (α, e):**

- **α < 1.5** → Model A (energy-optimal lower bound)
- **1.5 ≤ α ≤ 2.0**: if e < 0.1 → Model A; if e ≥ 0.1 → Model B
- **α > 2.0** → Model B (Hohmann structure)

**Model A:** Global minimum Delta-V. Elliptical: $\sqrt{2\mu(1/r_0 - 1/(r_{\mathrm{Pe}}+r_{\mathrm{Ap}}))}$. Circular: same with $r_{\mathrm{Pe}}=r_{\mathrm{Ap}}=r$.

**Model B:** Structured burns. Circular ($r_{\mathrm{Pe}}=r_{\mathrm{Ap}}=r$): $\mathrm{burn1} = \sqrt{\mu/r_0} \cdot \sqrt{2r/(r_0+r)}$, $\mathrm{burn2} = \sqrt{\mu/r} \cdot (1-\sqrt{2r_0/(r_0+r)})$. Elliptical: burn1+burn2 at $r_{\mathrm{Pe}}$, then burn3 at apoapsis. Both models converge to $\sqrt{2\mu/r_0}$ (escape speed) as $r \to \infty$.

See [Part 2: Surface → Orbit Ideal Delta-V Models](#part-2-surface--orbit-ideal-delta-v-models) for full derivation and formulas.

## 1.2 Estimated Values (Heuristics / Empirical Formulas)

These values use heuristics or fitted formulas because precise inputs are unavailable or costly to compute.

| Data | Estimation Method | Notes |
|------|-------------------|-------|
| **CdA (Drag Area)** | Both Editor and Flight: $C_d \times \sqrt{m_{\mathrm{wet}}}$, where $m_{\mathrm{wet}}$ is wet mass in tons. User $C_d$ (0.3–2.0) or default 0.50/1.0/1.5 (Optimistic/Normal/Pessimistic) | $C_d$ coefficient × √mass heuristic |
| **Gravity Loss (no simulation)** | `FallbackEstimate` empirical formula | Used when thrust/Isp data is missing |
| **Atmospheric Loss (no simulation)** | $A_{\mathrm{atmo}} + B_{\mathrm{atmo}}$ empirical formula | Fitted using $g_N$, $p_N$, $d_N$ body scales |
| **Attitude Loss** | $(A + B \sqrt{p_N} \cdot g_N) \times (1 + f_{\mathrm{inc}})$ with $f_{\mathrm{inc}} = (i/90°) \times \lvert\cos\phi\rvert$. $A$, $B$ scaled by mode and $g_N$, $d_N$, $p_N$ | Empirical coefficients; typical reference in table below |
| **Turn Start Speed** | $v_{\mathrm{turn}} = v_{\mathrm{base}} \times g_N^{0.25} \times (0.92 + 0.18 \ln(1+p_N) + 0.12 \cdot d_N^{0.3})$, with $v_{\mathrm{base}}$ = 55/80/95 m/s per mode | Gravity and atmosphere scaling |
| **Turn Start Altitude** | $h_{\mathrm{turn}} = \mathrm{Clamp}(h_{\mathrm{atmo}} \times (0.01 + 0.004 \ln(1+p_N)), 800, 22000) \times (v_{\mathrm{turn}}/80)$ | Heuristic turn altitude |
| **Turn Exponent (gravity turn)** | Linear fit from turn start speed; typical values: bottom 0.40/0.58/0.65, full 0.45/0.70/0.80 | Empirical; controls pitch-over rate |

### Typical Attitude Loss Reference

| Situation | Typical Attitude Loss |
|-----------|------------------------|
| Excellent trajectory | 10–30 m/s |
| Average trajectory | 30–80 m/s |
| Aggressive turn | 100–300 m/s |

## 1.3 Estimate Mode Parameters

| Mode | Cd | Turn Start Speed | Turn Exp. Bottom | Turn Exp. Full |
|------|----|------------------|------------------|----------------|
| Optimistic | 0.50 | 55 m/s base | 0.40 | 0.45 |
| Normal | 1.0 | 80 m/s base | 0.58 | 0.70 |
| Pessimistic | 1.5 | 95 m/s base | 0.65 | 0.80 |

## 1.4 Parameter Priority

**Advanced Settings** (gravity/atmosphere/attitude overrides, turn speed, Cd coefficient, turn altitude) **always override** the Pessimistic / Normal / Optimistic estimate button defaults.

## 1.5 Summary

- **Calculated:** Ideal Delta-V from surface (Model A/B), orbital speed, plane change Delta-V, rotation bonus, stage Delta-V, gravity field, atmospheric pressure/temperature/density, atmosphere-blended Isp. All driven by body data and change per celestial body.
- **Estimated:** Cd coefficient and CdA, gravity/atmospheric/attitude losses (especially in fallback and attitude terms), turn start speed/altitude, turn exponent. These rely on heuristics or empirical rules.
- **Editor & Flight:** CdA uses the same heuristic; no geometry-based CdA.

---

# Part 2: Surface → Orbit Ideal Delta-V Models

Two-body, no atmosphere, no gravity loss.

## 2.1 Scope

- Any celestial body
- Two-body model
- Neglect atmospheric drag
- Neglect gravity loss
- Neglect rotation
- Impulsive maneuvers

## 2.2 Symbols

$\mu$ = gravitational parameter (gravParameter)  
$r_0$ = body radius (distance from launch point to center)  
$r$ = target circular orbit radius = $r_0$ + altitude  
$r_{\mathrm{Pe}}$ = periapsis radius  
$r_{\mathrm{Ap}}$ = apoapsis radius  
$a$ = semi-major axis = $(r_{\mathrm{Pe}} + r_{\mathrm{Ap}}) / 2$  

$\alpha$ = $a / r_0$ (for model selection)  
$e$ = $(r_{\mathrm{Ap}} - r_{\mathrm{Pe}}) / (r_{\mathrm{Ap}} + r_{\mathrm{Pe}})$ (eccentricity)  

## 2.3 Model A: Energy-Minimum (Global Optimal)

### Derivation

Initial energy: $E_0 = -\mu/r_0$  

Target circular orbit: $E = -\mu/(2r)$  

Target elliptic orbit: $E = -\mu/(2a)$, where $a = (r_{\mathrm{Pe}} + r_{\mathrm{Ap}})/2$  

Energy increment: $\Delta E = E - E_0$  

From $\frac{1}{2}v^2 = \Delta E$:

### Final Formulas

**Circular Orbit**

$$
\text{Delta-V}_A = \sqrt{ 2\mu \left( \frac{1}{r_0} - \frac{1}{2r} \right) }
$$

**Elliptic Orbit**

$$
\text{Delta-V}_A = \sqrt{ 2\mu \left( \frac{1}{r_0} - \frac{1}{r_\mathrm{Pe} + r_\mathrm{Ap}} \right) }
$$

### Properties

- Global minimum Delta-V  
- Continuous-thrust limit solution  
- No Hohmann assumption  
- Valid for any target radius  

As $r \to \infty$: $\text{Delta-V}_A \to \sqrt{2\mu/r_0}$ (escape speed)

## 2.4 Model B: Hohmann-Structured (Engineering Solution)

### Case 1: Circular Target (rPe = rAp = r)

First burn (to transfer ellipse):

$$
\mathrm{burn1} = \sqrt{\frac{\mu}{r_0}} \cdot \sqrt{ \frac{2r}{r_0 + r} }
$$

Second burn (circularize at r):

$$
\mathrm{burn2} = \sqrt{\frac{\mu}{r}} \cdot \left( 1 - \sqrt{ \frac{2r_0}{r_0 + r} } \right)
$$

Total: $\text{Delta-V}_B = \mathrm{burn1} + \mathrm{burn2}$

### Case 2: Elliptic Target (rPe < rAp)

$$
\mathrm{burn1} = \sqrt{\frac{\mu}{r_0}} \cdot \sqrt{ \frac{2r_\mathrm{Pe}}{r_0 + r_\mathrm{Pe}} }
$$

$$
\mathrm{burn2} = \sqrt{\frac{\mu}{r_\mathrm{Pe}}} \cdot \left( 1 - \sqrt{ \frac{2r_0}{r_0 + r_\mathrm{Pe}} } \right)
$$

$$
\mathrm{burn3} = \sqrt{ \frac{2\mu\,r_\mathrm{Ap}}{ r_\mathrm{Pe}(r_\mathrm{Pe} + r_\mathrm{Ap}) } } - \sqrt{ \frac{\mu}{r_\mathrm{Pe}} }
$$

Total: $\text{Delta-V}_B = \mathrm{burn1} + \mathrm{burn2} + \mathrm{burn3}$

### Properties

- Clear structure  
- Direct mapping to maneuver steps  
- Suitable for distant orbits  
- Converges to Model A when $r \gg r_0$ or $r_{\mathrm{Ap}} \gg r_{\mathrm{Pe}}$  

As $r \to \infty$: $\text{Delta-V}_B \to \sqrt{2\mu/r_0}$

## 2.5 Model Selection Boundaries (Any Celestial Body)

Define: $\alpha = a/r_0$ ($a$ = semi-major axis), $e = (r_{\mathrm{Ap}}-r_{\mathrm{Pe}})/(r_{\mathrm{Ap}}+r_{\mathrm{Pe}})$

### 1. Low Orbit Region

**α < 1.5**

Characteristics:
- Target near bottom of gravity well
- Hohmann structure adds structural error
- Low-eccentricity ellipse may slightly increase Delta-V

Recommendation: Use Model A

### 2. Intermediate Orbit Region

**1.5 ≤ α ≤ 2.0**

Characteristics:
- Difference between models decays quickly
- Low eccentricity (e < 0.1): negligible Delta-V difference
- High eccentricity (e ≥ 0.1): prefer Model B

Recommendation:
- Low eccentricity: Model A
- High eccentricity: Model B

### 3. High / Distant Orbit Region

**α > 2.0**

Characteristics:
- Orbit near gravity well edge
- Models nearly equivalent
- Hohmann structure matches practical operations

Recommendation: Use Model B

## 2.6 Unified Decision Flow (Elliptic Orbits)

```text
Compute semi-major axis a = (rPe + rAp) / 2
Compute ratio α = a / r₀
Compute eccentricity e = (rAp − rPe) / (rAp + rPe)

# 1. Low orbit
if α < 1.5:
    use Model A

# 2. Intermediate orbit
else if 1.5 ≤ α ≤ 2.0:
    if e < 0.1:
        use Model A
    else:
        use Model B

# 3. High / distant orbit
else:  # α > 2.0
    use Model B
```

## 2.7 Summary

**Model A:**
- Energy limit
- Mathematically optimal
- Best as theoretical lower bound
- Single Delta-V injection; no concern for maneuver structure

**Model B:**
- Engineering structure
- Staged maneuvers
- Suitable for distant orbits and mission planning
- Works for low-eccentricity high orbits
- Closer to in-game Maneuver Node or practical staged operations

Both converge to $\sqrt{2\mu/r_0}$ (escape speed) as $r \to \infty$ or for high distant elliptical orbits.

---

# Part 3: Engine Classification and Identification

This section describes the automatic identification rules for engine types in OrbitalPayloadCalculator, the player feedback mechanism, and how each engine type is handled in dV calculation and fuel allocation. Designed to support stock KSP, RO (Realism Overhaul), and multi-fuel engines.

## 3.1 Engine Role Definitions

| Role | Description | Participates in dV | Assigned Fuel | Mass/Drag Counted |
|------|-------------|-------------------|---------------|------------------|
| **Main** | Liquid (from tanks) | ✓ | ✓ | ✓ |
| **Solid** | Solid (self-contained fuel, incl. RO) | ✓ | ✓ | ✓ |
| **Electric** | Electric/ion | ✓ | ✓ | ✓ |
| **Retro** | Retrograde | ✗ | ✗ | ✓ |
| **Settling** | Settling burn use | ✗ | ✗ | ✓ |
| **EscapeTower** | Escape tower | ✗ | ✗ | ✓ |

Engines that do not participate in dV or fuel allocation (Retro/Settling/EscapeTower) still have their mass and drag counted; otherwise the overall estimate would be biased. Participating engines receive only the propellants listed in their propellants.

## 3.2 Retro Engine Identification

### Principle

Retro engines have thrust direction inconsistent with the vehicle's bottom-stage main thrust (dot product < 0.8, including reverse and lateral). "Bottom-stage main thrust direction" is taken from the thrust direction of the single engine with the largest StageNumber (bottom stage, first to fire) and highest thrust among dV-participating engines (liquid, solid, electric).

### Thrust Direction Retrieval

- KSP uses `ModuleEngines.thrustTransforms`: each Transform's **-forward** (thrust outlet direction per Unity convention) represents thrust direction
- Must transform to **world space**: `part.transform.TransformDirection(-thrustTransform.forward)` or equivalent
- When `thrustTransforms` is empty, use `part.transform.up` etc. as fallback

### Editor vs Flight Mode

- **Flight mode**: `Part` and `Transform` are attached to the scene and can be read directly
- **Editor mode**: Parts in `EditorLogic.fetch.ship` have full `transform` and `attachNodes`; `thrustTransforms` is available after Part is loaded

Conclusion: **Thrust direction can be correctly identified in both editor and flight mode as long as the Part is loaded.**

### Identification Logic

1. Determine bottom-stage main thrust direction: among engines **not yet labeled Electric, EscapeTower, or Settling**, take the single engine with the largest StageNumber (bottom stage) and highest thrust; use its thrust direction as reference
2. For each engine: if its thrust direction dot product with bottom-stage main thrust < 0.8 (angle > ~37°, including reverse and lateral), label as **Retro**

## 3.3 Electric Engine Identification

**Any engine whose propellants include ElectricCharge is classified as electric.**

Example: `engine.propellants` contains any `prop.name == "ElectricCharge"` → `EngineRole = Electric`.

### Calculation: Same as Liquid and Solid

Electric propulsion uses the standard rocket equation, same formula:

$$
\text{Delta-V} = I_{\mathrm{sp}} \cdot g_0 \cdot \ln(m_{\mathrm{wet}} / m_{\mathrm{dry}})
$$

The difference:

- Propellants are XenonGas + ElectricCharge etc., not LiquidFuel/Oxidizer
- Fuel (including batteries) is already recorded in Part and resource systems

**Recommendation: Electric propulsion uses the same dV calculation logic as liquid and solid**, i.e.:

- Participates in stage dV accumulation
- Participates in fuel allocation: assign propellants per propellants list (electric gets ElectricCharge/xenon etc., liquid gets LOx etc.); never assign what is not in propellants
- No separate "electric branch" needed

## 3.4 Air-Breathing Engines

**No special handling for now**; keep existing logic. Air-breathing propellants (e.g., IntakeAir) are not in tanks; the current implementation is incomplete and deferred to a future iteration.

## 3.5 Settling Engine Identification

- **Thrust**: $\max\mathrm{Thrust} < 1\%$ of vehicle max thrust (and at least 0.1 kN), i.e. $\mathrm{Thrust}_{\mathrm{kN}} < \max(0.1,\, 0.01 \times \max\mathrm{Thrust}_{\mathrm{kN}})$
- **Fuel**: Self-contained (Part's Resources contain propellants required by the engine), exclude electric. No fixed resource names for compatibility with RO and other mods
- **Direction**: Thrust direction is the same as bottom-stage main thrust, dot product > 0.9

All above → `EngineRole = Settling`.

## 3.6 Escape Tower Identification

1. **Self-contained fuel**: Part's own Resources contain propellants required by the engine (not limited to SolidFuel, compatible with RO)
2. **Bound to Abort action group**: Iterate `engine.Actions`, check `(action.actionGroup & KSPActionGroup.Abort) != 0`
3. **Verdict**: Self-contained + bound to Abort → `EscapeTower`; Self-contained + not bound to Abort → Solid

## 3.7 Identification Flow Order

To avoid interference and ensure correct liquid thrust direction calculation, identification must follow this order:

| Step | Type | Condition | Order Reason |
|------|------|-----------|--------------|
| 1 | **Electric** | propellants contains ElectricCharge | No dependency, first |
| 2 | **EscapeTower** | Self-contained fuel + bound to Abort | Before other self-contained types; not bound → leave for step 5 |
| 3 | **Settling** | Thrust < 1% vehicle max + self-contained + dot product > 0.9 (same direction) + not electric | Before Retro so settling is excluded when computing bottom-stage main thrust |
| 4 | **Retro** | Thrust direction dot product with bottom-stage main < 0.8 (reverse or lateral) | Bottom-stage main taken from non-Electric/non-EscapeTower/non-Settling, max-thrust single engine; first three steps must complete |
| 5 | **Solid** | Self-contained + not EscapeTower + not Settling | After step 4, remaining self-contained → Solid (incl. RO) |
| 6 | **Main** | All others | Liquid from tanks |

## 3.8 Player Feedback and Manual Override

### Design

- Auto-identify each engine's `EngineRole` via code
- Provide "Engine Classification" button to open UI list; player can override per engine
- Save overrides to current vessel (e.g., part custom data or ship metadata); reused on next load

### UI Options Example

Each engine can be set to: Participate (liquid/solid/electric) / Retro / Settling / Escape tower / Do not participate. Liquid, solid, electric share the same dV and thrust logic; for UI display distinction, keep type labels without affecting calculation.

### Non-Participating Engines

Retro, Settling, EscapeTower:

- Do not count toward available dV
- Do not participate in fuel allocation (no propellants assigned)
- Mass and drag still counted for the whole vessel to keep estimate reasonable

**Fuel allocation logic**: Participating engines read required resources from their propellants and assign only those listed.

## 3.9 Part Exclusion (Mass Calculation)

The following parts **do not participate** in any mass, fuel, or dV calculation; filtered via `IsExcludedFromCalculation` in `VesselSourceService.BuildStatsFromParts` and related flows:

| Type | Identification |
|------|----------------|
| Launch clamps | `part.Modules` contains `LaunchClamp` or `ModuleLaunchClamp` |
| Modular Launch Pads (MLP) | manufacturer contains `Alphadyne`, or partName contains `AM.MLP`, `AM_MLP`, `_MLP` |
| Andromeda AeroSpace Agency (AASA) launch pads | partName contains `launch.pad` (e.g., `aasa.ag.launch.pad`) |

Excluded parts' mass, fuel, and engines are not included in statistics.

## 3.10 Current Implementation Status (2026-02)

**Engine classification:**

- Six role types auto-identified: Main / Solid / Electric / Retro / Settling / EscapeTower
- Calculation filter: only Main/Solid/Electric participate in dV and fuel allocation
- UI override: "Engine Classification" dialog allows per-engine role cycling or revert to auto
- Override persistence: stored by vessel key + part instance id to `GameData/OrbitalPayloadCalculator/PluginData/engine-role-overrides.cfg` (relative to KSP root)

**Part exclusion:**

- Implemented `IsExcludedFromCalculation`: excludes stock launch clamps, MLP (Alphadyne), AASA launch pads (`launch.pad`), etc., from mass and dV calculation.
