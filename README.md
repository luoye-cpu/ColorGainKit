# ColorGainKit

**色彩数学内核 + 增益图容器层**，放在同一个仓里的**两个可独立引用**的纯托管 .NET 工程：
零 NuGet 依赖、零进程调用、不读任何应用设置 —— 你可以只用其中一个。

| 工程 | 一句话 | 详细文档 |
|---|---|---|
| `src/ColorMatrix`（命名空间 `ColorMatrix`） | 原色↔原色矩阵推导、色适应、标准传递函数（sRGB/PQ/HLG…）、BT.2100 OOTF、BT.2446 Method A、色调映射算子、逐像素内核与 AVX2 路径；**增益图的像素侧数学也在这里**（`ComputeGainMapGray` 灰度增益编码、`SegmentedReinhardScalar` 分段 Reinhard） | [`ColorMatrix.md`](ColorMatrix.md) |
| `src/GainMapKit`（命名空间 `GainMapKit`） | Ultra HDR JPEG（ISO 21496-1 + MPF）解析；AVIF / HEIC 的 `tmap` 派生项**探测与写出** —— 只管**容器里的字节**，不碰像素 | [`GainMapKit.md`](GainMapKit.md) |

## 1. 为什么"一个仓、两个工程"

- **一个仓**：两块同源于 [ffmpegPictureUI](https://github.com/luoye-cpu/ffmpegPictureUI) 的色彩 / 亮度表达层，
  出处与许可登记（`THIRD-PARTY-NOTICES.md`）本来就该一处维护，判据也一起跑。
- **两个工程**：程序集与命名空间各自独立，且**互不依赖**（实测：容器层对内核的类型引用计数 = 0，
  两边也没有 `ProjectReference`）⇒ 只想要色彩数学的人不必吃下 AVIF 盒解析，
  只想要增益图容器读写的人不必吃下一条传递函数曲线。

> ⚠ **别把"增益图"当成一个工程**：这件事被切成了两半 —— **像素侧数学**（算出增益图、把 HDR 压成 SDR 底图）
> 在 `ColorMatrix`；**容器侧读写**（元数据、APP2 / `tmap` / MPF）在 `GainMapKit`；
> 而**编解码调度**（起 ffmpeg / cjxl / ultrahdr_app 真正产出文件）留在宿主，不在本仓。
> 想端到端做一张 Ultra HDR 图，光引本仓两个工程是不够的 —— 这是刻意的边界，不是漏实现。

## 2. 上手

```csharp
using ColorMatrix;
var m = ColorSpaceRegistry.LinearMatrixBetweenPrimaries("bt2020", "bt709"); // 行主序 × 列向量
double nits = TransferCurve.PqEotf(0.58);                                    // ≈ 203.0

using GainMapKit;
var r = IsoBmffGainMapProbe.Probe(File.ReadAllBytes("hdr.avif"));            // r.HasGainMap / r.Form / r.TmapPayload
var c = JpegGainMapContainer.Parse(File.ReadAllBytes("ultrahdr.jpg"));       // Ultra HDR JPEG
```

## 3. 构建与自检（合计 121 条断言，全部本机实测）

```bash
dotnet build ColorGainKit.sln -c Release                                  # 4 工程：0 警告 0 错误
dotnet run --project tests/ColorMatrix.SelfTest -c Release                # 43 PASS / 0 FAIL，exit 0
dotnet run --project tests/GainMapKit.SelfTest  -c Release                # 78 PASS / 0 FAIL，exit 0
dotnet run --project tests/GainMapKit.SelfTest  -c Release -- --dump      # 打印夹具读数与盒框走查
```

自检是**控制台程序 + 退出码**，刻意不引 xunit / NUnit（会破坏"零依赖"定位），可直接接 CI。
两个工程的判据都分两档写死：**外部参照**（对撞 zimg / lcms2 / `ultrahdr_app` / `avifdec` 的读数，
才谈得上与第三方互通）与**构造恒等 / 同源自洽**（自己铺的字节、自己读写互为对偶，只证不矛盾）。
两份文档各自的 §3/§4 记录了哪些格子属于哪一档。

## 4. 边界（先看这版，细节在两份文档）

**内核 `ColorMatrix`**：未收录或数学上表达不了的标准一律 **fail-closed**（返回 `null` / 拒绝），
绝不猜一个矩阵去乘像素 —— `film`（精度不足，有意不收录）、`smpte428`/ST 428-1
（原色是 CIE-XYZ 三角，xy 参数化表达不了，永久 fail-closed）、`log100`/`log316`/`xvYCC`/`BT.1361`；
**BT.2390 EETF** 与 **BT.2446 Method B/C** 未实现。

**容器层 `GainMapKit`** 三条实测边界：① 增益图本身是网格项时**不定案**
（`Determinate=false` 且点名原因 ⇒ 消费方必须当"未知"，不是"没有增益图"）；
② 元数据标量**只保留通道 0**（`Channels` 仍如实报 3，逐通道要自己从载荷再取）；
③ 写侧把 `tmap` 载荷当**不透明字节**、不校验其自洽性 —— 某通道 `max < min` 时本仓照样出容器，
而 libavif 会拒收。⇒ 元数据合法性是调用方的责任。

## 5. 许可

- 本仓 **MIT**（`LICENSE`）。
- 唯一的第三方**代码**：`src/ColorMatrix/ColorKernels.cs` 的 `GamutMapScalar`，
  **逐行移植自 libjxl**（BSD-3-Clause）⇒ 其署名义务由 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) A.1 履行，
  **MIT 不豁免它**；再分发二进制也要按该条复述声明。
- `src/GainMapKit` **无任何第三方代码移植**（实测带词边界计数 = 0），只有格式咨询、
  照录的第三方报错串，以及 2 个随仓的 libavif 数据夹具（BSD-2-Clause）——见同文件 B 部。
- 从 **GPL-3.0** 的宿主抽出却能给 MIT，依据是"唯一版权人"已核验（该仓 68 个提交同一作者）——见 C 部。

## 6. 与宿主的关系 / 仓库沿革

- ⚠ **宿主 `ffmpegPictureUI` 里那几处代码仍是权威**，本仓是抽取副本（尚未切 `ProjectReference`）
  ⇒ 改到这些逻辑要两边同步；副本与宿主的既定差异（命名空间、`internal`→`public`、
  `JpegContainer` → 顶层 `JpegGainMapContainer`）逐条记在 [`GainMapKit.md`](GainMapKit.md) §7 与
  [`ColorMatrix.md`](ColorMatrix.md) §5/§7。
- **沿革**：本仓由先前的两个仓（`ColorMatrix`、`GainMapKit`）合并而来，那两个仓已删除，
  其链接不再可达；本仓历史从 `main` 重新开始，内容与判据原样平移（43 + 78 条断言、
  6 处变异验牙、5 件夹具都在）。
