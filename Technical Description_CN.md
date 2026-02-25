# Orbital Payload Calculator 技术说明

本文档整合 Orbital Payload Calculator 的核心技术说明，包括：估计值与可计算值、地表理想 Delta-V 模型、引擎分类与识别。

---

# 第一部分：估计值与可计算值

本部分说明计算器中哪些数据是**根据物理公式精确计算**（随天体不同自动调整），哪些是**估计值**（使用启发式或经验公式）。

## 1.1 实际可计算的量（根据天体自动调整）

以下量由天体物理属性和轨道几何精确推导，不依赖估计：

| 数据 | 计算方式 | 使用的天体属性 |
|------|----------|----------------|
| **地表理想 Delta-V** | 两模型按 $`\alpha = a/r_0`$ 、 $`e = (r_{\mathrm{Ap}}-r_{\mathrm{Pe}})/(r_{\mathrm{Ap}}+r_{\mathrm{Pe}})`$ 自动选择。**模型 A**（α < 1.5 或 α ≤ 2 且 e < 0.1）：$`\sqrt{2\mu(1/r_0 - 1/(r_{\mathrm{Pe}}+r_{\mathrm{Ap}}))}`$ 。**模型 B**（其余）：霍曼 burn1+burn2 ， 椭圆轨道 +burn3 | `gravParameter`、`Radius`、rPe、rAp |
| **轨道速度** | $`v = \sqrt{\mu(2/r_{\mathrm{Pe}} - 1/a)}`$ | `gravParameter`、`Radius`、轨道高度 |
| **平面变轨 Delta-V** | $`2v \sin(\theta/2)`$ | 轨道速度、发射纬度、目标倾角 |
| **自转增益/损失** | 赤道速度 + 纬度修正 | `rotationPeriod`、`Radius` |
| **阶段 Delta-V** | 齐奥尔科夫斯基公式 | 质量、推进剂、Isp |
| **大气 Isp 混合** | 底级时间步进模拟采样 `GetPressure(h)`，按瞬时压力插值 `engine.atmosphereCurve` | `atmosphereDepth`、`atmospherePressureSeaLevel` |
| **仿真中重力损失** | 时间步进 $`g = \mu/R^2`$ | `gravParameter`、`Radius` |
| **仿真中大气密度** | $`\rho = p/(R_{\mathrm{air}} \cdot T)`$ | `GetPressure(h)`、`GetTemperature(h)` |
| **默认轨道高度** | 大气顶 + 10000 m | `atmosphereDepth` |
| **底级海平面 TWR** | 推力按所选天体海平面气压下的 Isp 计算；重力取 `body.GeeASL` | `atmospherePressureSeaLevel`、`GeeASL`、`engine.atmosphereCurve` |

### 地表理想 Delta-V 模型详情

两体、冲量式；忽略大气与重力损失。符号：$`\mu`$ = gravParameter ， $`r_0`$ = 天体半径 ， $`r_{\mathrm{Pe}}`$/$`r_{\mathrm{Ap}}`$ = 近点/远点半径 ， $`a = (r_{\mathrm{Pe}}+r_{\mathrm{Ap}})/2`$ ， $`\alpha = a/r_0`$ ， $`e = (r_{\mathrm{Ap}}-r_{\mathrm{Pe}})/(r_{\mathrm{Ap}}+r_{\mathrm{Pe}})`$ 。

**模型选择（α, e）：**

- **α < 1.5** → 模型 A（能量最优下界）
- **1.5 ≤ α ≤ 2.0**：若 e < 0.1 → 模型 A；若 e ≥ 0.1 → 模型 B
- **α > 2.0** → 模型 B（霍曼结构）

**模型 A：** 全局最小 Delta-V。椭圆：$`\sqrt{2\mu(1/r_0 - 1/(r_{\mathrm{Pe}}+r_{\mathrm{Ap}}))}`$ 。圆轨道：$`r_{\mathrm{Pe}}=r_{\mathrm{Ap}}=r`$ 时同上。

**模型 B：** 分段点火。圆轨道 ($`r_{\mathrm{Pe}}=r_{\mathrm{Ap}}=r`$)：$`\mathrm{burn1} = \sqrt{\mu/r_0} \cdot \sqrt{2r/(r_0+r)}`$ ， $`\mathrm{burn2} = \sqrt{\mu/r} \cdot (1-\sqrt{2r_0/(r_0+r)})`$ 。椭圆：在 $`r_{\mathrm{Pe}}`$ 处 burn1+burn2 ， 远点处 burn3。两模型当 $`r \to \infty`$ 时收敛为 $`\sqrt{2\mu/r_0}`$ （逃逸速度）。

