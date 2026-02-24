# 引擎分类与识别技术方案

本文档描述 OrbitalPayloadCalculator 中引擎类型的自动识别规则、玩家反馈机制，以及各类引擎在 dV 计算和燃料分配中的处理方式。因此要做出改进，以适配原版、RO 及多燃料引擎等等。

---

## 一、背景与问题

当前代码仅区分固推（IsSolid）与液推，未识别反推、沉底、逃逸塔、电推等类型，导致：

- **液推燃料被分配给反推**：反推与液推按推力比例瓜分液体燃料，造成可用 dV 估算错误
- **推力详情错误**：反推等引擎的推力被错误计入总推力或展示中
- **不应参与计算的引擎被计入**：反推、沉底、逃逸塔等不应贡献 dV，却参与了计算

---

## 二、引擎角色定义

| 角色 | 说明 | 参与 dV | 分配燃料 | 计入质量/阻力 |
|------|------|---------|----------|---------------|
| **Main** | 液推（从储箱取燃料） | ✓ | ✓ | ✓ |
| **Solid** | 固推（自带燃料，含 RO） | ✓ | ✓ | ✓ |
| **Electric** | 电推/离子 | ✓ | ✓ | ✓ |
| **Retro** | 反推 | ✗ | ✗ | ✓ |
| **Settling** | 沉底用 | ✗ | ✗ | ✓ |
| **EscapeTower** | 逃逸塔 | ✗ | ✗ | ✓ |

不参与 dV、不分配燃料的引擎（Retro/Settling/EscapeTower），其质量与阻力仍需计入，否则整体估算会偏差。参与分配的引擎按 propellants 只分配其列出的推进剂。

---

## 三、反推引擎识别

### 3.1 原理

反推的推力方向与载具液推方向相反。此处的「液推方向」指参与 dV 计算的引擎（液推、固推、电推）在最底部分级的推力方向。

### 3.2 推力方向获取

- KSP 使用 `ModuleEngines.thrustTransforms`：每个 Transform 的 **-forward**（或按 Unity 约定为推力出口方向）表示推力方向
- 需转换到**世界空间**：`part.transform.TransformDirection(-thrustTransform.forward)` 或等价写法
- `thrustTransforms` 为空时，可用 `part.transform.up` 等作为后备

### 3.3 编辑器模式与飞行模式

- **飞行模式**：`Part` 和 `Transform` 已挂载到场景，可直接读取
- **编辑器模式**：`EditorLogic.fetch.ship` 中的 Part 同样有完整的 `transform` 和 `attachNodes`，`thrustTransforms` 在 Part 加载后可用

结论：**编辑器和飞行模式下，只要 Part 已加载，推力方向均可正确识别**。需注意编辑器中的船可能尚未“竖起来”，但 part 间的相对朝向是确定的，推力方向相对载具的几何关系正确即可。

### 3.4 判定逻辑

1. 确定液推方向：在**尚未被标为 Electric、EscapeTower、Settling** 的引擎中，取 inverseStage 最大且推力最大的若干引擎，对其推力方向做加权平均（即从“将参与 dV 计算的液推、固推、电推”的引擎中提取）
2. 对每个引擎：若其推力方向与液推方向的点积 < -0.7（约 135° 以上夹角），则判为 **Retro**

---

## 四、电推引擎识别

### 4.1 识别规则

**只要 propellants 中包含 ElectricCharge，即视为电推引擎。**

实现示例：`engine.propellants` 中任意 `prop.name == "ElectricCharge"` → `EngineRole = Electric`。

### 4.2 计算方式：与液推、固推一样计算

电推使用常规火箭方程，公式相同：`dV = Isp * g0 * ln(m_wet / m_dry)`。区别在于：

- 推进剂为 XenonGas + ElectricCharge 等，而非 LiquidFuel/Oxidizer
- 燃料（含电池）在 Part 和资源系统中已有记录

**建议：电推与液推、固推采用同一套 dV 计算逻辑**，即：

