using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GainMapKit;

// ══════════════════════════════════════════════════════════════════════════════
//  GainMapKit 自检 —— 零依赖控制台断言（不引 xunit / NUnit），全过 exit 0 / 有失败 exit 非 0。
//
//  判据分两类，口径不同，别混淆：
//    · **外部参照**：夹具由第三方实现产出（Google libultrahdr v2.0.2、libavif 1.4.2），
//      期望值取自第三方工具的读数（`ultrahdr_app -m 1 -P -j` / `avifdec --info`），不是本库自述。
//      这一类才谈得上「与第三方互通」。
//    · **构造恒等 / 同源自洽**：夹具在本文件内按 ISO 布局手铺，或 writer→probe 互为对偶
//      （同一作者的两半）。这一类只能证明「不矛盾、判据有牙」，**不能**证明与第三方互通。
//
//  用法：dotnet run --project tests/GainMapKit.SelfTest -c Release [-- --dump]
// ══════════════════════════════════════════════════════════════════════════════

int pass = 0, fail = 0;
bool dump = args.Contains("--dump");

void CK(string name, bool ok, string detail = "")
{
    if (ok) { pass++; if (!dump) Console.WriteLine($"PASS {name}{(detail.Length > 0 ? "  " + detail : "")}"); }
    else { fail++; Console.WriteLine($"FAIL {name}  {detail}"); }
}

static string Fmt(double v) => v.ToString("G8", CultureInfo.InvariantCulture);

string FixDir = Path.Combine(AppContext.BaseDirectory, "fixtures");

// ── 0. 夹具登记（名字 + 尺寸 + SHA256 前 16）：任一不符 ⇒ fail-closed 退出 ─────────
var fixtures = new (string name, int size, string sha16)[]
{
    ("ref_ultrahdr_golden.jpg",                 4227, "8de5ae7766357f33"),  // libultrahdr v2.0.2 编码
    ("ref_libavif_colorgrid_gainmapgrid.avif",  2870, "c424c43fe4bab3b8"),  // libavif 1.4.2 产物
    ("ref_libavif_solo_gainmapgrid.avif",       1875, "d783e0d9ce778f97"),  // libavif 1.4.2 产物
    ("plain_no_gainmap.avif",                    330, "4209efb973728f18"),  // 本仓 avifenc 1.4.2 生成，无增益图
    ("plain_gain_input.avif",                    307, "a7aa586aeacacf02"),  // 另一张单图 AVIF，作增益图输入（16x12）
};
var bytes = new Dictionary<string, byte[]>();
Console.WriteLine("=== 0. 夹具完整性（按名字取 + 尺寸 + SHA256 前缀；件不齐不往下判） ===");
foreach (var f in fixtures)
{
    string p = Path.Combine(FixDir, f.name);
    if (!File.Exists(p)) { CK($"夹具在位 {f.name}", false, $"未找到 {p}"); continue; }
    byte[] d = File.ReadAllBytes(p);
    bytes[f.name] = d;
    string sha = Convert.ToHexString(SHA256.HashData(d))[..16].ToLowerInvariant();
    CK($"夹具完整 {f.name}", d.Length == f.size && sha == f.sha16,
       $"{d.Length} B（期望 {f.size}）sha={sha}（期望 {f.sha16}）");
}
if (bytes.Count != fixtures.Length)
{
    Console.WriteLine($"===== GainMapKit SelfTest: {pass} PASS / {fail} FAIL =====");
    return 1;
}
byte[] uhdr = bytes["ref_ultrahdr_golden.jpg"];
byte[] grid = bytes["ref_libavif_colorgrid_gainmapgrid.avif"];
byte[] solo = bytes["ref_libavif_solo_gainmapgrid.avif"];
byte[] plain = bytes["plain_no_gainmap.avif"];
byte[] gainIn = bytes["plain_gain_input.avif"];

double Lin(float log2v) => Math.Pow(2.0, log2v);
bool NearRel(double got, double want, double tol) => Math.Abs(got - want) <= tol * Math.Max(1.0, Math.Abs(want));

// ── 1. UltraHDR JPEG ↔ 厂商权威读数（外部参照） ────────────────────────────────
// 读数来源（本机实测，libultrahdr v2.0.2）：ultrahdr_app -m 1 -P -j ref_ultrahdr_golden.jpg
//   --maxContentBoost 5.86678 0.0486263 5.20679   --minContentBoost 1 1 1
//   --gamma 1 1 1                --offsetSdr 1e-07 x3   --offsetHdr 1e-07 x3
//   --hdrCapacityMin 1           --hdrCapacityMax 4.92611      --useBaseColorSpace 0
// 本库存储约定：GainMap* / HdrCapacity* 存 **log2 域的分数值 N/D**，标量只取通道 0
//   ⇒ 与厂商的**线性**读数对撞必须过 2^x 这一道换算（不是直接比数值）。
Console.WriteLine();
Console.WriteLine("=== 1. UltraHDR JPEG ↔ libultrahdr v2.0.2 权威读数（外部参照） ===");
var jpg = JpegGainMapContainer.Parse(uhdr);
CK("JPEG 容器解析成功", jpg != null);
if (jpg == null) { Console.WriteLine($"===== GainMapKit SelfTest: {pass} PASS / {fail} FAIL ====="); return 1; }
CK("顶层 JPEG 张数 = 2（底图 + 增益图）", jpg.Images.Count == 2, $"实 {jpg.Images.Count}");
CK("IsGainMapContainer() = true", jpg.IsGainMapContainer());
CK("ISO 元数据存在", jpg.IsoMetadata != null);
CK("ISO APP2 落在副图（index 1）", jpg.IsoImageIndex == 1, $"实 {jpg.IsoImageIndex}");
CK("底图尺寸 = 64x48（与编码请求一致）",
   jpg.Images.Count > 0 && jpg.Images[0].W == 64 && jpg.Images[0].H == 48,
   jpg.Images.Count > 0 ? $"实 {jpg.Images[0].W}x{jpg.Images[0].H}" : "无图");
