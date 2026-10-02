# ColorMatrix —— 色彩数学内核

> 本文件是本仓 **ColorMatrix 工程**的详细文档；增益图容器层那份见 [`GainMapKit.md`](GainMapKit.md)。总览见 [`README.md`](README.md)。

**纯托管的色彩数学内核** —— 色彩空间转换矩阵、传递函数、HDR 算子与逐像素执行器。
零外部依赖、零进程调用、无 ffmpeg 绑定；可直接用于任何 .NET 图像/视频色彩管线。

> 本库是从 [ffmpegPictureUI](https://github.com/luoye-cpu/ffmpegPictureUI) 的
> `Services/ColorMapping/` 中**抽取**出的可独立编译子集。抽取原则见
> [§5 本库刻意不包含什么](#5-本库刻意不包含什么)。

---

## 1. 这是什么

一套**自研**的色彩数学实现，用 C# 写成，**不链接、不依赖** lcms2 / LittleCMS / zimg / ffmpeg / libjxl
（零 NuGet 依赖）。覆盖「原色 ↔ 原色」的线性矩阵推导、色适应、标准传递函数（SDR 与 HDR）、
HDR→SDR 色调映射算子，以及逐像素的矩阵/曲线/量化执行。

> ⚠ **原创范围的诚实边界**：除 `ColorKernels.GamutMapScalar`（**逐行移植自 libjxl**，BSD-3-Clause）
> 与 `ToneMapping` 的 Hable/Mobius 常数（**公式形式对齐** ffmpeg `vf_tonemap.c`，未复制其源码）外，
> 本库其余代码为作者原创。完整出处登记见 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) ——
> 它对下游再分发有实际约束（BSD-3 第 2 条），不要跳过。

```csharp
using ColorMatrix;

// 例：求 bt2020 → bt709 的线性原色转换矩阵
var m = ColorSpaceRegistry.LinearMatrixBetweenPrimaries("bt2020", "bt709");
// → 3×3 行主序矩阵（行主序 × 列向量，全链同形）

// 例：BT.2100 PQ 的 EOTF
double nits = TransferCurve.PqEotf(0.58);   // → ≈ 203.0 nits

// 例：线性 float → sRGB 8bit（可选 AVX2）
SimdPixelOps.FloatToSrgb8(linearSpan, byteSpan);
```

## 2. 内容

| 文件 | 职责 |
|---|---|
| `ColorSpaceRegistry.cs` | 原色表（CICP/H.273）、白点、矩阵推导、Bradford 色适应、具名空间注册 |
| `ColorSpaceDescriptor.cs` | 色彩空间描述符与等价判定（消费 `EquivalenceTolerances`） |
| `TransferCurve.cs` | 标准传递函数（SDR 曲线归一为 7 参数规范形；PQ/HLG 为闭式） |
| `Bt2100Ootf.cs` | BT.2100 的 OOTF（正/逆） |
| `Bt2446MethodA.cs` | BT.2446 Method A 的 HDR→SDR 映射 |
| `ToneMapping.cs` | 色调映射算子（Reinhard / Hable / Mobius 等） |
| `ColorKernels.cs` | 逐像素内核：矩阵、曲线、位深量化与有序抖动 |
| `SimdPixelOps.cs` | SIMD 加速路径（AVX2）与标量回退 |
| `DescriptorConfidence.cs` / `EquivalenceTolerances.cs` | 从宿主抽取的最小依赖（枚举 / 阈值常量） |

## 3. 能力与覆盖

**原色（CICP/H.273）**：`bt709` · `bt470m` · `bt470bg` · `smpte170m` · `smpte240m` ·
`bt2020` · `smpte431` · `smpte432` · `jedec-p22`；具名空间含 sRGB / Display P3 /
Adobe RGB / ProPhoto RGB / Rec.2100 系列。

**传递函数**：CICP 1/4/5/6/7/8/13/14/15 · 16 (PQ) · 18 (HLG)；
BT.2100 OOTF；BT.2446 Method A。

**设计约束（有意为之）**：
- 矩阵布局与乘序**全链一致**：行主序 × 列向量，构造端 / 执行端 / ICC 读写端同形。
- **fail-closed**：未收录或无法表达的标准**返回 `null` / 拒绝**，绝不猜测一个矩阵去乘像素。
- 色适应复合为 `M⁻¹·L·M`（Bradford），已修对历史错序。

## 4. 实测数据（每条都能在本仓复现）

下表全部来自**本仓自带自检**的输出：`dotnet run --project tests/ColorMatrix.SelfTest -c Release`
⇒ 当前读数 **43 PASS / 0 FAIL / exit 0**。凡"外部参照"都是**别人的实现算出来的数**，
不是本库自己声称的期望值。

| 对撞对象（组） | 本仓实测读数 |
|---|---|
| 原色表逐 token 对撞 **zimg**（ffmpeg 的 `zscale` 实际下发的矩阵，第 8 组，8 个 token，容差 5e-4） | 逐元素最大实差：`bt470m 1.96e-4` · `bt470bg 4.32e-5` · `smpte170m 4.21e-5` · `smpte240m 4.21e-5` · `bt2020 5.06e-5` · `smpte431 2.56e-4` · `smpte432 4.30e-5` · `jedec-p22 5.25e-5` |
| 反退化：8 个 token 不得共用一组常数（第 8 组） | 算出 **7** 组不同矩阵（判据 ≥7）⇒ 原色表没有退化成"所有标准一条式" |
| 传递函数锚点（第 9 组） | PQ `ToLinear(0.580694)×10000 = ` **203.010 nits**（容差 0.6）；`ToLinear(0.8)` 对应 **1555.2 nits**（反控：远高于 SDR 白点） |
| PQ / HLG 往返互逆 + 三条曲线 100 点单调（第 9 组） | 互逆残差 `0.400000000000` / `0.600000000000`；PQ·HLG·BT.709 均单调不减，黑电平未被抬高 |
| `bt2020→bt709` 对撞 ITU 发布值（第 1 组）+ 往返互逆（第 3 组） | 通过；互逆 `M(a→b)·M(b→a) ≈ I` 实测 `maxErr 6.7e-16` |

⚠ **口径边界**：宿主项目另有一组更宽的对撞门禁（`verify-colorfmt-matrix` 等），
它跑在宿主的完整管线上、**不在本仓**，所以本表不引用它任何数字 —— 这里每一格都能用上面那条命令打出来。

**已知缺口（诚实登记，不是"还没填表"就是"数学上表达不了"）**：
- `film`（CICP 8）：精度不足，**有意不收录**，返回 `null`；
- `smpte428`（CICP 10 / ST 428-1）：原色是 **CIE-XYZ 三角**（蓝原色 y=0），
  **xy 参数化在数学上无法表达** ⇒ 永久 fail-closed，需新的参数化才能支持；
- `log100` / `log316` / `xvYCC` / `BT.1361` / `smpte428(γ2.6)`：未收录，fail-closed；
- **BT.2390 EETF**、**BT.2446 Method B/C**：未实现。

## 5. 本库刻意不包含什么

抽取时**刻意剔除**了以下部分——它们属于**应用层**，不是色彩数学内核：

- **规划层**（`ColorTransformPlan` / `ColorStrategyPlanning` / `ColorIntent` 等）：
  依赖宿主的 `FfmpegOptions`（Models）与 `FfmpegCommandBuilder`，且与宿主构成**双向循环依赖**。
- **执行层**（`RawColorPipeline`）：本质是 ffmpeg / cjxl / jxr 的**命令行编排器**，
  还依赖宿主的设置单例与临时文件。
- **`SimdKernelRouting`**：读宿主的 `AppSettingsService`（应用设置单例）。
  本库改以 `SimdPixelOps.UseAvx2ForFloatToSrgb8` 公开属性表达同一开关，由调用方设置。

⇒ 因此本库**不含任何 ffmpeg / cjxl / jxr 的进程调用，也不链接任何第三方库**。
唯一的第三方**代码**来源是一处：`ColorKernels.GamutMapScalar`（libjxl 逐行移植，BSD-3-Clause）
—— 见 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) §1。除此之外均为原创。

