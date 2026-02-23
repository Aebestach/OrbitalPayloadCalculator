# Surface → Orbit Ideal Δv Models (Two-Body, No Atmosphere, No Gravity Loss)

---

## Scope

- Any celestial body
- Two-body model
- Neglect atmospheric drag
- Neglect gravity loss
- Neglect rotation
- Impulsive maneuvers

---

# Symbols

μ     = gravitational parameter (gravParameter)  
r₀    = body radius (distance from launch point to center)  
r     = target circular orbit radius = r₀ + altitude  
rPe   = periapsis radius  
rAp   = apoapsis radius  
a     = semi-major axis = (rPe + rAp) / 2  

α     = a / r₀   (for model selection)  
e     = (rAp − rPe) / (rAp + rPe)   (eccentricity)  

---

# Model A: Energy-Minimum (Global Optimal)

## Derivation

Initial energy: E₀ = −μ/r₀  

Target circular orbit: E = −μ/(2r)  

Target elliptic orbit: E = −μ/(2a), where a = (rPe + rAp)/2  

Energy increment: ΔE = E − E₀  

From ½v² = ΔE:

## Final Formulas

### Circular Orbit

\[
\Delta v_A = \sqrt{ 2μ \left( \frac{1}{r_0} - \frac{1}{2r} \right) }
\]

### Elliptic Orbit

\[
\Delta v_A = \sqrt{ 2μ \left( \frac{1}{r_0} - \frac{1}{r_\mathrm{Pe} + r_\mathrm{Ap}} \right) }
\]

---

## Properties

✔ Global minimum Δv  
✔ Continuous-thrust limit solution  
✔ No Hohmann assumption  
✔ Valid for any target radius  

As r → ∞: Δv_A → √(2μ/r₀) (escape speed)

---

# Model B: Hohmann-Structured (Engineering Solution)

## Case 1: Circular Target (rPe = rAp = r)

First burn (to transfer ellipse):

\[
\mathrm{burn1} = \sqrt{\frac{\mu}{r_0}} \cdot \sqrt{ \frac{2r}{r_0 + r} }
\]

Second burn (circularize at r):

\[
\mathrm{burn2} = \sqrt{\frac{\mu}{r}} \cdot \left( 1 - \sqrt{ \frac{2r_0}{r_0 + r} } \right)
\]

Total: Δv_B = burn1 + burn2

---

## Case 2: Elliptic Target (rPe < rAp)

\[
\mathrm{burn1} = \sqrt{\frac{\mu}{r_0}} \cdot \sqrt{ \frac{2r_\mathrm{Pe}}{r_0 + r_\mathrm{Pe}} }
\]

\[
\mathrm{burn2} = \sqrt{\frac{\mu}{r_\mathrm{Pe}}} \cdot \left( 1 - \sqrt{ \frac{2r_0}{r_0 + r_\mathrm{Pe}} } \right)
\]

\[
\mathrm{burn3} = \sqrt{ \frac{2μ\,r_\mathrm{Ap}}{ r_\mathrm{Pe}(r_\mathrm{Pe} + r_\mathrm{Ap}) } } - \sqrt{ \frac{\mu}{r_\mathrm{Pe}} }
\]

Total: Δv_B = burn1 + burn2 + burn3

---

## Properties

✔ Clear structure  
✔ Direct mapping to maneuver steps  
✔ Suitable for distant orbits  
✔ Converges to Model A when r ≫ r₀ or rAp ≫ rPe  

As r → ∞: Δv_B → √(2μ/r₀)

---

# Model Selection Boundaries (Any Celestial Body)

Define: α = a/r₀ (a = semi-major axis), e = (rAp−rPe)/(rAp+rPe)

---

## 1️⃣ Low Orbit Region

**α < 1.5**

- Target near bottom of gravity well
- Hohmann structure adds structural error
- Low-eccentricity ellipse may slightly increase Δv

Recommendation: ✔ Use Model A

---

## 2️⃣ Intermediate Orbit Region

**1.5 ≤ α ≤ 2.0**

- Difference between models decays quickly
- Low eccentricity (e < 0.1): negligible Δv difference
- High eccentricity (e ≥ 0.1): prefer Model B

Recommendation:
- ✔ Low eccentricity: Model A
- ✔ High eccentricity: Model B

---

## 3️⃣ High / Distant Orbit Region

**α > 2.0**

- Orbit near gravity well edge
- Models nearly equivalent
- Hohmann structure matches practical operations

Recommendation: ✔ Use Model B

---

# Unified Decision Flow (Elliptic Orbits)

```text
Compute semi-major axis a = (rPe + rAp) / 2
Compute ratio α = a / r₀
Compute eccentricity e = (rAp − rPe) / (rAp + rPe)

# 1️⃣ Low orbit
if α < 1.5:
    use Model A

# 2️⃣ Intermediate orbit
else if 1.5 ≤ α ≤ 2.0:
    if e < 0.1:
        use Model A
    else:
        use Model B

# 3️⃣ High / distant orbit
else:  # α > 2.0
    use Model B
```

---

# Summary

**Model A:**
- Energy limit
- Mathematically optimal
- Best as theoretical lower bound
- Single Δv injection; no concern for maneuver structure

**Model B:**
- Engineering structure
- Staged maneuvers
- Suitable for distant orbits and mission planning
- Works for low-eccentricity high orbits
- Closer to in-game Maneuver Node or practical staged operations

Both converge to √(2μ/r₀) (escape speed) as r → ∞ or for high distant elliptical orbits.