CK("副图可定位", jpg.GetSecondaryImage() != null);
var m = jpg.IsoMetadata!;
CK("Channels = 3（编码传了 -M 1）", m.Channels == 3, $"实 {m.Channels}");
CK("GainMapMax → 厂商 maxContentBoost[0] = 5.86678",
   NearRel(Lin(m.GainMapMax), 5.86678, 2e-4), $"2^{Fmt(m.GainMapMax)} = {Fmt(Lin(m.GainMapMax))}");
CK("GainMapMin → 厂商 minContentBoost[0] = 1",
   NearRel(Lin(m.GainMapMin), 1.0, 2e-4), $"实 {Fmt(Lin(m.GainMapMin))}");
CK("Gamma → 厂商 gamma[0] = 1", NearRel(m.Gamma, 1.0, 2e-4), $"实 {Fmt(m.Gamma)}");
CK("OffsetSdr → 厂商 1e-07", NearRel(m.OffsetSdr, 1e-07, 1e-3), $"实 {Fmt(m.OffsetSdr)}");
CK("OffsetHdr → 厂商 1e-07", NearRel(m.OffsetHdr, 1e-07, 1e-3), $"实 {Fmt(m.OffsetHdr)}");
CK("HdrCapacityMin → 厂商 1（线性）", NearRel(Lin(m.HdrCapacityMin), 1.0, 2e-4), $"实 {Fmt(Lin(m.HdrCapacityMin))}");
CK("HdrCapacityMax → 厂商 4.92611（线性）",
   NearRel(Lin(m.HdrCapacityMax), 4.92611, 5e-4), $"2^{Fmt(m.HdrCapacityMax)} = {Fmt(Lin(m.HdrCapacityMax))}");
CK("UseBaseColorSpace = false（厂商 --useBaseColorSpace 0）", !m.UseBaseColorSpace);
CK("反控：本件逐通道确实不同（ch0=5.86678 vs 厂商 ch1=0.0486263）",
   NearRel(Lin(m.GainMapMax), 5.86678, 2e-4) && !NearRel(Lin(m.GainMapMax), 0.0486263, 1e-2),
   $"实 ch0={Fmt(Lin(m.GainMapMax))}");
// 反控：MPF 端序判据不能被「永远当小端」糊过去
CK("MPF 结构被读到（条目数或基准偏移有实值）",
   jpg.MpfEntries.Count > 0 || jpg.MpfBaseOffset >= 0,
   $"entries={jpg.MpfEntries.Count} baseOff={jpg.MpfBaseOffset} le={jpg.MpfLittleEndian}");

// ── 2. ISOBMFF 探测 ↔ avifdec 读数（外部参照）+ 负控 ──────────────────────────
Console.WriteLine();
Console.WriteLine("=== 2. ISOBMFF 探测 ↔ avifdec 1.4.2 读数（外部参照） ===");
if (dump)
{
    DumpProbe("colorgrid", grid); DumpProbe("solo", solo); DumpProbe("plain", plain); DumpProbe("jpeg", uhdr);
    Console.WriteLine("[walk-grid] top: " + Describe(grid, 0, grid.Length));
    var mtG = FindTop(grid, "meta");
    if (mtG != null)
        Console.WriteLine("[walk-grid] meta children: " + Describe(grid, mtG.Value.start + 12, mtG.Value.end));
    var irG = FindMetaChild(grid, "iref");
    if (irG != null)
    {
        Console.WriteLine("[walk-grid] iref entries: " + Describe(grid, irG.Value.start + 12, irG.Value.end));
        Console.WriteLine("[hex-grid] iref 原始 40 B: " +
            string.Join(" ", Slice(grid, irG.Value.start, 40).Select(x => x.ToString("X2"))));
        var dmG = FindIrefEntry(grid, "dimg");
        if (dmG != null)
            Console.WriteLine("[hex-grid] dimg 原始 24 B: " +
                string.Join(" ", Slice(grid, dmG.Value.start, 24).Select(x => x.ToString("X2"))));
    }
}

var p1 = IsoBmffGainMapProbe.Probe(grid);
CK("libavif 件：识别为 ISOBMFF 且定案", p1.IsIsoBmff && p1.Determinate, p1.FailReason ?? "");
CK("libavif 件：报出增益图，形式 = tmap", p1.HasGainMap && p1.Form == "tmap", $"form={p1.Form}");
CK("libavif 件：三个 item id 互异且都解析出来",
   p1.PrimaryItemId >= 1 && p1.TmapItemId >= 1 && p1.GainMapItemId >= 1 &&
   p1.PrimaryItemId != p1.TmapItemId && p1.PrimaryItemId != p1.GainMapItemId &&
   p1.TmapItemId != p1.GainMapItemId,
   $"primary={p1.PrimaryItemId} tmap={p1.TmapItemId} gm={p1.GainMapItemId}");
CK("libavif 件：增益图编码识别为 av01 ⇒ ffmpeg 走 -f obu",
   p1.GainMapCodec == "av01" && p1.GainMapFfmpegFormat == "obu",
   $"codec={p1.GainMapCodec} ff={p1.GainMapFfmpegFormat}");
CK("libavif 件：ftyp 品牌集含 tmap（读侧据此识别派生项）", p1.Brands.Contains("tmap"), $"[{p1.Brands}]");
CK("libavif 件：tmap 载荷长度 = 142（= 1 + 2+2+1+16 + 3×40，独立分母 3 通道）",
   p1.TmapPayload?.Length == 142, $"实 {p1.TmapPayload?.Length ?? -1}");