## 6. 构建

```bash
dotnet build src/ColorMatrix/ColorMatrix.csproj -c Release
```

- 目标框架 `net11.0`。当前状态：**0 警告 0 错误**。
- **文档生成是开着的**（`GenerateDocumentationFile=true`，只豁免 `CS1591` 缺注释）⇒
  悬空的 `<see cref>`、非法 XML 注释都会变成可见的 `CS1570/CS1574` 警告。
  本轮公开前据此修掉了 **15 处既有 markup 缺陷**（两处悬空 cref 指向未抽入本库的规划层类型、
  两处 `<summary>` 未闭合、未转义的 `<`、`TransformRgba8` 缺 7 个 `<param>`）。
- **换 TFM 复测**（2026-10-02 复测：`net8.0` 同样 **0 警告 0 错误**，本库不使用任何 .NET 11 专有 API）：
  ```bash
  dotnet restore src/ColorMatrix/ColorMatrix.csproj -p:TargetFramework=net8.0
  dotnet build   src/ColorMatrix/ColorMatrix.csproj -c Release -p:TargetFramework=net8.0 --no-restore
  ```
  ⚠ 少了 `restore` 那一步会直接 `error NETSDK1005`（资产文件里没有该 TFM 的目标）——
  这是还原态问题，不是代码问题；别把它误读成"库在 net8 上编不过"。测完记得再 `dotnet restore` 回 `net11.0`。

### 自带自检（零依赖）

```bash
dotnet run --project tests/ColorMatrix.SelfTest -c Release
```

**刻意不用 xunit / NUnit** —— 那会引入 NuGet 依赖，破坏本库「零外部依赖」的定位。
自检是控制台程序，**全过 exit 0 / 有失败 exit 非 0**，可直接接 CI。

当前读数：**43 PASS / 0 FAIL**，覆盖 9 组：

