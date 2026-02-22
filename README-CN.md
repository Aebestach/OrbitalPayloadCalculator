# 轨道运载能力计算器

<div align="center">
    
<img src="https://imgur.com/ryDQSmm.jpg" alt="Banner"/>

[![License](https://img.shields.io/github/license/Aebestach/OrbitalPayloadCalculator)](LICENSE)
[![Release](https://img.shields.io/github/v/release/Aebestach/OrbitalPayloadCalculator)](https://github.com/Aebestach/OrbitalPayloadCalculator/releases)

[English](README.md) | [中文](README-CN.md)

</div>

---

## 📖 简介 
在坎巴拉太空计划 (KSP) 里，设计火箭往往不是“工程学”，而是“玄学”——拍脑袋堆燃料、凭感觉上级数，发射时祈祷别炸，入轨后才发现：

* 要么 Delta-V 算少了，差一点点就能到 Mun，只能看着绿人流泪；
* 要么 Delta-V 多到离谱，油箱富得流油，去个 Mun 像是准备环游太阳系；
* 最尴尬的是，好不容易入轨了，却发现载荷重得像铅块，燃料表一格一格往下掉，心也跟着一起掉。

令人惊讶的是，KSP 已经十年之久了，却很少有人做过专门解决这个问题的工具。自己在设计火箭时也深有体会：不断对照Delta-V表计算载荷，既费脑又容易出错。正是这些需求和“痛点”，促使 **Orbital Payload Calculator** 的诞生。

现在，**Orbital Payload Calculator** 可以帮你把“玄学造箭”升级成“理性工程”。
它能提前算清载荷质量和所需 Delta-V，让你不再靠感觉堆燃料，也不再为了保险多带两罐“心理安慰油”。

从此，你的火箭不是“差一点”，也不是“富得流油”，而是——刚刚好，优雅入轨，潇洒去 Mun。 🚀

**Orbital Payload Calculator** 是 **坎巴拉太空计划 (KSP)** 的一款工具模组，用于估算火箭在指定目标轨道下的最大运载能力。它通过基于物理的上升模拟来计算重力、大气阻力和转向损失，帮助你在发射前精确设计运载火箭，告别盲目猜测。

本模组支持 **编辑器 (VAB/SPH)** 和 **飞行场景**（仅限着陆或发射前状态）。


<div align="center">
    <img src="https://imgur.com/AQLVjPl.jpg" alt="UI Screenshot"/>
</div>


## ✨ 功能特性

*   **🚀 运载能力估算**
    *   计算能够送入目标轨道的最大有效载荷质量（吨）。
    *   通过迭代算法，找出火箭可用 Delta-V 与入轨需求 Delta-V 平衡点的载荷质量。
*   **📉 高级损失模型**
    *   **模拟仿真：** 使用天体的真实大气压力和温度曲线运行分步上升模拟。
    *   **重力与阻力：** 根据载具的推重比 (TWR) 和气动特性自动估算重力损失和大气阻力。
    *   **乐观模式：** 提供“乐观估算”选项，假设采用激进/完美的上升轨迹，从而得出较低的损失值。
*   **🌍 多天体与自转支持**
    *   支持任意天体（Kerbin, Eve, Duna 以及原版的模组星球）。
    *   考虑天体自转对发射的影响（顺行发射 vs 逆行发射）以及发射纬度。
*   **🔄 分级分析**
    *   支持单级、多级火箭。
    *   根据大气压力自动混合真空比冲 (ISP) 和海平面比冲。
    *   查看详细的分级数据：质量、推力、比冲、推重比和 Delta-V。
*   **🛠️ 可配置目标**
    *   设置目标 **远点 (Ap)**、**近点 (Pe)** 和 **轨道倾角**。
    *   支持单位：`m` (米), `km` (千米), `Mm` (兆米)。
    *   如果目标倾角小于发射纬度，会自动计算所需的平面变更 (Plane Change) Delta-V。

## 📦 依赖要求

*   **Click Through Blocker** (必须)

## 📥 安装说明

1.  下载最新版本 Release。
2.  将 `GameData/OrbitalPayloadCalculator` 文件夹复制到 KSP 安装目录的 `GameData` 文件夹下。
3.  确保已安装 **Click Through Blocker**。

## 🎮 使用指南

### 打开计算器
*   **快捷键：** 按下 **Left Alt + P** (默认) 切换窗口显示。
*   **工具栏：** 点击应用启动器 (AppLauncher) 中的 **Orbital Payload Calculator** 图标。

### 操作流程
1.  **选择天体：** 选择发射所在的星球（例如 Kerbin）。
2.  **设置轨道：** 输入期望的 **远点**、**近点** 和 **倾角**。
3.  **发射纬度：**
    *   编辑器中：手动输入预计的发射纬度。
    *   飞行中：默认读取当前载具的纬度。
4.  **损失设置：**
    *   **自动估算 (Auto-estimate)：** 推荐开启。使用内置的上升模拟。
    *   **乐观估算 (Optimistic)：** 如果你的重力转弯非常高效，可勾选此项。
    *   **手动覆盖：** 取消勾选“Auto”后，可手动输入重力、大气和姿态控制的 Delta-V 预留值。
5.  **开始计算：** 点击 **开始计算** 按钮。

### 结果解读
*   **Estimated Payload (估算载荷)：** 火箭在当前基础上还能额外携带的最大吨位。
*   **Required Delta-V (需求 Delta-V)：** 入轨所需估算的总 Delta-V（轨道速度 + 损失 + 平面变更 ± 自转损失/助力）。
*   **Available Delta-V (可用 Delta-V)：** 载具当前估算的总 Delta-V。
*   **Loss Breakdown (损失分解)：** 估算重力、阻力和姿态控制分别消耗了多少 Delta-V。

### 载荷计算方式

*   **融合载荷法 (Incorporate Payload)：**
   将载荷直接计入火箭总质量进行计算。只要估算载荷仍大于0，说明火箭还有运输能力。

*   **纯火箭法 (Pure Rocket)：**
   将载荷放在一侧（不影响火箭计算）, 直接计算火箭自身的 Delta-V，从而直接得到运输能力。

<div style="display: flex; flex-direction: column; gap: 20px; justify-content: center; align-items: center;">
  <div style="text-align: center;">
    <img src="https://i.imgur.com/pylRnia.jpg" alt="融合载荷法" width="1000"/>
    <p align="center">融合载荷法</p>
  </div>
  <div style="text-align: center;">
    <img src="https://i.imgur.com/QRGMMKk.jpg" alt="纯火箭法" width="1000"/>
    <p align="center">纯火箭法</p>
  </div>
</div>

## 🧩 工作原理

1.  **载具分析：** 模组会扫描你的载具（无论是编辑器中还是飞行中），建立分级列表，计算每一级的质量、推力和比冲。
2.  **上升模拟：** 模拟一个重力转弯上升过程：
    *   垂直爬升直到达到特定速度/高度。
    *   根据幂律曲线进行重力转弯俯仰。
    *   阻力计算基于质量推导出的估算 CdA（阻力系数 * 面积）。
    *   推力和比冲会随大气压力的变化动态调整。
3.  **二分搜索：** 模组会多次运行模拟，不断调整“模拟载荷质量”，直到可用 Delta-V 与需求 Delta-V 相匹配，从而找到火箭运载能力的极限。

## ⚠️ 注意事项

*   **SSTO 支持**
    *   **纯火箭 SSTO:** 理论支持良好。
    *   **吸气式 / 混合动力 SSTO:**（如 RAPIER）仅作参考，因 IntakeAir 等不储存在燃料罐中的推进剂未被纳入计算。

*   **计算准确性说明**
    *   实际发射过程中会受到重力转弯速度与起始高度、飞行轨迹、载具气动布局以及操作方式等多种因素影响，因此计算得到的载荷质量为理论估算值。
    *   若实际飞行结果与计算值存在偏差，可通过调整 MechJeb（MJ）中的重力转弯速度、转弯高度及相关飞行参数进行优化，以获得更接近理论值的表现。

* **兼容性与测试范围说明**

  * 当前版本尚未在 **FAR（Ferram Aerospace Research）**、**RSS（Real Solar System）**、**Principia** 以及 **各种缩放天体** 等环境下进行系统性测试。
  * 欢迎玩家在上述环境中进行测试，并反馈问题或提供改进建议。

* **开发状态说明**

  * 本 Mod 仍在持续完善中——它已经能帮你算Delta-V，但还没聪明到替你拯救每一次“空气动力学灾难”。
  * 如果它算错了、炸了、或者表现得像个实习工程师，欢迎提出意见和建议。大家一起把它打磨成真正的“航天总师”。 