CK("libavif 件：增益图 item 字节被取出", (p1.GainMapItemData?.Length ?? 0) > 0,
   $"实 {p1.GainMapItemData?.Length ?? -1} B");
// avifdec --info 对同一件报：Base Headroom 3.00 / Alternate Headroom 0.00（**log2 域**）
var gm1 = ParseTmap(p1);
CK("libavif 件 → avifdec：baseHdrHeadroom 3.00（log2，即 8×）",
   gm1 != null && NearRel(gm1.HdrCapacityMin, 3.0, 1e-3), $"实 {Fmt(gm1?.HdrCapacityMin ?? -999)}");
CK("libavif 件 → avifdec：alternateHdrHeadroom 0.00（log2，即 1×）",
   gm1 != null && NearRel(gm1.HdrCapacityMax, 0.0, 1e-3), $"实 {Fmt(gm1?.HdrCapacityMax ?? -999)}");
CK("libavif 件：载荷首字节 = version 0", (p1.TmapPayload?[0] ?? 0xFF) == 0);

// 能力边界（实测，不作「通过」宣传）：grid 型增益图项本库**不定案**
var p2 = IsoBmffGainMapProbe.Probe(solo);
CK("边界如实：grid 型增益图 ⇒ Determinate=false 且点名原因（而非谎报「无增益图」）",
   p2.Determinate == false && p2.HasGainMap && (p2.FailReason ?? "").Contains("dimg"),
   $"det={p2.Determinate} has={p2.HasGainMap} reason={p2.FailReason ?? "-"} gmItem={p2.GainMapItemId}");
CK("边界如实：即便不定案，tmap 载荷仍取得出（元数据不丢）",
   p2.TmapPayload?.Length == 142, $"实 {p2.TmapPayload?.Length ?? -1}");

// 负控：没有增益图的真实 AVIF ⇒ 必须判「无」
var p3 = IsoBmffGainMapProbe.Probe(plain);
CK("负控：无增益图的 AVIF ⇒ IsIsoBmff 真、HasGainMap 假", p3.IsIsoBmff && !p3.HasGainMap,
   $"iso={p3.IsIsoBmff} has={p3.HasGainMap} det={p3.Determinate}");
// 负控：把 JPEG 当 BMFF 喂进去 ⇒ magic 门必须拦住
var p4 = IsoBmffGainMapProbe.Probe(uhdr);
CK("负控：JPEG 喂 BMFF 探测 ⇒ 判非 ISOBMFF", !p4.IsIsoBmff && !IsoBmffGainMapProbe.LooksLikeIsoBmff(uhdr));
// 负控：逐点截断不得抛异常（解析器必须失步即停，而不是越界读）
int truncBad = 0; string truncDetail = "";
foreach (int cut in new[] { 8, 12, 16, 24, 32, 64, 128, 256, 512, 1024, grid.Length - 1 })
{
    var slice = grid[..cut];
    try { _ = IsoBmffGainMapProbe.Probe(slice); }
    catch (Exception e) { truncBad++; if (truncDetail == "") truncDetail = $"cut={cut}: {e.GetType().Name}"; }
}
CK("负控：11 个截断点全部不抛异常", truncBad == 0, truncDetail);

// 苛刻夹具：把 libavif 写的「带 reference_count」的 dimg 盒改成 ISO 的「无 count」形态，
// 两种读法必须都收敛到同一个增益图 item（源码注释的判据，此处实测）。
var (convOk, convDetail) = DimgBothReadingsConverge(grid, p1.TmapItemId);
CK("iref 两种合法读法收敛（删掉 count 后仍指到同一增益图 item）", convOk, convDetail);