| 组 | 内容 |
|---|---|
| 1 | `bt2020→bt709` 对撞 ITU 发布值（含**反控**：不得退化成单位阵） |
| 2 | 契约：同原色返回 `null`、`film`/`smpte428`/未知 token 一律 fail-closed |
| 3 | 往返互逆 `M(a→b)·M(b→a) ≈ I`（实测 maxErr 6.7e-16） |
| 4 | 白点守恒：原色矩阵三行行和 ≈ 1 |
| 5 | 传递函数收录面：PQ/HLG/BT.709 收录，log100/log316/未知 fail-closed |
| 6 | HDR 算子闭式常数（含**反控**：非恒返回常量） |
| 7 | 逐像素内核：alpha 不受矩阵影响、矩阵真的改变颜色、`null` 为无操作 |
| 8 | **原色表逐 token 对撞 zimg**（8 个 token 的入库常量，容差 5e-4）+ **反控**：8 个 token 须算出 ≥7 组不同矩阵（防原色表退化成"所有标准共用一组常数"—— 历史上出过这个 P1） |
| 9 | **传递函数锚点 / 互逆 / 单调**：PQ 锚点 `ToLinear(0.580694)×10000 ≈ 203 nits`（实测 203.010）、PQ/HLG 往返互逆、三条曲线 100 点单调扫描、黑电平不被抬高 |

> ⚠ **`ToLinear` 的归一化约定**（实测澄清，勿再假设）：它归一化到**该曲线自身的峰值**，
> 不是归一化到 SDR 白点 203nits —— PQ 的 `1.0` = 10000 nits、HLG 的 `1.0` = 其峰值。
> ⇒ 跨曲线的锚点断言必须写成「乘回峰值换算成 nits」才与约定无关。
> 本组第一版我按"归一化到 203nits"写，**实测 3 条转红 —— 那是我的假设错，不是库错**；
> 这本身也是该组"有牙"的证据：断言确实绑在真实行为上，不是恒真式。

> ⚠ **自检的变异验牙**（2026-10-02 本机复跑，计数以当前 43 条为准；还原后两次都回到 **43 PASS / 0 FAIL**，
> 且被改文件 SHA256 与基线一致）：
> - 把 `Bt2446MethodA.LumaPrime` **代码行**的系数 `0.2627 * rp` 改成 `0.2628 * rp`
>   ⇒ **41 PASS / 2 FAIL**（红的正是第 6 组那两条 `LumaPrime` 锚点：白 `(1,1,1)` 与灰阶 `(0.5,0.5,0.5)`，
>   白那条件打印出 `实 1.000100000000`）。
> - 把 `bt470m` 的 R 色度 `{ 0.670, 0.330 }` 改成 `{ 0.680, 0.330 }`
>   ⇒ **42 PASS / 1 FAIL**（`bt470m→bt709` 对撞 zimg：`maxErr=8.64e-3` vs 容差 5e-4）。
> - **空变异教训（保留，别删）**：第一次改 `LumaPrime` 时用的是裸常数 `0.2627`，`replace(...,1)`
>   命中的是**注释里**的同名常数（`Bt2446MethodA.cs:33`），代码行（`:34`）没动 ⇒ 测试照绿，
>   差点被误读成「断言没牙」。⇒ **变异模式必须唯一到代码行**（本轮因此改用 `0.2627 * rp`），
>   并用"转红"本身证明它生效；命中数不等于 1 就直接判"这条变异不算"。



## 7. 来源与许可

- **来源**：从 [ffmpegPictureUI](https://github.com/luoye-cpu/ffmpegPictureUI) 抽取
  （该仓库为 **GPL-3.0**）。本库是该仓库中**不含 ffmpeg 依赖、可独立编译**的数学内核子集；
  其中除 `ColorKernels.GamutMapScalar`（libjxl 逐行移植）与 `ToneMapping` 的 Hable/Mobius
  常数（对齐 ffmpeg `vf_tonemap.c` 的公式形式）外，均为作者原创 —— 逐条登记见
  [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。
- **许可**：**MIT**（见 `LICENSE`），署名口径与源仓库一致（`Copyright (c) 2025-2026 luoye-cpu`）。
  ⚠ MIT 是本库原创部分的授权；libjxl 派生件自带的 **BSD-3-Clause 署名义务不被 MIT 豁免**，
  再分发源码须保留 `THIRD-PARTY-NOTICES.md`，再分发二进制须按该文件 §1 第 2 条复述声明。
- **「唯一版权人」前提 —— 已验证**（不是假设）：
  `git log` 全量作者去重 = **1 人**（`luoye-cpu`，68 个提交全部）；且触及被抽取文件
  （`Services/ColorMapping/`、`ColorSpaceRegistry.cs`、`SimdPixelOps.cs`）的提交也**全部**出自同一作者。
  ⇒ 不存在需要他人授权的第三方贡献者，GPL-3.0 → MIT 的重授权在**作者原创部分**上成立。
  （⚠ 若日后引入外部贡献者，新增部分的重授权需另行取得其同意。）

## 8. 免责

本库提供色彩数学实现，**不构成对任何标准符合性的担保**。
接入关键产线前请用你自己的参照（如 zimg / libavif / 真实素材）做交叉验证。
