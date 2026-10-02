# GainMapKit —— 增益图容器层


**增益图（Gain Map）的容器层** —— 纯托管、零依赖、零进程调用：解析与写入
Ultra HDR JPEG（ISO 21496-1 + MPF）与 ISOBMFF（AVIF / HEIC 的 `tmap` 派生项）里的增益图元数据。

> 本库是从 [ffmpegPictureUI](https://github.com/luoye-cpu/ffmpegPictureUI) 的
> `Services/GainMapDecoder.cs` / `IsoBmffGainMapProbe.cs` / `IsoBmffGainMapWriter.cs` 中
> **抽取**出的可独立编译子集。**宿主仍是权威副本**，同步口径见 §7。
> 本文件是本仓 **GainMapKit 工程**的详细文档；色彩数学内核那份见 [`ColorMatrix.md`](ColorMatrix.md)，
> 两个工程互不依赖，可只引用其中一个。总览与合仓说明见 [`README.md`](README.md)。

## 1. 这是什么 / 不是什么

**是**：字节层的容器读写。给你一段 JPEG / AVIF / HEIC 的字节，本库告诉你「有没有增益图、
以什么形式承载、元数据各字段是什么」；也能反过来把增益图元数据**装配**进 AVIF 容器。

**不是**：把增益真正作用到像素上（HDR = SDR × gain）、JPEG 解码、编码调度。
分工要说清：**增益图的像素侧数学**（灰度增益 `SimdPixelOps.ComputeGainMapGray`、
分段 Reinhard `SegmentedReinhardScalar`）在**本仓的 `ColorMatrix` 工程**里；
而"读像素 → 应用 → 编码写文件"那一段要起进程、要临时文件、要读应用设置
⇒ 留在宿主 [ffmpegPictureUI](https://github.com/luoye-cpu/ffmpegPictureUI)，一旦搬进来就不成其为库了。

```csharp
using GainMapKit;

// 读：Ultra HDR JPEG 容器（不是增益图容器 / 解析不出 ⇒ 返回 null 或 IsGainMapContainer()=false）
var c = JpegGainMapContainer.Parse(File.ReadAllBytes("ultrahdr.jpg"));
if (c != null && c.IsGainMapContainer() && c.IsoMetadata != null)
{
    float maxBoostLog2 = c.IsoMetadata.GainMapMax;          // ISO 存 log2 分数值
    float maxBoostLinear = MathF.Pow(2f, maxBoostLog2);     // 厂商工具报的是线性值
}

// 读：ISOBMFF（AVIF / HEIC）
var r = IsoBmffGainMapProbe.Probe(File.ReadAllBytes("hdr.avif"));
//   r.HasGainMap / r.Form("tmap") / r.PrimaryItemId / r.TmapItemId / r.GainMapItemId
//   r.GainMapCodec("av01"|"hevc"…) / r.GainMapFfmpegFormat / r.TmapPayload

// 写：把 tmap 派生项装进 AVIF（底图与增益图各是一张单图 AVIF 的字节）
var payload = IsoBmffGainMapWriter.BuildTmapPayload(iso21496MetadataBytes);
var built = IsoBmffGainMapWriter.Build(baseAvif, gainMapAvif, payload,
                                       basePrimaries: 9, baseTransfer: 16, baseMatrix: 9);
if (!built.Ok) throw new Exception(built.FailReason);       // 失败一定点名，不静默产出坏容器
File.WriteAllBytes("out.avif", built.Data!);
```

## 2. 内容（4 文件 / 1713 行）

| 文件 | 职责 |
|---|---|
| `GainMapMetadata.cs` | 解析后的增益图元数据（17 个字段，含 hdrgm 声明的逻辑尺寸） |
| `JpegGainMapContainer.cs` | Ultra HDR **JPEG** 容器：顶层多张 JPEG 边界、APP2 `urn:iso:…:21496:-1` 载荷、MPF（TIFF，双端序）条目；`ParseIso21496` 是 ISO 元数据的唯一解析入口 |
| `IsoBmffGainMapProbe.cs` | **ISOBMFF** 读侧：`ftyp` 品牌、`meta/pitm/iinf/iref/iprp(grpl)` 走查、`tmap` 派生项定位、`hvcC`→Annex-B 前置转换、失败原因一律 `[IsoBmff] ` 前缀 |
| `IsoBmffGainMapWriter.cs` | **ISOBMFF** 写侧：装配 `tmap` 项 + `iref/dimg` + `grpl(altr)` + 改写底图 `colr`，输出单文件容器 |

## 3. 实测数据（外部参照对撞，非自述）

自检套件 **78 PASS / 0 FAIL**（`exit 0`），构建 **0 警告 0 错误**。以下每条的期望值
都来自**第三方工具对同一件产物/夹具的读数**，不是本库自己说的：

| 对撞对象 | 期望值来源 | 读数 |
|---|---|---|
| ISO 21496-1 元数据逐字段 | **Google libultrahdr v2.0.2** `ultrahdr_app -m 1 -P -j` | 7 组字段全对撞通过（`maxContentBoost 5.86678`、`hdrCapacityMax 4.92611`、`useBaseColorSpace 0`…；本库存 log2 分数、厂商报线性 ⇒ 过 `2^x` 换算后逐条相等） |
| ISOBMFF 读侧（`tmap` 定位 + 载荷语义） | **libavif 1.4.2** `avifdec --info` | avifdec 对该件报「含增益图 + `Base Headroom 3.00 / Alternate Headroom 0.00`」；本库探测同一件读出 `HdrCapacityMin=3.00 / HdrCapacityMax=0.00`（log2 域）逐字相符，并判出 `Form=tmap`。⚠ **口径**：`PrimaryItemId/TmapItemId/GainMapItemId` 那组 id 是**本库自证**（互异且能取回载荷），avifdec 并不逐项打印 id ⇒ 别把它们当成第三方背书 |
| **写侧产物互通性** | **libavif 1.4.2** `avifdec --info` | 本库 `Build()` 的产物被 avifdec **接受**并读回：底图 `9/16/9`（正是请求的 CICP）、`Gain map: 16x12 pixels … Base Headroom 3.00 / Alternate 0.00`、`Alternate image: Transfer Char. 16`。⚠ **口径**：这台机器上这份 libavif **没编 AV1 解码器**（`avifdec` 另报 `Failed to decode image: No codec available`）⇒ 验到的是**容器层被第三方接受并读对**，**不是**第三方解出了像素 |
| `iref` 的 `reference_count` 两种合法读法 | 对真实件做字节手术（删 count 并同步收缩 `dimg/iref/meta` 三层盒长） | 两种形态收敛到同一增益图 item（实测） |
| 负控 / 反退化 | 见 §5 | 11 个截断点不抛、JPEG 喂 BMFF 判否、无增益图片判否、只取通道 0 有反控 |

**怎么复算这些第三方读数**（两个工具都不在本仓，需自备同版本；`--dump` 会打印本库对同一件的读数、
盒框走查，并把 `Build()` 的产物写成 `built_by_selftest.avif` 供 `avifdec` 复核）：

```bash
ultrahdr_app -m 1 -P -j fixtures/ref_ultrahdr_golden.jpg
avifdec --info fixtures/ref_libavif_colorgrid_gainmapgrid.avif
dotnet run --project tests/GainMapKit.SelfTest -c Release -- --dump
```

**变异验牙**：6 处定向破坏（摘掉 `min_version` 门 / 通道 0 读数偏一 / `useBaseColorSpace` 恒真 /
BMFF magic 恒真 / writer 不再识别既有 `colr` / `iref` 判别法退化）⇒ **6/6 被抓**，
各自打出点名断言；还原后 4 份源文件 SHA256 与基线逐位一致，复跑回到 `78/0`。

## 4. 能力与边界（诚实登记，别当「已支持」引用）

✅ **已支持并有外部对撞**：Ultra HDR JPEG 读（APP2 + MPF + 双端序）、ISOBMFF 读
（AVIF `tmap`、item 定位、载荷取出）、AVIF 写（`tmap` + `dimg` + `altr` + `colr` 改写）。

⚠ **边界 1 — grid 型增益图项：本库「不定案」，而不是谎报「没有」**。
`fixtures/ref_libavif_solo_gainmapgrid.avif`（增益图本身是网格项）实测：
`Determinate=false`、`GainMapItemId=-1`、原因 `[IsoBmff] tmap 的 dimg 增益图候选数 = 0（应为 1）`，
而 `tmap` 载荷仍能取出（元数据不丢）。⇒ 消费方必须把 `Determinate=false` 当「未知」处理，
**不能**当「无增益图」处理。

⚠ **边界 2 — 标量只保留通道 0**。`Channels` 字段如实报 3，但 `GainMapMin/Max/Gamma/Offset*`
是**单值**（取通道 0）。逐通道增益需要调用方自己从 `TmapPayload` / ISO 载荷再取 —— 本库不假装支持。

⚠ **边界 3 — 写侧不校验元数据自洽性**。`Build()` 把 `tmap` 载荷当不透明字节。反例实测：
某通道 `max < min` 时本库照样产出容器，而 libavif `avifdec` 对同一件报
`Invalid tone mapped image item` + `Per-channel max is less than per-channel min in gain map metadata`。
⇒ 元数据合法性是**调用方**的责任。

❌ **不在范围内**：像素级增益应用、JPEG 编码侧装配（宿主的 `GainMapEncoder`）、
HEIC 写侧（无实测件 ⇒ 未宣称）、XMP `hdrgm` 读取（在宿主 `GainMapDecoder`）。
另有 **ST 428-1 / film 等色彩侧永久 fail-closed 项** —— 那是内核侧的边界，见 [`ColorMatrix.md`](ColorMatrix.md) §4。

## 5. 构建与自检

```bash
dotnet build src/GainMapKit/GainMapKit.csproj -c Release
dotnet run   --project tests/GainMapKit.SelfTest -c Release      # 78 PASS / 0 FAIL，exit 0
dotnet run   --project tests/GainMapKit.SelfTest -c Release -- --dump   # 打夹具读数与盒框走查
```

- 目标框架 `net11.0`。⚠ 我只实测了 net11.0（0 警告 0 错误）；**没有**实测更低 TFM，
  所以这里不声称更低框架可用。
- 自检**刻意不用 xunit / NUnit**（会引入 NuGet 依赖，破坏零依赖定位），是控制台程序 + 退出码，可直接接 CI。
- 库 csproj 开了 `GenerateDocumentationFile`，因此**悬空的 `<see cref>` 会变成可见的 CS1574**；
  现在 0 警告 ⇒ 引用关系自洽（只豁免了 `CS1591` 公共成员缺注释这一类）。
- 夹具按**文件名 + 尺寸 + SHA256 前缀**取用，件不齐就 fail-closed 直接退出，不做「目录里第一个」的猜测。
  复现第三方夹具的命令行见 `THIRD-PARTY-NOTICES.md` §3。

## 6. 为什么这套判据值得信一点

三条设计约束，都是踩过坑之后钉死的：

- **失败要点名**：读不出来就写清「为什么」（`[IsoBmff] …` / `[GainMap-Avif] …`），
  空原因会被断言打回（自检里有专门一条查 `StartsWith("[GainMap-Avif]")`）。
- **不猜容器**：`iref` 的 `reference_count` 在规范里不存在、在 libavif 产物里存在，
  本库**两种读法都容忍**并实测收敛（§3）；判别法退化会在自检里红（M6）。
- **降级不是修复**：定不了案就报 `Determinate=false`，绝不用「找不到 ⇒ 说它没有」糊过去（边界 1）。

## 7. 来源、许可与双份同步

- **来源**：抽自 [ffmpegPictureUI](https://github.com/luoye-cpu/ffmpegPictureUI)（**GPL-3.0**）。
  切点：`GainMapDecoder.cs` 第 41-69 行 + 第 503-816 行（并把同类内 `:462` 的 `IsoNamespace` 常量随件带入）、
  `IsoBmffGainMapProbe.cs` 整文件、`IsoBmffGainMapWriter.cs` 整文件；锚点写在各文件头部注释里。
- **许可**：**MIT**（`LICENSE`）。第三方咨询与夹具数据的许可登记见 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。
- **「唯一版权人」前提**：被抽文件的全部提交出自 `luoye-cpu` 一人
  （`git log --format='%an' -- <路径> | sort | uniq -c` 实测：`GainMapDecoder.cs` 5 次、
  `IsoBmffGainMapProbe.cs` 1 次，均为同一作者；`IsoBmffGainMapWriter.cs` 是 2026-10-02 新写、尚未提交）。
  ⇒ 无第三方贡献者需要另行授权，GPL-3.0 → MIT 的重授权在版权上成立。
- ⚠ **宿主仍是权威**，本库是副本。本副本有**三处既定差异**，回灌时必须逐条映射：
  ① 命名空间 `FfmpegGui.Services` → `GainMapKit`；
  ② `GainMapDecoder.JpegContainer` 提升为顶层 `JpegGainMapContainer`（宿主侧 3 处
  全限定访问 `GainMapDecoder.JpegContainer.` 因此不能直接替换）；
  ③ 对外可见性提升（`internal` → `public`：`IsoBmffGainMapWriter`、`BuildResult`、
  `HasTmapItemFast`、`MaxManagedParseBytes`）。
  宿主侧对被抽符号的引用实测为 **36 行 / 5 个文件**（`GainMapDecoder.cs` 26、`GainMapEncoder.cs` 4、
  `tests/ServiceProbe/Program.cs` 3、`CjxlService.cs` 2、`QueueProcessor.cs` 1）⇒ 切引用（cutover）
  是**独立一批**改动，本库发布不含它；两仓暂长期双份，改本层代码时**两边都要动**。

## 8. 免责

本库提供容器读写实现，**不构成对 ISO 21496-1 / 14496-12 / 23008-12 / CIPA DC-X007
任何一条符合性的担保**。接入关键产线前请用你自己的第三方工具（`avifdec` / `ultrahdr_app` /
真实素材）交叉验证 —— 本仓自检的外部对撞正是这么做的。