// ── 3. ISO 21496-1 合成载荷（构造恒等：本文件独立铺字节，不调本库 writer） ──────
Console.WriteLine();
Console.WriteLine("=== 3. ISO 21496-1 合成载荷（构造恒等，独立铺字节） ===");
static List<byte> Be32List(uint v) => new() { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
static void AddU16(List<byte> b, ushort v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }

// 3a 独立分母模式（libultrahdr / libavif 实际采用的形态）：
//   [minV u16][writerV u16][flags u8][baseN baseD altN altD] + 每通道 (min,max,gamma,offSdr,offHdr) 各 (N,D)
static byte[] LayIndependent(byte flags, (uint n, uint d) baseHr, (uint n, uint d) altHr,
                             (uint n, uint d)[] chMin, (uint n, uint d)[] chMax,
                             (uint n, uint d)[] chGamma, (uint n, uint d)[] chOffS, (uint n, uint d)[] chOffH)
{
    var b = new List<byte>();
    AddU16(b, 0); AddU16(b, 0); b.Add(flags);
    foreach (var (n, d) in new[] { baseHr, altHr }) { b.AddRange(Be32List(n)); b.AddRange(Be32List(d)); }
    int ch = (flags & 0x80) != 0 ? 3 : 1;
    for (int c = 0; c < ch; c++)
        foreach (var arr in new[] { chMin, chMax, chGamma, chOffS, chOffH })
        { b.AddRange(Be32List(arr[c].n)); b.AddRange(Be32List(arr[c].d)); }
    return b.ToArray();
}
var one = (1u, 1u);
// 逐通道 min ≤ max 是增益元数据的自洽前提（libavif 会直接拒收 max<min 的 tmap）；
// 通道 0 取 1/4→7/2，与通道 1/2 明显不同量级，兼作「只读通道 0」的反控。
byte[] indep = LayIndependent(0x80, (3, 1), (0, 1),
    new[] { (1u, 4u), (1u, 2u), (1u, 8u) }, new[] { (7u, 2u), (3u, 2u), (5u, 4u) },
    new[] { one, one, one }, new[] { (1u, 10000000u), one, one }, new[] { one, one, one });
CK("独立分母：载荷长度 = 141（2+2+1+16+3×40，未加 version 前导字节）", indep.Length == 141, $"实 {indep.Length}");
var si = JpegGainMapContainer.ParseIso21496(indep, 0, indep.Length);
CK("独立分母：解析成功且 Channels=3", si != null && si.Channels == 3, $"实 {si?.Channels.ToString() ?? "null"}");
CK("独立分母：HdrCapacityMin = baseN/baseD = 3", si != null && NearRel(si.HdrCapacityMin, 3.0, 1e-6),
   $"实 {Fmt(si?.HdrCapacityMin ?? -1)}");
CK("独立分母：HdrCapacityMax = altN/altD = 0", si != null && NearRel(si.HdrCapacityMax, 0.0, 1e-6),
   $"实 {Fmt(si?.HdrCapacityMax ?? -1)}");
CK("独立分母：标量取通道 0（min=1/4=0.25，而非通道 1 的 1/2）", si != null && NearRel(si.GainMapMin, 0.25, 1e-6),
   $"实 {Fmt(si?.GainMapMin ?? -1)}");
CK("独立分母：max 取通道 0（7/2=3.5，而非通道 1 的 3/2）", si != null && NearRel(si.GainMapMax, 3.5, 1e-6),
   $"实 {Fmt(si?.GainMapMax ?? -1)}");
CK("独立分母：offsetSdr = 1/10000000", si != null && NearRel(si.OffsetSdr, 1e-07, 1e-9),
   $"实 {Fmt(si?.OffsetSdr ?? -1)}");

// 3b 公共分母模式：[denom][baseN][altN] + 每通道 5 个分子 ⇒ 3 通道共 77 字节
static byte[] LayCommon(byte flags, uint denom, uint baseN, uint altN,
                        uint[] chMin, uint[] chMax, uint[] chGamma, uint[] chOffS, uint[] chOffH)
{
    var b = new List<byte>();
    AddU16(b, 0); AddU16(b, 0); b.Add(flags);
    b.AddRange(Be32List(denom)); b.AddRange(Be32List(baseN)); b.AddRange(Be32List(altN));
    int ch = (flags & 0x80) != 0 ? 3 : 1;
    for (int c = 0; c < ch; c++)
        foreach (var arr in new[] { chMin, chMax, chGamma, chOffS, chOffH }) b.AddRange(Be32List(arr[c]));
    return b.ToArray();
}
byte[] comm = LayCommon(0x88, 4, 12, 0, new uint[] { 2, 9, 1 }, new uint[] { 14, 3, 5 },
                        new uint[] { 4, 8, 4 }, new uint[] { 1, 1, 1 }, new uint[] { 1, 1, 1 });
CK("公共分母：载荷长度 = 77（2+2+1+12+3×20）", comm.Length == 77, $"实 {comm.Length}");
var sc = JpegGainMapContainer.ParseIso21496(comm, 0, comm.Length);
CK("公共分母：按 flags bit3 走通，min=2/4=0.5", sc != null && NearRel(sc.GainMapMin, 0.5, 1e-6),
   $"实 {Fmt(sc?.GainMapMin ?? -1)}（若误走独立分母读法则为 0 或 null）");
CK("公共分母：max=14/4=3.5", sc != null && NearRel(sc.GainMapMax, 3.5, 1e-6), $"实 {Fmt(sc?.GainMapMax ?? -1)}");
CK("公共分母：容量 min=12/4=3、max=0", sc != null && NearRel(sc.HdrCapacityMin, 3, 1e-6)
   && NearRel(sc.HdrCapacityMax, 0, 1e-6), $"实 {Fmt(sc?.HdrCapacityMin ?? -1)}/{Fmt(sc?.HdrCapacityMax ?? -1)}");

// 3c flags 逐位：bit7 通道数 / bit6 useBaseColorSpace / bit2 backwardDirection / gamma 0 ⇒ 归 1
byte[] gray = LayIndependent(0x00, (1, 1), (2, 1), new[] { (0u, 1u) }, new[] { (1u, 1u) },
                             new[] { (0u, 1u) }, new[] { (0u, 1u) }, new[] { (0u, 1u) });
var sg = JpegGainMapContainer.ParseIso21496(gray, 0, gray.Length);
CK("flags bit7=0 ⇒ 单通道（Channels=1）", sg != null && sg.Channels == 1, $"实 {sg?.Channels.ToString() ?? "null"}");
CK("gamma 分子 0 ⇒ 归一为 1（不落在 0）", sg != null && NearRel(sg.Gamma, 1.0, 1e-6), $"实 {Fmt(sg?.Gamma ?? -1)}");
byte[] f64 = LayIndependent(0x44, (1, 1), (1, 1), new[] { (0u, 1u) }, new[] { (1u, 1u) },
                            new[] { (1u, 1u) }, new[] { (0u, 1u) }, new[] { (0u, 1u) });
var s64 = JpegGainMapContainer.ParseIso21496(f64, 0, f64.Length);
CK("flags bit6 ⇒ UseBaseColorSpace=true 且 bit2 ⇒ BaseRenditionIsHdr=true",
   s64 != null && s64.UseBaseColorSpace && s64.BaseRenditionIsHdr,
   $"useBase={s64?.UseBaseColorSpace.ToString() ?? "null"} bwd={s64?.BaseRenditionIsHdr.ToString() ?? "null"}");
CK("flags bit7=0（单通道）且 bit3=0 ⇒ 独立分母、长度 61",
   f64.Length == 61 && s64 != null && s64.Channels == 1, $"实 len={f64.Length} ch={s64?.Channels.ToString() ?? "null"}");

// 3d 版本门与截断：这两条是「摘掉判据就会放行」的位置
byte[] v1 = (byte[])indep.Clone(); v1[0] = 1;
CK("负控：min_version ≠ 0 ⇒ 拒绝（null）", JpegGainMapContainer.ParseIso21496(v1, 0, v1.Length) == null);
CK("负控：短 1 字节 ⇒ 拒绝（长度门真的在管）",
   JpegGainMapContainer.ParseIso21496(indep, 0, indep.Length - 1) == null);
CK("负控：头 4 字节 ⇒ 拒绝（不足最小记录）",
   JpegGainMapContainer.ParseIso21496(indep, 0, 4) == null);
CK("负控：off 越界不抛异常", SafeParse(indep, indep.Length - 2, 40));

// ── 4. writer → probe 自洽（同源对偶；只证不矛盾，不证与第三方互通） ────────────
Console.WriteLine();
Console.WriteLine("=== 4. writer 容器组装（同源自洽 + 构造恒等；见 README 的口径声明） ===");
var tmapPayload = IsoBmffGainMapWriter.BuildTmapPayload(indep);
CK("BuildTmapPayload = 1 个前导 version(0) + 原载荷",
   tmapPayload.Length == indep.Length + 1 && tmapPayload[0] == 0, $"实 {tmapPayload.Length} B");

// 与宿主链路同形：底图与增益图是**两张不同的单图 AVIF**（此处均由本仓 avifenc 1.4.2 生成）
var built = IsoBmffGainMapWriter.Build(plain, gainIn, tmapPayload, 9, 16, 9, true, 16);
CK("Build 成功（底图 32x24 + 增益图 16x12 两张不同单图）", built.Ok, built.FailReason ?? "");
if (dump && built.Ok)
    File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "built_by_selftest.avif"), built.Data!);
