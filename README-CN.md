# 轨道运载能力计算器 (Orbital Payload Calculator)

<div align="center">

[![License](https://img.shields.io/github/license/Aebestach/OrbitalPayloadCalculator)](LICENSE)
[![Release](https://img.shields.io/github/v/release/Aebestach/OrbitalPayloadCalculator)](https://github.com/Aebestach/OrbitalPayloadCalculator/releases)

[English](README.md) | [中文](README-CN.md)

</div>

---

## 📖 简介

**Orbital Payload Calculator** 是 **坎巴拉太空计划 (KSP)** 的一款工具模组，用于估算火箭在指定目标轨道下的最大运载能力。它通过基于物理的上升模拟来计算重力、大气阻力和转向损失，帮助你在发射前精确设计运载火箭，告别盲目猜测。

本模组支持 **编辑器 (VAB/SPH)** 和 **飞行场景**（仅限着陆或发射前状态）。

## ✨ 功能特性

*   **🚀 运载能力估算**
    *   计算能够送入目标轨道的最大有效载荷质量（吨）。
    *   通过迭代算法，找出火箭可用 Delta-V 与入轨需求 Delta-V 平衡点的载荷质量。
*   **📉 高级损失模型**
    *   **模拟仿真：** 使用天体的真实大气压力和温度曲线运行分步上升模拟。
    *   **重力与阻力：** 根据载具的推重比 (TWR) 和气动特性自动估算重力损失和大气阻力。
    *   **乐观模式：** 提供“乐观估算”选项，假设采用激进/完美的上升轨迹，从而得出较低的损失值。
*   **🌍 多天体与自转支持**
    *   支持任意天体（Kerbin, Eve, Duna 以及模组星球）。
    *   考虑天体自转对发射的影响（顺行发射 vs 逆行发射）以及发射纬度。
*   **🔄 分级分析**
    *   完美支持多级火箭。
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
5.  **开始计算：** 点击 **Calculate** 按钮。

### 结果解读
*   **Estimated Payload (估算载荷)：** 火箭在当前基础上还能额外携带的最大吨位。
*   **Required Δv (需求 Δv)：** 入轨所需的总 Delta-V（轨道速度 + 损失 + 平面变更 + 自转助力）。
*   **Available Δv (可用 Δv)：** 载具当前的总 Delta-V。
*   **Loss Breakdown (损失分解)：** 显示重力、阻力和姿态控制分别消耗了多少 Δv。

## ⚙️ 配置

*   **字体大小：** 你可以直接在窗口顶部调整 UI 字体大小 (13-20)，点击 **Save** 保存。
*   **设置存储：** 字体设置保存在 Unity 的 `PlayerPrefs` 中。

## 🧩 工作原理

1.  **载具分析：** 模组会扫描你的载具（无论是编辑器中还是飞行中），建立分级列表，计算每一级的质量、推力和比冲。
2.  **上升模拟：** 模拟一个重力转弯上升过程：
    *   垂直爬升直到达到特定速度/高度。
    *   根据幂律曲线进行重力转弯俯仰。
    *   阻力计算基于质量推导出的估算 CdA（阻力系数 * 面积）。
    *   推力和比冲会随大气压力的变化动态调整。
3.  **二分搜索：** 模组会多次运行模拟，不断调整“模拟载荷质量”，直到可用 Delta-V 与需求 Delta-V 相匹配，从而找到火箭运载能力的极限。
