# Orbital Payload Calculator
# 轨道运载能力计算器

<div align="center">
    
<img src="https://i.imgur.com/ryDQSmm.jpg" alt="Banner"/>

[![License](https://img.shields.io/github/license/Aebestach/OrbitalPayloadCalculator)](LICENSE)
[![Release](https://img.shields.io/github/v/release/Aebestach/OrbitalPayloadCalculator)](https://github.com/Aebestach/OrbitalPayloadCalculator/releases)

[English](README.md) | [中文](README_CN.md)

</div>

---

## 简介
在坎巴拉太空计划 (KSP) 里，设计火箭往往不是“工程学”，而是“玄学”——拍脑袋堆燃料、凭感觉上级数，发射时祈祷别炸，入轨后才发现：

- 要么 Delta-V 算少了，差一点点就能到 Mun，只能看着绿人流泪；
- 要么 Delta-V 多到离谱，油箱富得流油，去个 Mun 像是准备环游太阳系；
- 最尴尬的是，好不容易入轨了，却发现载荷重得像铅块，燃料表一格一格往下掉，心也跟着一起掉。

令人惊讶的是，KSP 已经十年之久了，却很少有人做过专门解决这个问题的工具。自己在设计火箭时也深有体会：不断对照Delta-V表计算载荷，既费脑又容易出错。正是这些需求和“痛点”，促使 **Orbital Payload Calculator** 的诞生。

现在，**Orbital Payload Calculator** 可以帮你把“玄学造箭”升级成“理性工程”。
它能提前算清载荷质量和所需 Delta-V，让你不再靠感觉堆燃料，也不再为了保险多带两罐“心理安慰油”。

从此，你的火箭不是“差一点”，也不是“富得流油”，而是——刚刚好，优雅入轨，潇洒去 Mun。

**Orbital Payload Calculator** 是 **坎巴拉太空计划 (KSP)** 的一款工具模组，用于估算火箭在指定目标轨道下的最大运载能力。它通过基于物理的上升模拟来计算重力、大气阻力和转向损失，帮助你在发射前精确设计运载火箭，告别盲目猜测。

本模组支持 **编辑器 (VAB/SPH)** 和 **飞行场景**（仅限着陆或发射前状态）。


<div align="center">
    <img src="https://i.imgur.com/vLPrqJE.png" alt="UI Screenshot"/>
</div>


## 功能特性

-   **运载能力估算**：计算能够送入目标轨道的最大有效载荷质量（吨），通过迭代算法匹配可用 Delta-V 与入轨需求。
-   **高级损失模型**：基于天体现实大气曲线的上升模拟；提供悲观/普通/乐观三种估算模式；支持高级参数覆盖。
-   **多天体与自转支持**：支持任意天体及发射纬度；考虑顺行/逆行发射影响。
-   **分级分析**：支持多级火箭；自动识别引擎角色（Main / Solid / Electric / Retro / Settling / EscapeTower）；检测分离组；支持引擎角色手动覆盖。
-   **可配置目标**：设置远点、近点、倾角、发射纬度；单位支持 m / km / Mm；自动计算平面变更 Delta-V。
-   **理想 Delta-V 模型**：地表→轨道理想 Delta-V（两体、冲量式），根据轨道参数自动选择能量最优或霍曼结构模型。

**技术实现细节**详见 [技术说明](Technical%20Description_CN.md)。

## 依赖要求

-   **Click Through Blocker**

## 兼容性说明

- **天体与气动模组**：原版天体、RSS（Real Solar System）、FAR（Ferram Aerospace Research）等在理论上均可支持计算。实际运载能力取决于真实飞行轨迹，计算结果仅供参考；在多数场景下预测精度尚可。
- **发射台模组**：本 Mod 已尽可能降低 **AlphaMensaes Modular Launch Pads** 等发射台模组对计算的影响（但可能仍有遗漏）。建议在计算时**不要**将发射塔架、发射台等与火箭无关的死重计入载具结构，以获得更准确的运载能力估算。

## 安装说明

1.  下载最新版本 Release。
2.  将 `GameData/OrbitalPayloadCalculator` 文件夹复制到 KSP 安装目录的 `GameData` 文件夹下。
3.  确保已安装 **Click Through Blocker**。

## 使用指南

### 打开计算器
-   **快捷键：** 按下 **Left Alt + P** (默认) 切换窗口显示。
-   **工具栏：** 点击应用启动器 (AppLauncher) 中的 **Orbital Payload Calculator** 图标。

### 操作流程
1.  **选择天体：** 选择发射所在的星球（例如 Kerbin）。
2.  **设置轨道：** 输入期望的 **远点**、**近点**、**倾角** 和 **发射纬度**。
3.  **发射纬度：**
    -   编辑器中：手动输入预计的发射纬度（范围 -90°～90°），超出范围会告警且无法计算。
    -   飞行中：自动读取当前载具的纬度，以度分秒 (DMS) 格式显示，带 N/S 方位。
4.  **损失设置：**
    -   **悲观估算 / 普通估算 / 乐观估算：** 三选一，均使用内置上升模拟。普通估算为默认；乐观假设更优化的上升轨迹；悲观给出保守的高损失估算。
    -   **高级设置：** 任意情况下可展开，可设置转弯速度、阻力系数 (Cd，建议范围 0.3–2.0)、重力转弯高度及重力/大气/姿态损失覆盖；**高级参数优先级高于悲观/普通/乐观估算**。
    -   **货舱当作整流罩：** 开启时，货舱质量在其分离阶段按整流罩排除；若入轨后仍保留货舱，请关闭。
    -   **姿态损失参考：** 优秀轨迹 10–30 m/s，一般轨迹 30–80 m/s，粗暴转弯 100–300 m/s。
5.  **引擎分类：** 在分级详情中可点击 **引擎分类** 按钮，逐引擎覆盖角色（如将反推标为参与计算等），覆盖结果会持久化保存。
6.  **开始计算：** 点击 **开始计算** 按钮。

### 载荷计算方式

-   **融合载荷法 (Incorporate Payload)：**
   将载荷直接计入火箭总质量进行计算。只要估算载荷仍大于0，说明火箭还有运输能力。

-   **纯火箭法 (Pure Rocket)：**
   将载荷放在一侧（不影响火箭计算），直接计算火箭自身的 Delta-V，从而直接得到运输能力。

<p align="center">
  <img src="https://i.imgur.com/ZbtSU3o.jpg" alt="融合载荷法" width="1000"/><br/>融合载荷法
</p>
<p align="center">
  <img src="https://i.imgur.com/IhsxXNS.jpg" alt="纯火箭法" width="1000"/><br/>纯火箭法
</p>

## 注意事项

-   **SSTO 支持**
    -   **纯火箭 SSTO:** 理论支持良好。
    -   **吸气式 / 混合动力 SSTO:**（如 RAPIER）仅作参考，因 IntakeAir 等不储存在燃料罐中的推进剂未被纳入计算。

-   **计算准确性说明**
    -   实际发射过程中会受到重力转弯速度与起始高度、飞行轨迹、载具气动布局以及操作方式等多种因素影响，因此计算得到的载荷质量为理论估算值。
    -   若实际飞行结果与计算值存在偏差，可通过调整 MechJeb（MJ）中的重力转弯速度、重力转弯高度及相关飞行参数进行优化，以获得更接近理论值的表现。

-   **开发状态说明**

    - 本 Mod 仍在持续完善中——它已经能帮你算Delta-V，但还没聪明到替你拯救每一次“空气动力学灾难”。
    - 如果它算错了、炸了、或者表现得像个实习工程师，欢迎提出意见和建议。大家一起把它打磨成真正的“航天总师”。 