if (dump && built.Data != null)
{
    DumpColrBoxes("built", built.Data);
    DumpColrBoxes("plain-ref(1-13-6)", plain);
    var mtB = FindTop(built.Data, "meta");
    if (mtB != null) Console.WriteLine("[walk-built] meta 子盒: " + Describe(built.Data, mtB.Value.start + 12, mtB.Value.end));
    var iprpB = FindMetaChild(built.Data, "iprp");
    if (iprpB != null)
    {
        Console.WriteLine("[walk-built] iprp 子盒: " + Describe(built.Data, iprpB.Value.start + 8, iprpB.Value.end));
        foreach (var c in Walk(built.Data, iprpB.Value.start + 8, iprpB.Value.end))
            if (c.type == "ipco")
                Console.WriteLine("[walk-built] ipco 性质盒: " + Describe(built.Data, c.start + 8, c.end));
    }
}
if (built.Ok)
{
    var pb = IsoBmffGainMapProbe.Probe(built.Data!);
    CK("自洽：自家产物能被自家探测读出 tmap", pb.IsIsoBmff && pb.HasGainMap && pb.Form == "tmap",
       $"iso={pb.IsIsoBmff} has={pb.HasGainMap} form={pb.Form} reason={pb.FailReason ?? "-"}");
    CK("自洽：tmap 载荷逐字节回取相等（未改写）",
       pb.TmapPayload != null && pb.TmapPayload.AsSpan().SequenceEqual(tmapPayload),
       $"实 {(pb.TmapPayload?.Length ?? -1)} B vs 入 {tmapPayload.Length} B");
    CK("构造恒等：ftyp 品牌集含 tmap", pb.Brands.Contains("tmap"), $"[{pb.Brands}]");
    // 底图 colr 必须是请求的那组 CICP（9/16/9）—— 三元组按盒框取，不靠字节扫描碰运气。
    // 外部印证：avifdec --info 对本件报 Color Primaries 1...（见 README §4 的实测记录）
    var colrs = IpcoColrTriples(built.Data!);
    CK("构造恒等：底图 colr 被改写为请求的 CICP（9/16/9）",
       colrs.Count > 0 && colrs[0] == (9, 16, 9), $"ipco 内 colr/nclx 三元组序列 = {string.Join(" ", colrs)}");
    // 外部印证（本机 avifdec 1.4.2 --info 对本件的读数）：
    //   * Resolution : 32x24   * Color Primaries: 9  * Transfer Char. : 16  * Matrix Coeffs. : 9
    //   * Gain map   : 16x12 pixels, 8 bit, YUV444, Full Range, ..., Base Headroom 3.00 (HDR), Alternate Headroom 0.00 (SDR)
    //   * Alternate image: Color Primaries: 9 / Transfer Char. : 16
    var ispes = IpcoIspeSizes(built.Data!);
    CK("外部印证：ispe 含底图 32x24 与增益图 16x12（与 avifdec 报的一致）",
       ispes.Contains((32, 24)) && ispes.Contains((16, 12)), $"实 ispe 序列 = {string.Join(" ", ispes)}");
    CK("构造恒等：增益图 item 带 Temporal Delimiter（12 00 开头，只一次）",
       StartsOnceWithTd(pb.GainMapItemData), $"首 4 字节 = {Hex4(pb.GainMapItemData)}");
    CK("构造恒等：三个 item id 互异", pb.PrimaryItemId != pb.TmapItemId && pb.TmapItemId != pb.GainMapItemId
       && pb.PrimaryItemId >= 1, $"p={pb.PrimaryItemId} t={pb.TmapItemId} g={pb.GainMapItemId}");
    CK("构造恒等：BuildResult 报告的 item 尺寸非 0",
       built.BaseItemBytes > 0 && built.GainMapItemBytes > 0 && built.TmapPayloadBytes == tmapPayload.Length,
       $"base={built.BaseItemBytes} gm={built.GainMapItemBytes} tmap={built.TmapPayloadBytes}");
    // 反控：grpl/altr 是 libavif 识别「替代图」的必要条件 —— 删掉 grpl 盒后探测必须改判
    var (grplOk, grplDetail) = GrplRemovalChangesVerdict(built.Data!);
    CK("反控：删掉 grpl 后判据改变（说明该盒确实承重，不是恒真）", grplOk, grplDetail);
    // 实测边界：抹掉 ftyp 的 tmap 品牌后，**自家读侧**判据不变 ⇒ 本库的定性靠 iref/tmap 项而非品牌；
    // 写入侧仍带该品牌（上一条已断言），供第三方识别派生项容器。
    var (brandChanged, brandDetail) = BrandRemovalChangesVerdict(built.Data!);
    CK("边界如实：自家读侧不靠 ftyp 品牌定性（抹掉 tmap ⇒ 判据不变，实测）", !brandChanged, brandDetail);
}

