# Third-party notices / 第三方出处与许可

本仓（ColorGainKit）整体以 **MIT** 授权（见 `LICENSE`）。本仓有两个独立工程，
它们的第三方出处**分开登记**，别混着读：

- **A 部 = [`src/ColorMatrix`](src/ColorMatrix)（色彩数学内核）**：这里**有一处真正的代码移植**
  （libjxl 的 `GamutMapScalar`，BSD-3-Clause），附带完整的上游声明文本。
- **B 部 = [`src/GainMapKit`](src/GainMapKit)（增益图容器层）**：这里**没有任何代码移植**
  （实测计数 0，口径见 B.0），但有「格式咨询」、照录的第三方报错串，以及 2 个随仓的第三方数据夹具。
- **C 部 = 两部的共同许可关系**（与源仓库 GPL-3.0 → MIT 的依据）。

判定口径：**「依赖」与「出处」是两件事**。两个工程都不链接、不依赖任何第三方**库**
（零 NuGet 依赖，构建时不需要 lcms2 / zimg / libjxl / libavif / ffmpeg）；
但下述代码与常数的**来源**不属于本仓作者，如实登记。

---

## A. `src/ColorMatrix` —— 色彩数学内核

### A.1 libjxl —— **代码移植**（BSD-3-Clause）

| 项 | 内容 |
|---|---|
| 被移植的部分 | `ColorKernels.GamutMapScalar`（色域映射 GMO 标量参考实现） |
| 上游出处 | libjxl `lib/jxl/cms/tone_mapping.h` 的 `GamutMapScalar` |
| 移植程度 | **逐行移植**（循环结构、中间量语义、`val == luminance` 时改用除数 `1.0` 的除零处理均照上游） |
| 附带的常数 | `GamutMapParams.PreserveSaturation` 默认 `0.3` —— 取自 libjxl `lib/jxl/cms/jxl_cms_internal.h:158`（上游 `tone_mapping.h` 的形参默认值是 `0.1f`，本库按其内部 CMS 路径取 `0.3`） |
| 上游许可 | BSD-3-Clause |
| 上游版权 | `Copyright (c) the JPEG XL Project Authors. All rights reserved.` |

⇒ 按 BSD-3-Clause 第 1 条，再分发源码必须保留下列版权声明、条件列表与免责。
`ColorKernels.cs` 的 GMO 小节头部也带了同一署名，本文件是其完整版。