完整推导与公式见 [第二部分：地表→轨道理想 Delta-V 模型](#第二部分地表轨道理想-delta-v-模型)（见下文）。

## 1.2 估计值（启发式 / 经验公式）

以下量因无法在编辑器中获取精确数据或计算代价高，而使用启发式或经验公式：

| 数据 | 估计方式 | 说明 |
|------|----------|------|
| **CdA（气动阻力面积）** | 编辑器和飞行模式一致：$`C_d \times \sqrt{m_{\mathrm{wet}}}`$ ， $`m_{\mathrm{wet}}`$ 为湿质量（吨）。用户输入 $`C_d`$ (0.3–2.0) 或默认 0.50/1.0/1.5（乐观/普通/悲观） | $`C_d`$ 系数 × √质量 启发式 |
| **重力损失**（无仿真时） | `FallbackEstimate` 经验公式 | 无推力/Isp 数据时使用的替代公式 |
| **大气损失**（无仿真时） | $`A_{\mathrm{atmo}} + B_{\mathrm{atmo}}`$ 经验公式 | 基于 $`g_N`$ 、 $`p_N`$ 、 $`d_N`$ 归一化缩放因子的拟合公式 |
| **姿态损失** | $`(A + B \sqrt{p_N} \cdot g_N) \times (1 + f_{\mathrm{inc}})`$ ， $`f_{\mathrm{inc}} = (i/90°) \times \lvert\cos\phi\rvert`$ 。 $`A`$ 、 $`B`$ 由模式及 $`g_N`$ 、 $`d_N`$ 、 $`p_N`$ 等缩放 | 无论仿真与否都使用经验系数；典型参考见下表 |
| **Turn Start Speed** | $`v_{\mathrm{auto}} = v_{\mathrm{base}} \times g_N^{0.25} \times (0.92 + 0.18 \ln(1+p_N) + 0.12 \cdot d_N^{0.3})`$ ；当底级 TWR ∈ [1.05, 3.0] 且用户未覆盖时， $`v_{\mathrm{turn}} = v_{\mathrm{auto}} \times \sqrt{\mathrm{TWR}_{\mathrm{ref}}/\mathrm{TWR}}`$ ；否则 $`v_{\mathrm{turn}} = v_{\mathrm{auto}}`$ ； $`v_{\mathrm{base}}`$ 按模式取 55/80/95 m/s， $`\mathrm{TWR}_{\mathrm{ref}}`$ 按模式取 1.4/1.5/1.6 | 基于重力、大气、底级推重比的启转速度 |
| **Turn Start Altitude** | $`h_{\mathrm{turn}} = \mathrm{Clamp}(h_{\mathrm{atmo}} \times (0.01 + 0.004 \ln(1+p_N)), 800, 22000) \times (v_{\mathrm{turn}}/80)`$ | 启转高度估计 |
| **转弯指数（重力转弯）** | 由启转速度线性拟合得出，典型值：底级 0.40/0.58/0.65，全段 0.45/0.70/0.80 | 经验值；控制俯仰转向速率 |

### 姿态损失典型参考

| 情况 | 典型姿态损失 |
|------|--------------|
| 优秀轨迹 | 10–30 m/s |
| 一般轨迹 | 30–80 m/s |
| 粗暴转弯 | 100–300 m/s |

## 1.3 估算模式参数

| 模式 | Cd | 启转速度基准 | TWR 参考 | 底级转弯指数 | 全段转弯指数 |
|------|----|-------------|----------|--------------|--------------|
| 乐观 | 0.50 | 55 m/s | 1.4 | 0.40 | 0.45 |
| 普通 | 1.0 | 80 m/s | 1.5 | 0.58 | 0.70 |
| 悲观 | 1.5 | 95 m/s | 1.6 | 0.65 | 0.80 |

## 1.4 参数优先级

**高级设置** 中的重力/大气/姿态损失覆盖、转弯速度、Cd 系数、重力转弯高度 **优先级始终高于** 悲观/普通/乐观估算按钮的默认值。

## 1.5 简要总结

- **可算的量**：地表理想 Delta-V（模型 A/B）、轨道速度、平面变轨、自转、阶段 Delta-V、重力场、大气压强/温度/密度、大气混合 Isp、底级海平面 TWR。这些都由天体数据驱动，随天体不同自动变化。
- **估计的量**：Cd 系数与 CdA、重力/大气/姿态损失（尤其是 Fallback 和姿态项）、启转速度和高度、转弯指数。启转速度在无用户覆盖时由模式基准 + 天体缩放 + 底级 TWR 共同决定；TWR 在 1.05～3.0 范围内时， $`v_{\mathrm{turn}} \propto \sqrt{\mathrm{TWR}_{\mathrm{ref}}/\mathrm{TWR}}`$ ， $`\mathrm{TWR}_{\mathrm{ref}}`$ 按模式取 1.4/1.5/1.6。
- **编辑器和飞行模式**：CdA 使用相同启发式，不基于部件几何计算。

---

# 第二部分：地表→轨道理想 Delta-V 模型

两体、无大气、无重力损失。

## 2.1 适用范围

- 任意天体
- 两体模型
- 忽略大气阻力
- 忽略重力损失
- 忽略自转
- 冲量式机动

## 2.2 基本符号

$`\mu`$ = 天体引力参数 (gravParameter)  
$`r_0`$ = 天体半径（发射点到中心距离）  
$`r`$ = 目标圆轨道半径 = $`r_0`$ + 目标高度  
$`r_{\mathrm{Pe}}`$ = 目标近点半径  
$`r_{\mathrm{Ap}}`$ = 目标远点半径  
$`a`$ = 半长轴 = $`(r_{\mathrm{Pe}} + r_{\mathrm{Ap}}) / 2`$  

$`\alpha`$ = $`a / r_0`$ （用于模型选择）  
$`e`$ = $`(r_{\mathrm{Ap}} - r_{\mathrm{Pe}}) / (r_{\mathrm{Ap}} + r_{\mathrm{Pe}})`$ （椭圆偏心率）  

## 2.3 模型 A：能量最小模型（全局最优）

### 推导基础

初始能量：$`E_0 = -\mu/r_0`$  

目标圆轨道能量：$`E = -\mu/(2r)`$  

目标椭圆轨道能量：$`E = -\mu/(2a)`$ ， 其中 $`a = (r_{\mathrm{Pe}} + r_{\mathrm{Ap}})/2`$  

所需能量增量：$`\Delta E = E - E_0`$  

由 $`\frac{1}{2}v^2 = \Delta E`$ 得：

### 最终公式

**圆轨道：**

$$
\text{Delta-V}_A = \sqrt{ 2\mu \left( \frac{1}{r_0} - \frac{1}{2r} \right) }
$$

**椭圆轨道：**

$$
\text{Delta-V}_A = \sqrt{ 2\mu \left( \frac{1}{r_0} - \frac{1}{r_\mathrm{Pe} + r_\mathrm{Ap}} \right) }
$$

### 特性

- 全局最小 Delta-V  
- 连续推力极限解  
- 不假设霍曼结构  
- 任意目标半径适用  

当 $`r \to \infty`$ 时：$`\text{Delta-V}_A \to \sqrt{2\mu/r_0}`$ （逃逸速度）

## 2.4 模型 B：霍曼结构模型（工程结构解）

### 情况 1：目标为圆轨道 (rPe = rAp = r)

第一段（进入转移椭圆）：

$$
\mathrm{burn1} = \sqrt{\frac{\mu}{r_0}} \cdot \sqrt{ \frac{2r}{r_0 + r} }
$$

第二段（在 r 处圆化）：

$$
\mathrm{burn2} = \sqrt{\frac{\mu}{r}} \cdot \left( 1 - \sqrt{ \frac{2r_0}{r_0 + r} } \right)
$$

总 Delta-V：$`\text{Delta-V}_B = \mathrm{burn1} + \mathrm{burn2}`$

### 情况 2：目标为椭圆轨道 (rPe < rAp)

$$
\mathrm{burn1} = \sqrt{\frac{\mu}{r_0}} \cdot \sqrt{ \frac{2r_\mathrm{Pe}}{r_0 + r_\mathrm{Pe}} }
$$

$$
\mathrm{burn2} = \sqrt{\frac{\mu}{r_\mathrm{Pe}}} \cdot \left( 1 - \sqrt{ \frac{2r_0}{r_0 + r_\mathrm{Pe}} } \right)
$$

$$
\mathrm{burn3} = \sqrt{ \frac{2\mu\,r_\mathrm{Ap}}{ r_\mathrm{Pe}(r_\mathrm{Pe} + r_\mathrm{Ap}) } } - \sqrt{ \frac{\mu}{r_\mathrm{Pe}} }
$$

总 Delta-V：$`\text{Delta-V}_B = \mathrm{burn1} + \mathrm{burn2} + \mathrm{burn3}`$

### 特性

- 结构清晰  
- 可直接对应机动步骤  
- 适用于远轨道  
- 当 $`r \gg r_0`$ 或 $`r_{\mathrm{Ap}} \gg r_{\mathrm{Pe}}`$ 时与模型 A 收敛  

当 $`r \to \infty`$ 时：$`\text{Delta-V}_B \to \sqrt{2\mu/r_0}`$

## 2.5 模型选择边界（任意天体通用）

定义：$`\alpha = a/r_0`$ （ $`a`$ 为半长轴）， $`e = (r_{\mathrm{Ap}}-r_{\mathrm{Pe}})/(r_{\mathrm{Ap}}+r_{\mathrm{Pe}})`$

### 1. 近地轨道区域

**α < 1.5**

特点：
- 目标仍在引力井底部
- 霍曼结构引入额外结构误差
- 低轨椭圆可能略增加 Delta-V

推荐：使用模型 A

### 2. 中间轨道区域

**1.5 ≤ α ≤ 2.0**

特点：
- 两模型差异快速衰减
- 低偏心椭圆（e < 0.1）Delta-V 差异可忽略
- 高偏心椭圆（e ≥ 0.1）建议用模型 B

推荐：
- 低偏心椭圆：模型 A
- 高偏心椭圆：模型 B

### 3. 高升轨 / 远轨道区域

**α > 2.0**

特点：
- 轨道接近引力井边缘
- 两模型几乎等价
- 霍曼结构更符合实际操作

推荐：使用模型 B

## 2.6 统一决策流程（椭圆轨道版）

```text
计算半长轴 a = (rPe + rAp) / 2
计算相对比率 α = a / r₀
计算偏心率 e = (rAp − rPe) / (rAp + rPe)

1. 近地轨道区域
若 α < 1.5：
    使用模型 A

2. 中间轨道区域
否则若 1.5 ≤ α ≤ 2.0：
    若 e < 0.1：
        使用模型 A
    否则：
        使用模型 B

3. 高升轨 / 远轨道区域
否则： α > 2.0
    使用模型 B
```

## 2.7 总结

**模型 A：**
- 能量极限
- 数学最优
- 适合作为理论下界
- 一次 Delta-V 注入即可，不关心机动结构

**模型 B：**
- 工程结构
- 分段机动
- 适合远轨与任务规划
- 低偏心高轨椭圆也可用
- 更贴近游戏内机动节点（Maneuver Node）或实际分段操作

两者在 $`r \to \infty`$ 或高远轨椭圆时收敛为 $`\sqrt{2\mu/r_0}`$ ， 即逃逸速度。

---

# 第三部分：引擎分类与识别技术方案

本文档描述 OrbitalPayloadCalculator 中引擎类型的自动识别规则、玩家反馈机制，以及各类引擎在 Delta-V 计算和燃料分配中的处理方式，用以适配原版、RO 及多燃料引擎等。

## 3.1 引擎角色定义

| 角色 | 说明 | 参与 Delta-V | 分配燃料 | 计入质量/阻力 |
|------|------|---------|----------|---------------|
| **Main** | 液推（从储箱取燃料） | ✓ | ✓ | ✓ |
| **Solid** | 固推（自带燃料，含 RO） | ✓ | ✓ | ✓ |
| **Electric** | 电推/离子 | ✓ | ✓ | ✓ |
| **Retro** | 反推 | ✗ | ✗ | ✓ |
| **Settling** | 沉底用 | ✗ | ✗ | ✓ |
| **EscapeTower** | 逃逸塔 | ✗ | ✗ | ✓ |

不参与 Delta-V、不分配燃料的引擎（Retro/Settling/EscapeTower），其质量与阻力仍需计入，否则整体估算会偏差。参与分配的引擎按 propellants 只分配其列出的推进剂。

## 3.2 反推引擎识别

### 原理

反推的推力方向与载具底级主推方向不一致（点积 < 0.8，含反向与侧向）。「底级主推方向」取参与 Delta-V 计算的引擎（液推、固推、电推）中 StageNumber 最大（最底级，即最先点火）且推力最大的单台引擎的推力方向。

### 推力方向获取

- KSP 使用 `ModuleEngines.thrustTransforms`：每个 Transform 的 **-forward**（或按 Unity 约定为推力出口方向）表示推力方向
- 需转换到**世界空间**：`part.transform.TransformDirection(-thrustTransform.forward)` 或等价写法
- `thrustTransforms` 为空时，可用 `part.transform.up` 等作为后备

### 编辑器模式与飞行模式

- **飞行模式**：`Part` 和 `Transform` 已挂载到场景，可直接读取
- **编辑器模式**：`EditorLogic.fetch.ship` 中的 Part 同样有完整的 `transform` 和 `attachNodes`，`thrustTransforms` 在 Part 加载后可用

结论：**编辑器和飞行模式下，只要 Part 已加载，推力方向均可正确识别**。

### 判定逻辑

1. 确定底级主推方向：在**尚未被标为 Electric、EscapeTower、Settling** 的引擎中，取 StageNumber 最大（最底级）且推力最大的**单台**引擎，以其推力方向作为参考方向
2. 对每个引擎：若其推力方向与底级主推方向的点积 < 0.8（夹角 > 约 37°，含反向与侧向），则判为 **Retro**

## 3.3 电推引擎识别

**只要 propellants 中包含 ElectricCharge，即视为电推引擎。**

实现示例：`engine.propellants` 中任意 `prop.name == ElectricCharge` → `EngineRole = Electric`。

### 计算方式：与液推、固推一样计算

电推使用常规火箭方程，公式相同：

$$
\text{Delta-V} = I_{\mathrm{sp}} \cdot g_0 \cdot \ln(m_{\mathrm{wet}} / m_{\mathrm{dry}})
$$

区别在于：

- 推进剂为 XenonGas + ElectricCharge 等，而非 LiquidFuel/Oxidizer
- 燃料（含电池）在 Part 和资源系统中已有记录

电推与液推、固推使用同一套 Delta-V 计算逻辑，即：

- 参与各阶段的 Delta-V 累加
- 参与燃料分配：按 propellants 分配其所需推进剂（电推分 ElectricCharge/氙等，液推分液氧等），propellants 没有的绝不分配
- 无需单独的 ***电推分支***

## 3.4 吸气式引擎

**暂不做特殊处理**。吸气式推进剂（如 IntakeAir）不在储箱中，当前实现不分配此类推进剂。

## 3.5 沉底引擎识别

- **推力**：$`\max\mathrm{Thrust} < 1\%`$ 全箭最大推力（且至少 0.1 kN），即 $`\mathrm{Thrust}_{\mathrm{kN}} < \max(0.1,\, 0.01 \times \max\mathrm{Thrust}_{\mathrm{kN}})`$
- **燃料**：自身携带燃料（Part 的 Resources 中含该引擎 propellants 所需的推进剂），排除电推。不限定具体资源名，以兼容 RO 等 mod 的不同燃料类型
- **方向**：推力方向与底级主推方向同向，点积 > 0.9

满足以上条件 → `EngineRole = Settling`。

## 3.6 逃逸塔识别

1. **是否自带燃料**：Part 自身 Resources 中含该引擎 propellants 所需推进剂（不限定 SolidFuel，兼容 RO）
2. **是否绑定 Abort 动作组**：遍历 `engine.Actions`，检查 `(action.actionGroup & KSPActionGroup.Abort) != 0`
3. **判定**：自带燃料 + 绑定 Abort → `EscapeTower`；自带燃料 + 未绑定 Abort → 固推（Solid）

## 3.7 识别流程顺序

为避免串扰、保证液推方向计算正确，须按以下顺序判定：

| 步骤 | 类型 | 判定条件 | 顺序理由 |
|------|------|----------|----------|
| 1 | **Electric** | propellants 含 ElectricCharge | 无依赖，最先 |
| 2 | **EscapeTower** | 自带燃料 + 绑定 Abort | 优先于其它自带燃料类型；未绑 Abort 不标，留步骤 5 |
| 3 | **Settling** | 推力 < 1% 全箭最大推力 + 自带燃料 + 点积 > 0.9（同向）+ 非电推 | 先于 Retro，以便底级主推方向计算时排除沉底 |
| 4 | **Retro** | 推力方向与底级主推点积 < 0.8（反向或侧向） | 底级主推取自「非 Electric / 非 EscapeTower / 非 Settling」中底级推力最大单台，故前三步须先完成 |
| 5 | **Solid** | 自带燃料 + 非 EscapeTower + 非 Settling | 前四步完成后，「剩余自带燃料」判为固推（含 RO） |
| 6 | **Main** | 其余 | 从储箱获取燃料的液推 |

## 3.8 玩家反馈与手动覆盖

引擎角色由代码自动识别，支持玩家手动覆盖；覆盖结果保存在 part custom data 或 ship 元数据中，随船只加载。

### 不参与计算的引擎

Retro、Settling、EscapeTower 三类：

- 不计入可用 Delta-V
- 不参与燃料分配（不向它们分配任何推进剂）
- 质量和阻力仍计入全船，保证估算合理

**燃料分配逻辑**：参与分配的引擎按各自的 propellants 读取所需资源，只分配其列出的推进剂。

## 3.9 部件排除（质量计算）

以下部件**不参与**任何质量、燃料、Delta-V 计算，在 `VesselSourceService.BuildStatsFromParts` 等流程中通过 `IsExcludedFromCalculation` 过滤：

| 类型 | 判定方式 |
|------|----------|
| 发射塔架 | `part.Modules` 含 `LaunchClamp` 或 `ModuleLaunchClamp` |
| Modular Launch Pads (MLP) | manufacturer 含 `Alphadyne`，或 partName 含 `AM.MLP`、`AM_MLP`、`_MLP` |
| Andromeda AeroSpace Agency (AASA) 发射台 | partName 含 `launch.pad`（如 `aasa.ag.launch.pad`） |

排除后，这些部件的质量、燃料、引擎均不会被计入统计数据。

## 3.10 燃料导管（FTX-2 External Fuel Duct）建模

Kerbal X 等载具使用燃料导管将捆绑助推器的燃料导向主堆芯，主推优先消耗助推器燃料后再分离空助推器。若不建模燃料导管，燃料归属可能错误，导致 Delta-V 与分离质量计算偏差。

### 识别与解析

- **部件识别**：`part.partInfo.name == "fuelLine"`，或 `part.Modules` 含 `CModuleFuelLine` / `ModuleFuelLine` / `FuelLine`，或 `part` 为 `CompoundPart` 且含上述模块
- **流向**：`part.parent` = Source（燃料由此流出），另一端 = Target（燃料流入）。KSP CompoundPart 的 `attachNodes` 常为空，需通过反射获取另一端的 Part 引用
- **数据结构**：`FuelLineEdge { SourcePartId, TargetPartId }`

### 燃料分配

1. 构建燃料流图 `flowGraph: sourcePartId -> [targetPartIds]`
2. 对每个作为燃料管源的储箱，沿图 BFS 找到可达的、有 Delta-V 引擎的阶段，取其 stageNumber 最大者作为目标阶段
3. 将该储箱的推进剂从原阶段移除，加入目标阶段；同时累加 `BoosterPhasePropellantTons`（助推器阶段推进剂量）
4. `AssignPropellantByFuelLines` 在常规燃料累加之后、`RedistributePropellant` 之前执行
5. `RedistributePropellant` 迁移燃料时同步迁移 `BoosterPhasePropellantTons`

### 分离组调整

- 燃料管源储箱的燃料已归属到主推所在阶段，分离时助推器已空。故 `GroupLiquidPropellantTons` 不包含这些储箱的液推质量，避免重复扣除；分离组内若有任一根燃料导管源，则该组 `GroupLiquidPropellantTons = 0`
- **重叠去重**：径向分离组与轴向分离组的释放集合可能重叠（如 6 个助推器被 2 个轴向分离器的 released 集合包含）。用 `UniqueDroppedDryMassTons` 对释放部件 ID 去重后只计一次干质量；仿真中通过 `ReleasedPartIds` 与 `droppedPartIds` 避免重复扣除

### 分阶段燃料分配（芦笋式仿真）

- **BoosterPhasePropellantTons**：从燃料导管源转移的推进剂总量，按实际储箱 `amount × density` 累加，**随载具动态计算**（Kerbal X 约 12 t 仅为示例）
- **BoosterEngineIndices**：径向助推器分离组中的发动机索引；判定条件：单发动机且干质量 < 15 t（启发式阈值，用于区分径向助推器与轴向级）
- **仿真分配**：先将 `BoosterPhasePropellantTons` 按推力比例分配给所有液推；剩余推进剂仅分配给芯级发动机。助推器发动机在烧完其份额后熄火并抛离，与真实芦笋式时序一致