// 负控：底图给 JPEG（非 AVIF）⇒ 必须显式失败并点名，而不是静默产出坏容器
var badIn = IsoBmffGainMapWriter.Build(uhdr, plain, tmapPayload);
CK("负控：底图非 ISOBMFF ⇒ Ok=false 且失败原因点名",
   !badIn.Ok && (badIn.FailReason ?? "").StartsWith("[GainMap-Avif]"), $"reason={badIn.FailReason ?? "-"}");
// 边界如实（实测，不是「通过」的宣传）：本库的 writer 把 tmap 载荷当**不透明字节**，
// 不校验增益元数据自身的自洽性。反例：把某通道的 max 铺到小于 min ——
// 本库照样产出容器，而 libavif avifdec 对同一件报
//   "Invalid tone mapped image item / Per-channel max is less than per-channel min in gain map metadata"
// ⇒ 元数据的合法性由**调用方**负责；本库不代替第三方校验。
byte[] inverted = LayIndependent(0x80, (3, 1), (0, 1),
    new[] { (7u, 2u), (1u, 2u), (1u, 8u) }, new[] { (1u, 4u), (3u, 2u), (5u, 4u) },
    new[] { one, one, one }, new[] { (1u, 10000000u), one, one }, new[] { one, one, one });
var badMeta = IsoBmffGainMapWriter.Build(plain, gainIn, IsoBmffGainMapWriter.BuildTmapPayload(inverted));
var bm = JpegGainMapContainer.ParseIso21496(inverted, 0, inverted.Length);
CK("边界如实：max<min 的元数据本库仍接受（第三方 avifdec 会拒 ⇒ 校验责任在调用方）",
   badMeta.Ok && bm != null && bm.GainMapMin > bm.GainMapMax,
   $"Ok={badMeta.Ok} min={bm?.GainMapMin.ToString() ?? "null"} max={bm?.GainMapMax.ToString() ?? "null"}");
var badPayload = IsoBmffGainMapWriter.Build(plain, plain, Array.Empty<byte>());
CK("负控：空 tmap 载荷 ⇒ 拒绝（不产出无元数据的容器）", !badPayload.Ok, badPayload.FailReason ?? "无原因");

// ── 5. 上限与外部入口 ────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== 5. 上限常量与文件入口 ===");
CK("MaxManagedParseBytes = 256 MiB（与宿主同口径，登记在此以免漂移）",
   IsoBmffGainMapProbe.MaxManagedParseBytes == 256L * 1024 * 1024,
   $"实 {IsoBmffGainMapProbe.MaxManagedParseBytes}");
var fromFile = IsoBmffGainMapProbe.ProbeFileAsync(Path.Combine(FixDir, "ref_libavif_colorgrid_gainmapgrid.avif"))
                              .GetAwaiter().GetResult();
CK("ProbeFileAsync 与 Probe(字节) 结论一致",
   fromFile != null && fromFile.HasGainMap && fromFile.TmapItemId == p1.TmapItemId &&
   fromFile.GainMapItemId == p1.GainMapItemId,
   $"file: has={fromFile?.HasGainMap.ToString() ?? "null"} t={fromFile?.TmapItemId ?? -1} g={fromFile?.GainMapItemId ?? -1}");
var fast = IsoBmffGainMapProbe.HasTmapItemFast(Path.Combine(FixDir, "plain_no_gainmap.avif"));
CK("HasTmapItemFast：无增益图片 ⇒ false（不是 null「未知」）", fast == false, $"实 {fast?.ToString() ?? "null"}");
var fast2 = IsoBmffGainMapProbe.HasTmapItemFast(Path.Combine(FixDir, "ref_libavif_colorgrid_gainmapgrid.avif"));
CK("HasTmapItemFast：有增益图片 ⇒ true", fast2 == true, $"实 {fast2?.ToString() ?? "null"}");
var fast3 = IsoBmffGainMapProbe.HasTmapItemFast(Path.Combine(FixDir, "ref_ultrahdr_golden.jpg"));
CK("HasTmapItemFast：JPEG（非 BMFF）⇒ false 且不抛", fast3 == false, $"实 {fast3?.ToString() ?? "null"}");

Console.WriteLine();
Console.WriteLine($"===== GainMapKit SelfTest: {pass} PASS / {fail} FAIL =====");
return fail == 0 ? 0 : 1;

// ── helpers ─────────────────────────────────────────────────────────────────
static GainMapMetadata? ParseTmap(IsoBmffGainMapProbe.Result r)
{
    var pl = r.TmapPayload;
    return pl is { Length: > 1 } ? JpegGainMapContainer.ParseIso21496(pl, 1, pl.Length - 1) : null;
}

static bool SafeParse(byte[] d, int off, int len)
{
    try { _ = JpegGainMapContainer.ParseIso21496(d, off, len); return true; }
    catch { return false; }
}