```
Copyright (c) the JPEG XL Project Authors.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

**对下游的要求**：以二进制形式分发（含打进自己的 App / NuGet 包）时，须按第 2 条把上面这段
随文档一并复述。BSD-3-Clause 与 MIT 兼容，因此本库以 MIT 发布不冲突；但 MIT 不豁免上述署名义务。

### A.2 ffmpeg —— **常数与公式形式对齐**（未复制源代码）

| 项 | 内容 |
|---|---|
| 涉及 | `ToneMapping` 的 `Hable`、`Mobius` 两个算子 |
| 上游出处 | ffmpeg `libavfilter/vf_tonemap.c` 的 `hable()` / `mobius()` |
| 具体取用 | Hable 的 `a=.15 b=.50 c=.10 d=.20 e=.02 f=.30` 常数、`hable(sig)/hable(peak)` 归一化形式；Mobius 的分式与默认拐点 `0.3` |
| 未取用 | ffmpeg 的 C 源代码本身（表达式为作者用 C# 原创书写） |

⇒ 这些是**公开公布的算法常数与数学式**（Hable 曲线出自 John Hable 为《Uncharted 2》公开的发表，
Mobius 变换为其公开定义），不是受版权保护的表达性代码。此处登记是为口径完整，不额外产生许可义务。
若你的发行渠道需要，也可把 `Hable`/`Mobius` 两个算子换掉后忽略本节。

### A.3 ITU / CICP 标准 —— 常数取自标准正文（非代码移植）

| 项 | 内容 |
|---|---|
| `Bt2446MethodA.cs` | ITU-R Report **BT.2446-1** §4 Method A 的常数，取自建议书 Table 2 / 3 / 4 正文 |
| `Bt2100Ootf.cs` | ITU-R **BT.2100** 的 OOTF 常数（逆 OOTF 由正式表达式代数反解，非另抄公式） |
| `TransferCurve.cs` | ITU-R BT.709 / BT.601 / BT.2100 的传递函数分段定义；PQ/HLG 闭式按标准 |
| `ColorSpaceRegistry.cs` | **H.273 / CICP** 原色与传递函数的色度坐标表；具名空间（sRGB、Display P3、Adobe RGB、ProPhoto、Rec.2100 系列）取自其公开定义 |

⇒ 标准文本里的数值/公式属于事实与数学，实现按标准不构成对标准文档的代码衍生。
标准原文本身未随本库分发。

### A.4 仅作**外部参照**、不进本库的东西

这些实现用于**对拍验证**（见 `ColorMatrix.md` §4 与自检第 8 组），既不链接也不复制其代码，
也不参与本库构建：

- **zimg / ffmpeg 的 `zscale`** —— 原色矩阵逐 token 的外部参照数值来源。
- **lcms2（LittleCMS）** —— 色度坐标与 ICC 的第二路独立参照。
- **exiftool** —— 增益图容器层（另仓）的权威读数对撞工具。

`TransferCurve` 的 7 参数规范形与 Google **skcms** / **libultrahdr** 的 `TransferFunction`
结构同构 —— 那是该领域的通行表达约定，本库未移植其代码。

---

## B. `src/GainMapKit` —— 增益图容器层

### B.0 结论（实测口径）

| 问题 | 回答 | 我怎么量的 |
|---|---|---|
| 有没有第三方**源码的移植**？ | **没有 —— 计数 0** | `grep -rhoE '逐行\|移植\|照抄\|<ported>\|<copied>' src tests --include=*.cs \| wc -l` = **0**。<br>（注意：用 `ported` 不带词边界会命中 `supported` 而误报 1 —— 这条我踩过，词边界必须加。） |
| 有没有查过第三方实现？ | **有，且注释里点名了** | 下表逐条列出。属于「读别人的实现来确认格式要求」，不是「把别人的代码搬进来」。 |
| 有没有随仓分发第三方**数据**？ | **有，2 个 .avif** | 见 §3，许可 = libavif 本体的 BSD-2-Clause。 |

### B.1 格式咨询（consulted，未移植代码）

| 被咨询对象 | 许可 | 本库从中得到什么 | 注释落点 |
|---|---|---|---|
| **libavif**（`write.c`、`avifWriteToneMappedImagePayload`） | BSD-2-Clause，© Joe Drago | `tmap` 载荷布局（version 字节 + ISO 21496-1 结构）、`iref/dimg` 的 `reference_count` 现实形态、`altr` 分组、item id 约定、TD 前缀 | `IsoBmffGainMapProbe.cs:36, 408`；`IsoBmffGainMapWriter.cs:187, 58, 78, 118` |
| **Google libultrahdr v2.0.2**（`decodeGainmapMetadata`、`avifultrahdr.cpp`） | Apache-2.0，© Google LLC | ISO 21496-1 各字段的偏移/flags 位含义；为 `tmap` 单独建 `nclx` 的做法 | `JpegGainMapContainer.cs`（ParseIso21496 文档块）；`IsoBmffGainMapWriter.cs:122-123` |
| **AndroidX `JpegImageCodec`** | Apache-2.0 | hdrgm XMP 声明的「逻辑尺寸」字段读法约定 | `GainMapMetadata.cs:27` |
| **标准正文**：ISO/IEC 21496-1、14496-12（ISOBMFF）、23008-12（AVIF）、CIPA DC-X007（MPF） | 标准文本本身 | 盒布局、位域、MPF TIFF 结构、`offset_size/length_size` 同字节高低半字节等 | 各文件内联注释 |

⚠ 注释里出现的 `write.c:3437-3443` / `avifultrahdr.cpp:506-520` 这类**行号**指向上级仓库某个版本，
本公开仓**不含**那些源码树 ⇒ 行号不可在此仓复核。对应的版本口径：libavif **1.4.2**、
libultrahdr **v2.0.2**（本机 `avifenc --version` 与 `ultrahdr_app` 自报值实测）。

**这些咨询不改变授权**：读别人的实现来确认「格式要求是什么」，与复制其表达性代码，是两件事。
本库的实现是 C# 重写的，且 `Probe`（读）与 `Writer`（写）由本库作者自撰。

#### B.1a 注释里**原文照录**的第三方文本（短句，事实性引用）

| 位置 | 照录的文本 | 属于 |
|---|---|---|
| `IsoBmffGainMapWriter.cs:139` 附近 | `Missing Temporal Delimiter` / `No sequence header available` | ffmpeg 的 `avif`/`obu` demuxer 报错串（用作"为什么要补 TD"的实证） |
| `IsoBmffGainMapWriter.cs:231` | `box[dimg] for 'tmap' item 2 must have exactly 2 entries with distinct ids` | libavif `avifdec` 的报错串（用作"为什么必须写 reference_count"的实证） |

⇒ 这两处是**短事实性文本**（报错串本身），用于固定"为什么这样实现"；不是第三方源代码。
若你的合规口径不接受照录厂商报错串，可以改成转述 —— 但请连同其证明作用一起换掉。

### B.2 ffmpeg 在本仓的角色：只有「被测对象」与「实测事实」

- 本库**不调用** ffmpeg，也不含 ffmpeg 源码。
- 注释里有若干「实测 ffmpeg 的 `avif` muxer 不采纳 `-color_primaries/-color_trc`」一类的陈述
  ⇒ 那是**对本机 ffmpeg 产物跑第三方读数得到的事实**，用于解释本库为什么要自己写 `colr`。

### B.3 随仓分发的第三方**数据**（测试夹具）

| 文件 | 来源 | 许可 |
|---|---|---|
| `fixtures/ref_libavif_colorgrid_gainmapgrid.avif`（2870 B） | libavif `tests/data`（由 `avifgainmaptest` 的 `GainMapTest.CreateGainMapImages` 生成，上游分支 `maryla-uc/libavif` 的 `weirdgainmaps`） | **与 libavif 相同 = BSD-2-Clause**（libavif `tests/data/README.md` 对该文件写的就是「License: same as libavif」） |
| `fixtures/ref_libavif_solo_gainmapgrid.avif`（1875 B） | 同上 | 同上 |

⚠ 分发本仓时请**连这两个夹具一起**保留本文件。libavif 上游 `LICENSE` 的首个条款块是
**BSD-2-Clause**（`Copyright 2019 Joe Drago. All rights reserved.`，只有"保留源码声明"与
"二进制复述声明"两条，没有第 3 条背书条款）；上述两个夹具的许可即按该文件走。
⇒ 再分发源码须保留其声明（第 1 条），打包成二进制/App 时须随文档复述（第 2 条）。

其余 3 个夹具是本机用第三方编码器从自造的原始像素生成的 ⇒ 数据本身不来自第三方仓库：

| 文件 | 生成方式（可复现） |
|---|---|
| `fixtures/ref_ultrahdr_golden.jpg`（4227 B） | `ultrahdr_app -m 0 -p hdr_rgba10.raw -y sdr_rgba8.raw -a 5 -b 3 -t 1 -w 64 -h 48 -M 1 -k 1.0 -K 8.0 -z ref.jpg`（libultrahdr **v2.0.2** 编码器；原始像素由本仓脚本铺） |
| `fixtures/plain_no_gainmap.avif`（330 B） | `avifenc -q 40 plain_input.png plain_no_gainmap.avif`（libavif **1.4.2**） |
| `fixtures/plain_gain_input.avif`（307 B） | `avifenc -q 35 gm_input.png plain_gain_input.avif`（libavif **1.4.2**） |

---

## C. 两部共同：与源仓库的许可关系

- 两个工程都抽自 [ffmpegPictureUI](https://github.com/luoye-cpu/ffmpegPictureUI)（**GPL-3.0**，已公开）；
  各自在本仓的对应位置：内核来自其 `Services/ColorMapping/` 等，容器层来自
  `Services/GainMapDecoder.cs` / `IsoBmffGainMapProbe.cs` / `IsoBmffGainMapWriter.cs`（切点写在各文件头注释）。
- **「唯一版权人」前提 —— 已验证**（不是假设）：该仓 `git log` 全量作者去重 = **1 人**
  （`luoye-cpu`，68 个提交全部），且触及被抽取文件的提交也全部出自同一作者。
  ⇒ 不存在需要他人授权的第三方贡献者，GPL-3.0 → MIT 的重授权在**作者原创部分**上成立。
  （⚠ 若日后引入外部贡献者，新增部分的重授权需另行取得其同意。）
- ⚠ **宿主仍是权威**：本仓是抽取副本，尚未切 `ProjectReference` ⇒ 改到这些逻辑时**两边都要动**；
  副本与宿主的既定差异（命名空间、可见性提升、`JpegContainer` → `JpegGainMapContainer` 更名）
  记在 `GainMapKit.md` §7 与 `ColorMatrix.md` §5/§7。
- A.1 的 libjxl 派生件原本是 BSD-3-Clause（宽松许可），**不产生 GPL 传染**；其署名义务由本文件履行。