- 参与各阶段的 dV 累加
- 参与燃料分配：按 propellants 分配其所需推进剂（电推分 ElectricCharge/氙等，液推分液氧等），propellants 没有的绝不分配
- 无需单独的“电推分支”


---

## 五、吸气式引擎

**暂不做特殊处理**，保持现有逻辑：该怎么样就怎么样。吸气式推进剂（如 IntakeAir）不在储箱中，当前实现本身就不完整，留待后续迭代。

---

## 六、沉底引擎识别

- **推力**：maxThrust 为个位数量级（如 < 5–10 kN）
- **燃料**：自身携带燃料（Part 的 Resources 中含该引擎 propellants 所需的推进剂），排除电推。不限定具体资源名，以兼容 RO 等 mod 的不同燃料类型
- **方向**：推力方向与液推方向非一致

满足以上条件 → `EngineRole = Settling`。

---

## 七、逃逸塔识别

1. **是否自带燃料**：Part 自身 Resources 中含该引擎 propellants 所需推进剂（不限定 SolidFuel，兼容 RO）
2. **是否绑定 Abort 动作组**：遍历 `engine.Actions`，检查 `(action.actionGroup & KSPActionGroup.Abort) != 0`
3. **判定**：自带燃料 + 绑定 Abort → `EscapeTower`；自带燃料 + 未绑定 Abort → 固推（Solid）

---

## 八、识别流程顺序

为避免串扰、保证液推方向计算正确，须按以下顺序判定：

| 步骤 | 类型 | 判定条件 | 顺序理由 |
|------|------|----------|----------|
| 1 | **Electric** | propellants 含 ElectricCharge | 无依赖，最先 |
| 2 | **EscapeTower** | 自带燃料 + 绑定 Abort | 优先于其它自带燃料类型；未绑 Abort 不标，留步骤 5 |
| 3 | **Settling** | 推力 < 阈值 + 自带燃料 + 非电推 | 先于 Retro，以便液推方向计算时排除沉底 |
| 4 | **Retro** | 推力方向与液推相反 | 液推方向取自「非 Electric / 非 EscapeTower / 非 Settling」引擎，故前三步须先完成 |
| 5 | **Solid** | 自带燃料 + 非 EscapeTower + 非 Settling | 前四步完成后，「剩余自带燃料」判为固推（含 RO） |
| 6 | **Main** | 其余 | 从储箱获取燃料的液推 |

---

## 九、玩家反馈与手动覆盖

### 9.1 设计思路

- 先用代码自动识别每个引擎的 `EngineRole`
- 提供“引擎分类”按钮，打开 UI 列表，玩家可逐项覆盖
- 覆盖结果保存到当前船只（如 part custom data 或 ship 元数据），下次加载沿用

### 9.2 UI 选项示例

每台引擎可选：参与计算（液推/固推/电推）/ 反推 / 沉底 / 逃逸塔 / 不参与计算。液推、固推、电推按同一套逻辑参与 dV 和推力计算；若需在 UI 上区分展示，可保留类型标签但不影响计算

### 9.3 不参与计算的引擎

Retro、Settling、EscapeTower 三类：

- 不计入可用 dV
- 不参与燃料分配（不向它们分配任何推进剂）
- 质量和阻力仍计入全船，保证估算合理

**燃料分配逻辑**：参与分配的引擎按各自的 propellants 读取所需资源，只分配其列出的推进剂。电推的 propellants 含 ElectricCharge 等；液推从储箱分配其所需资源，固推一般自带燃料，无需从共享池分配。引擎的 propellants 里没有的，自然不会分配。

---

## 十、当前实现状态（2026-02）

- 已实现六类角色自动识别：Main / Solid / Electric / Retro / Settling / EscapeTower。
- 已实现计算过滤：仅 Main/Solid/Electric 参与 dV 与燃料分配。
- 已实现 UI 覆盖：提供“引擎分类”弹窗，可逐引擎循环角色或恢复自动。
- 已实现覆盖持久化：按 vessel key + part instance id 存储到 `GameData/OrbitalPayloadCalculator/PluginData/engine-role-overrides.cfg`。