// 把 libavif 写的「带 reference_count」dimg 盒改写成 ISO 14496-12 的「无 count」形态
// （删 2 字节的 count，并把 dimg / iref / meta 三层盒长各减 2），
// 断言两种读法都指向同一个增益图 item（源码注释的判据，此处实测）。
static (bool, string) DimgBothReadingsConverge(byte[] grid, int tmapId)
{
    var ir0 = FindMetaChild(grid, "iref");
    (int start, int end, string type)? dm = null;
    if (ir0 != null)
        foreach (var ent in Walk(grid, ir0.Value.start + 12, ir0.Value.end))
        {
            if (ent.type != "dimg") continue;
            if (((grid[ent.start + 8] << 8) | grid[ent.start + 9]) != tmapId) continue;
            dm = ent; break;
        }
    var ir = FindMetaChild(grid, "iref");
    var mt = FindTop(grid, "meta");
    if (dm == null || ir == null || mt == null)
        return (false, $"找不到 from=tmap({tmapId}) 的 dimg：dm={(dm == null ? "null" : dm.Value.start.ToString())} " +
                       $"iref={(ir == null ? "null" : "ok")} meta={(mt == null ? "null" : "ok")}");
    int d = dm!.Value.start;
    int from = (grid[d + 8] << 8) | grid[d + 9];
    int count = (grid[d + 10] << 8) | grid[d + 11];
    if (count < 2 || count > 16 || d + 12 + count * 2 > grid.Length)
        return (false, $"dimg count 读数不合理：{count}");
    var to = Enumerable.Range(0, count).Select(i => (grid[d + 12 + i * 2] << 8) | grid[d + 12 + i * 2 + 1]).ToList();

    var before = IsoBmffGainMapProbe.Probe(grid);

    var b = new List<byte>();
    b.AddRange(grid[..(d + 10)]);          // 保留到 from 字段末
    b.AddRange(grid[(d + 12)..]);          // 跳过 count 字段
    var outB = b.ToArray();
    Sub32At(outB, d, 2); Sub32At(outB, ir.Value.start, 2); Sub32At(outB, mt.Value.start, 2);
    var after = IsoBmffGainMapProbe.Probe(outB);

    bool ok = from >= 1 && to.Count == 2 && before.GainMapItemId >= 1 && after.GainMapItemId >= 1 &&
              after.GainMapItemId == before.GainMapItemId && after.TmapItemId == before.TmapItemId &&
              after.PrimaryItemId == before.PrimaryItemId;
    return (ok, $"dimg from={from} to=[{string.Join(",", to)}] | 带count: p={before.PrimaryItemId} " +
                $"t={before.TmapItemId} gm={before.GainMapItemId} | 无count: p={after.PrimaryItemId} " +
                $"t={after.TmapItemId} gm={after.GainMapItemId} det={after.Determinate} " +
                $"reason={after.FailReason ?? "-"}");
}

// 独立实现的盒框扫描（与库内解析器不同一份代码；先证框得住，再谈内容）
static List<(int start, int end, string type)> Walk(byte[] d, int from, int to)
{
    var list = new List<(int, int, string)>();
    int p = from;
    while (p + 8 <= to)
    {
        uint size = Be32At(d, p);
        string type = Encoding.ASCII.GetString(d, p + 4, 4);
        if (size < 8 || p + (long)size > to) break;
        list.Add((p, p + (int)size, type));
        p += (int)size;
    }
    return list;
}

static (int start, int end, string type)? FindTop(byte[] d, string type)
{
    foreach (var b in Walk(d, 0, d.Length)) if (b.type == type) return b;
    return null;
}

// meta 是 fullbox：size(4)+type(4)+version/flags(4) ⇒ 子盒从 start+12 起
static (int start, int end, string type)? FindIrefEntry(byte[] d, string type)
{
    var ir = FindMetaChild(d, "iref");
    if (ir == null) return null;
    foreach (var ent in Walk(d, ir.Value.start + 12, ir.Value.end)) if (ent.type == type) return ent;
    return null;
}

static (int start, int end, string type)? FindMetaChild(byte[] d, string type)
{
    var mt = FindTop(d, "meta");
    if (mt == null) return null;
    foreach (var b in Walk(d, mt.Value.start + 12, mt.Value.end)) if (b.type == type) return b;
    return null;
}

// iref 同为 fullbox ⇒ 引用盒条目从 start+12 起
static int Be16At(byte[] d, int o) => (d[o] << 8) | d[o + 1];

static uint Be32At(byte[] d, int o)
    => ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

static void Sub32At(byte[] d, int o, uint v)
{
    uint n = Be32At(d, o) - v;
    d[o] = (byte)(n >> 24); d[o + 1] = (byte)(n >> 16); d[o + 2] = (byte)(n >> 8); d[o + 3] = (byte)n;
}

static List<(int, int, int)> IpcoColrTriples(byte[] d)
{
    var res = new List<(int, int, int)>();
    var iprp = FindMetaChild(d, "iprp");
    if (iprp == null) return res;
    foreach (var c in Walk(d, iprp.Value.start + 8, iprp.Value.end))   // iprp / ipco 都不是 fullbox
    {
        if (c.type != "ipco") continue;
        foreach (var p in Walk(d, c.start + 8, c.end))
        {
            // colr nclx 的 pri/trc/mtx 是 **16 位**字段（实测标定：avifdec 报 9/16/9 的件里
            // 字节是 00 09 00 10 00 09 ⇒ 按单字节读会读成 (0,9,0)）
            if (p.type != "colr" || p.end - p.start < 19) continue;
            if (Encoding.ASCII.GetString(d, p.start + 8, 4) != "nclx") continue;
            res.Add((Be16At(d, p.start + 12), Be16At(d, p.start + 14), Be16At(d, p.start + 16)));
        }
    }
    return res;
}

static List<(uint w, uint h)> IpcoIspeSizes(byte[] d)
{
    var res = new List<(uint, uint)>();
    var iprp = FindMetaChild(d, "iprp");
    if (iprp == null) return res;
    foreach (var c in Walk(d, iprp.Value.start + 8, iprp.Value.end))
    {
        if (c.type != "ipco") continue;
        foreach (var p in Walk(d, c.start + 8, c.end))
        {
            // ispe 是 fullbox ⇒ size(4)+type(4)+ver/flags(4)+w(4)+h(4)
            if (p.type != "ispe" || p.end - p.start < 20) continue;
            res.Add((Be32At(d, p.start + 12), Be32At(d, p.start + 16)));
        }
    }
    return res;
}

// 反控：抹掉 ftyp 里的 tmap 品牌 ⇒ 判据必须变化，否则「读侧据此识别派生项」这句是摆设
static (bool, string) BrandRemovalChangesVerdict(byte[] built)
{
    var ft = FindTop(built, "ftyp");
    if (ft == null) return (false, "产物里没有 ftyp 盒");
    var needle = Encoding.ASCII.GetBytes("tmap");
    int at = -1;
    for (int i = ft.Value.start + 8; i + 4 <= ft.Value.end; i++)
        if (built.AsSpan(i, 4).SequenceEqual(needle)) { at = i; break; }
    if (at < 0) return (false, $"ftyp（{ft.Value.end - ft.Value.start} B）里找不到 tmap 品牌");
    var b = (byte[])built.Clone();
    for (int k = 0; k < 4; k++) b[at + k] = (byte)'X';
    var before = IsoBmffGainMapProbe.Probe(built);
    var after = IsoBmffGainMapProbe.Probe(b);
    bool changed = after.HasGainMap != before.HasGainMap || after.Determinate != before.Determinate ||
                   after.GainMapItemId != before.GainMapItemId || after.TmapItemId != before.TmapItemId;
    return (changed, $"抹掉 @{at} 的品牌 | 删前 has={before.HasGainMap} det={before.Determinate} " +
                     $"t={before.TmapItemId} gm={before.GainMapItemId} brands=[{before.Brands}] | " +
                     $"删后 has={after.HasGainMap} det={after.Determinate} t={after.TmapItemId} " +
                     $"gm={after.GainMapItemId} brands=[{after.Brands}]");
}

static bool StartsOnceWithTd(byte[]? item)
    => item != null && item.Length > 4 && item[0] == 0x12 && item[1] == 0x00 && item[2] != 0x12;

static string Hex4(byte[]? item)
    => item == null || item.Length < 4 ? "-" : string.Join(" ", item[..4].Select(x => $"{x:X2}"));

// 反控：grpl(altr) 盒删掉后，判据必须变化（该盒承重）；若判据不变 ⇒ 说明这条断言没牙。
static (bool, string) GrplRemovalChangesVerdict(byte[] built)
{
    var gr = FindMetaChild(built, "grpl");
    var mt = FindTop(built, "meta");
    if (gr == null) return (false, "产物里找不到 grpl 盒（writer 未写 altr ⇒ 判据无处可删）");
    if (mt == null) return (false, "产物里找不到 meta 盒");
    int size = gr.Value.end - gr.Value.start;
    var b = new List<byte>();
    b.AddRange(built[..gr.Value.start]);
    b.AddRange(built[gr.Value.end..]);
    var outB = b.ToArray();
    Sub32At(outB, mt.Value.start, (uint)size);   // meta 盒长必须同步收缩，否则是在测失步而不是在测 altr
    var before = IsoBmffGainMapProbe.Probe(built);
    var after = IsoBmffGainMapProbe.Probe(outB);
    bool changed = after.HasGainMap != before.HasGainMap || after.Determinate != before.Determinate ||
                   after.GainMapItemId != before.GainMapItemId || after.Form != before.Form;
    return (changed, $"grpl 盒 {size} B | 删前 has={before.HasGainMap} det={before.Determinate} " +
                     $"gm={before.GainMapItemId} form={before.Form ?? "-"} | 删后 has={after.HasGainMap} " +
                     $"det={after.Determinate} gm={after.GainMapItemId} form={after.Form ?? "-"}");
}


static string Describe(byte[] d, int from, int to)
    => string.Join(" ", Walk(d, from, to).Select(b => $"{b.type}@{b.start}/{b.end - b.start}"));

static byte[] Slice(byte[] d, int from, int len)
{
    int n = Math.Min(len, d.Length - from);
    if (n <= 0) return Array.Empty<byte>();
    var r = new byte[n];
    Array.Copy(d, from, r, 0, n);
    return r;
}


static void DumpColrBoxes(string tag, byte[] d)
{
    var sb = new StringBuilder($"[hex-{tag}] colr（从盒长字段起 20 B）:");
    int n = 0;
    for (int i = 4; i + 20 <= d.Length && n < 6; i++)
    {
        if (!d.AsSpan(i, 4).SequenceEqual("colr"u8)) continue;
        sb.Append($" +{i - 4}: {string.Join(" ", Slice(d, i - 4, 20).Select(x => x.ToString("X2")))} |");
        n++;
    }
    Console.WriteLine(n == 0 ? $"[hex-{tag}] 没有 colr 盒" : sb.ToString());
}

static void DumpProbe(string tag, byte[] d)
{
    var r = IsoBmffGainMapProbe.Probe(d);
    Console.WriteLine($"[dump:{tag}] LooksLike={IsoBmffGainMapProbe.LooksLikeIsoBmff(d)} IsIsoBmff={r.IsIsoBmff} " +
        $"Det={r.Determinate} HasGM={r.HasGainMap} Form={r.Form ?? "-"} primary={r.PrimaryItemId} " +
        $"tmap={r.TmapItemId} gm={r.GainMapItemId} codec={r.GainMapCodec ?? "-"} " +
        $"ff={r.GainMapFfmpegFormat ?? "-"} brands=[{r.Brands}] tmapLen={(r.TmapPayload?.Length ?? -1)} " +
        $"gmLen={(r.GainMapItemData?.Length ?? -1)} fail={r.FailReason ?? "-"}");
}
