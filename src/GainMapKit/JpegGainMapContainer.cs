// ⚠ 本文件抽自 ffmpegPictureUI（宿主为权威副本），锚点见仓库 README §7。
//    宿主内的对应位置：https://github.com/luoye-cpu/ffmpegPictureUI/blob/main/src/FfmpegGui/Services/GainMapDecoder.cs 第 503-816 行。
//    本副本的既定差异：命名空间 → GainMapKit；JpegContainer 提升为顶层 JpegGainMapContainer；
//    GainMapDecoder.cs:462 的 IsoNamespace 随件带入。
namespace GainMapKit;

/// <summary>
/// Ultra HDR JPEG 容器（纯托管、零依赖）：顶层拼接的多张 JPEG + ISO 21496-1 元数据 + MPF 条目。
/// 结构对齐 GainMapEncoder.WriteJpegGainMapFile 与 Google libultrahdr 输出：
///   [主图: SOI … APP1(XMP) APP2(MPF) … SOS 扫描 EOI][副图: SOI APP2(ISO 21496-1) … EOI]
/// </summary>
public sealed class JpegGainMapContainer
{
    /// <summary>ISO 21496-1 增益图元数据的 APP2 命名空间前缀（含结尾 NUL）。</summary>
    internal const string IsoNamespace = "urn:iso:std:iso:ts:21496:-1";
    /// <summary>顶层 JPEG 图像边界（含 SOI..EOI）与 SOF 尺寸。</summary>
    public readonly struct Img
    {
        public readonly int Start;
        public readonly int Length;
        public readonly int W;
        public readonly int H;
        public Img(int start, int length, int w, int h)
        { Start = start; Length = length; W = w; H = h; }
    }

    public readonly List<Img> Images = new();
    public GainMapMetadata? IsoMetadata;
    public int IsoImageIndex = -1;
    /// <summary>MPF IndividualImageArray 条目（attribute / size / offset）。</summary>
    public readonly List<(uint Attr, uint Size, uint Offset)> MpfEntries = new();
    /// <summary>MPF TIFF 头（'MM'/'II'）绝对位置 = MPF offset 计数基准（CIPA DC-X007）。</summary>
    public int MpfBaseOffset = -1;
    public bool MpfLittleEndian;

    private byte[] _data = Array.Empty<byte>();

    /// <summary>取某张图像的原始字节。</summary>
    public byte[] Slice(Img img)
    {
        var b = new byte[img.Length];
        Array.Copy(_data, img.Start, b, 0, img.Length);
        return b;
    }

    /// <summary>主图（底图 base rendition）= 顶层第一张 JPEG（携带 XMP/MPF 与底图 ICC）。</summary>
    public Img? GetPrimaryImage() => Images.Count > 0 ? Images[0] : null;

    /// <summary>副图（增益图）定位：ISO 所在图像 → MPF 非零偏移条目 → 顶层第二张。</summary>
    public Img? GetSecondaryImage()
    {
        if (Images.Count == 0) return null;
        if (IsoImageIndex >= 0 && IsoImageIndex < Images.Count) return Images[IsoImageIndex];
        if (MpfEntries.Count >= 2 && MpfBaseOffset >= 0)
        {
            for (int i = 1; i < MpfEntries.Count; i++)
            {
                int abs = MpfBaseOffset + (int)MpfEntries[i].Offset;
                if (abs > 0 && abs + 1 < _data.Length && _data[abs] == 0xFF && _data[abs + 1] == 0xD8)
                {
                    foreach (var img in Images)
                        if (img.Start == abs) return img;
                }
            }
        }
        return Images.Count >= 2 ? Images[1] : null;
    }

    /// <summary>
    /// 是否为增益图 (Ultra HDR) 容器：ISO 21496-1 元数据为权威判据；
    /// 无 ISO 时需同时满足 MPF ≥ 2 幅 + 副图为与主图同比例的严格降采样
    /// （避免将 MPO 3D/全景等多幅图 JPEG 误判）。
    /// </summary>
    public bool IsGainMapContainer()
    {
        if (IsoMetadata != null) return true;
        if (Images.Count < 2 || MpfEntries.Count < 2) return false;
        var pri = Images[0]; var sec = Images[1];
        if (pri.W <= 0 || sec.W <= 0 || sec.H <= 0) return false;
        if (sec.W >= pri.W || sec.H >= pri.H) return false;          // 增益图必为降采样
        double ar1 = (double)pri.W / pri.H, ar2 = (double)sec.W / sec.H;
        return ar1 > 0 && Math.Abs(ar1 - ar2) / ar1 < 0.05;
    }

    /// <summary>解析整个文件；无法定位任何 SOI 时返回 null。</summary>
    public static JpegGainMapContainer? Parse(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return null;
        var c = new JpegGainMapContainer { _data = data };
        int i = 0;
        while (i + 4 <= data.Length)
        {
            if (data[i] != 0xFF || data[i + 1] != 0xD8) { i++; continue; }

            int imgStart = i;
            int p = i + 2;
            int isoDataStart = -1, isoDataLen = 0;
            int mpfStart = -1, mpfLen = 0;
            int imgW = 0, imgH = 0;
            bool terminated = false;

            // 仅需 p+1 即可读标记字节（EOI 常落在文件末尾前 2 字节，不能要求 p+3）
            while (p + 1 < data.Length)
            {
                if (data[p] != 0xFF) { p++; continue; }
                byte m = data[p + 1];
                if (m == 0xFF || m == 0x00) { p++; continue; }         // 填充字节 / 扫描内转义 00
                if (m == 0xD9) { terminated = true; break; }           // EOI → 图像结束
                if (m == 0x01 || (m >= 0xD0 && m <= 0xD8)) { p += 2; continue; }  // TEM / RSTn-SOI 等无长度标记
                if (m == 0xDA) { p = SkipScanData(data, p + 2); continue; }       // SOS → 跳过熵编码段
                if (m < 0xC0) { p += 2; continue; }                    // 其余无长度前缀标记 → 保守前进
                if (p + 3 >= data.Length) break;                       // 段头不完整（截断）

                int segLen = (data[p + 2] << 8) | data[p + 3];
                if (segLen < 2) { p += 2; continue; }
                int payload = p + 4, payloadLen = segLen - 2;
                if (payload + payloadLen > data.Length) break;         // 截断

                // SOF0..SOF15 (排除 C4=DHT / C8=reserved / CC=DAC) → 图像尺寸
                if (m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC
                    && payloadLen >= 5 && imgW == 0)
                {
                    imgH = (data[payload + 1] << 8) | data[payload + 2];
                    imgW = (data[payload + 3] << 8) | data[payload + 4];
                }
                if (m == 0xE2 && payloadLen > IsoNamespace.Length + 1)
                {
                    // ISO 21496-1: "urn:iso:...:21496:-1\0" + 二进制元数据
                    if (data[payload] == (byte)'u' && MatchAscii(data, payload, IsoNamespace)
                        && data[payload + IsoNamespace.Length] == 0)
                    {
                        isoDataStart = payload + IsoNamespace.Length + 1;
                        isoDataLen = payload + payloadLen - isoDataStart;
                    }
                    // MPF: "MPF\0" + TIFF 头
                    else if (data[payload] == (byte)'M' && data[payload + 1] == (byte)'P'
                             && data[payload + 2] == (byte)'F' && data[payload + 3] == 0)
                    {
                        mpfStart = payload + 4; mpfLen = payload + payloadLen - mpfStart;
                    }
                }
                p = payload + payloadLen;
            }

            if (!terminated) break;   // 末张图像未遇 EOI（流截断）
            c.Images.Add(new Img(imgStart, p + 2 - imgStart, imgW, imgH));   // 含 EOI
            int idx = c.Images.Count - 1;

            if (isoDataStart >= 0 && isoDataLen >= 5)
            {
                var meta = ParseIso21496(data, isoDataStart, isoDataLen);
                if (meta != null) { c.IsoMetadata = meta; c.IsoImageIndex = idx; }
            }
            if (mpfStart >= 0 && mpfLen >= 16 && c.MpfEntries.Count == 0)
                c.ParseMpf(data, mpfStart, mpfLen);

            i = p + 2;   // 从 EOI 之后继续（下一张 SOI 或文件尾）
        }
        return c.Images.Count > 0 ? c : null;
    }

    /// <summary>跳过熵编码扫描数据：首个 FF xx（xx≠00 且 xx∉D0..D7）即下一标记。</summary>
    private static int SkipScanData(byte[] d, int from)
    {
        int p = from;
        while (p + 1 < d.Length)
        {
            if (d[p] == 0xFF && d[p + 1] != 0x00 && !(d[p + 1] >= 0xD0 && d[p + 1] <= 0xD7))
                return p;
            p++;
        }
        return d.Length;
    }

    private static bool MatchAscii(byte[] d, int offset, string s)
    {
        if (offset + s.Length > d.Length) return false;
        for (int k = 0; k < s.Length; k++)
            if (d[offset + k] != (byte)s[k]) return false;
        return true;
    }

    // ── MPF (APP2 'MPF\0' → TIFF IFD → tag 0xB002 IndividualImageArray) ──
    private void ParseMpf(byte[] d, int tiffStart, int tiffLen)
    {
        int End = tiffStart + tiffLen;
        if (tiffStart + 8 > d.Length) return;
        bool le = d[tiffStart] == (byte)'I' && d[tiffStart + 1] == (byte)'I';
        if (!le && !(d[tiffStart] == (byte)'M' && d[tiffStart + 1] == (byte)'M')) return;
        MpfLittleEndian = le;
        int ifd = tiffStart + (int)Be32(d, tiffStart + 4, le);
        if (ifd + 2 > End) return;
        int count = Be16(d, ifd, le);
        int entry = ifd + 2;
        int mpEntryVal = -1; uint mpEntryCount = 0;
        for (int n = 0; n < count && entry + 12 <= End; n++, entry += 12)
        {
            int tag = Be16(d, entry, le);
            int type = Be16(d, entry + 2, le);
            uint cnt = Be32(d, entry + 4, le);
            if (tag == 0xB002)   // MPEntry: UNDEF, 16 字节/条目
            {
                int size = type == 4 ? 4 : type is 3 or 2 ? 2 : 1;   // LONG/SHORT/BYTE-UNDEF
                mpEntryCount = cnt * (uint)size;
                mpEntryVal = mpEntryCount <= 4 ? entry + 8 : tiffStart + (int)Be32(d, entry + 8, le);
            }
        }
        if (mpEntryVal < 0 || mpEntryCount < 32 || mpEntryVal + mpEntryCount > End) return;
        // 图像条目: attribute(4) size(4) offset(4) reserved(4)，均相对 TIFF 头
        for (int o = mpEntryVal; o + 16 <= mpEntryVal + mpEntryCount; o += 16)
        {
            uint attr = Be32(d, o, le);
            uint size = Be32(d, o + 4, le);
            uint offs = Be32(d, o + 8, le);
            if (attr == 0 && size == 0 && offs == 0) break;   // 越界/零页保护
            MpfEntries.Add((attr, size, offs));
            if (offs == 0) MpfBaseOffset = tiffStart;         // 主图条目 → 确立基准
        }
        if (MpfBaseOffset < 0 && MpfEntries.Count > 0) MpfBaseOffset = tiffStart;
    }

    private static int Be16(byte[] d, int o, bool le)
        => le ? d[o] | (d[o + 1] << 8) : (d[o] << 8) | d[o + 1];
    private static uint Be32(byte[] d, int o, bool le)
        => le
            ? (uint)d[o] | ((uint)d[o + 1] << 8) | ((uint)d[o + 2] << 16) | ((uint)d[o + 3] << 24)
            : ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

    /// <summary>
    /// 解析 ISO 21496-1 二进制增益图元数据（对齐 libultrahdr decodeGainmapMetadata）。
    ///   [min_version u16][writer_version u16][flags u8]
    ///   flags: bit7 多通道 / bit6 useBaseColorSpace / bit2 backwardDirection(HDR 意图作基图) / bit3 公共分母
    ///   公共分母模式: [denom][baseHeadroomN][altHeadroomN] + 每通道 (min,max,gamma,baseOffset,altOffset 分子)
    ///   独立分母模式: [baseN baseD altN altD] + 每通道 5 对 (N,D)
    /// </summary>
    public static GainMapMetadata? ParseIso21496(byte[] d, int off, int len)
    {
        try
        {
            int end = off + len, p = off;
            if (p + 5 > end) return null;
            int minVersion = Be16(d, p, false); p += 2;
            if (minVersion != 0) return null;              // 仅 v0 定义
            p += 2;                                        // writer_version
            byte flags = d[p++];
            int channels = (flags & 0x80) != 0 ? 3 : 1;
            bool commonDenom = (flags & 0x08) != 0;

            var meta = new GainMapMetadata
            {
                Channels = channels,
                // ISO 无 BaseRenditionIsHDR 标志：HDR 意图作基图 → 向后方向，或增益全 ≤ 0
                BaseRenditionIsHdr = (flags & 0x04) != 0,
                UseBaseColorSpace = (flags & 0x40) != 0,   // bit6 = useBaseColorSpace
            };

            float rd(int n, int dd) => dd == 0 ? 0f : (float)((double)n / dd);

            if (commonDenom)
            {
                if (p + 12 + channels * 20 > end) return null;
                int dn = (int)Be32(d, p, false); p += 4;                       // 公共分母
                int bhn = (int)Be32(d, p, false); p += 4;                       // baseHdrHeadroomN
                int ahn = (int)Be32(d, p, false); p += 4;                       // alternateHdrHeadroomN
                meta.HdrCapacityMin = rd(bhn, dn);
                meta.HdrCapacityMax = rd(ahn, dn);
                for (int c = 0; c < channels; c++)
                {
                    int mn = (int)Be32(d, p, false); p += 4;                    // gainMapMinN
                    int xn = (int)Be32(d, p, false); p += 4;                    // gainMapMaxN
                    int gn = (int)Be32(d, p, false); p += 4;                    // gainMapGammaN
                    int sn = (int)Be32(d, p, false); p += 4;                    // baseOffsetN
                    int tn = (int)Be32(d, p, false); p += 4;                    // alternateOffsetN
                    if (c == 0)
                    {
                        meta.GainMapMin = rd(mn, dn);
                        meta.GainMapMax = rd(xn, dn);
                        meta.Gamma = rd(gn, dn) <= 0f ? 1f : rd(gn, dn);
                        meta.OffsetSdr = rd(sn, dn);
                        meta.OffsetHdr = rd(tn, dn);
                    }
                }
            }
            else
            {
                if (p + 16 + channels * 40 > end) return null;
                int bhn = (int)Be32(d, p, false), bhd = (int)Be32(d, p + 4, false); p += 8;
                int ahn = (int)Be32(d, p, false), ahd = (int)Be32(d, p + 4, false); p += 8;
                meta.HdrCapacityMax = rd(ahn, ahd);
                meta.HdrCapacityMin = rd(bhn, bhd);
                for (int c = 0; c < channels; c++)
                {
                    int mn = (int)Be32(d, p, false); int md = (int)Be32(d, p + 4, false); p += 8;
                    int xn = (int)Be32(d, p, false); int xd = (int)Be32(d, p + 4, false); p += 8;
                    int gn = (int)Be32(d, p, false); int gd = (int)Be32(d, p + 4, false); p += 8;
                    int sn = (int)Be32(d, p, false); int sd = (int)Be32(d, p + 4, false); p += 8;
                    int tn = (int)Be32(d, p, false); int td = (int)Be32(d, p + 4, false); p += 8;
                    if (c == 0)
                    {
                        meta.GainMapMin = rd(mn, md);
                        meta.GainMapMax = rd(xn, xd);
                        meta.Gamma = rd(gn, gd) == 0 ? 1f : rd(gn, gd);
                        meta.OffsetSdr = rd(sn, sd);
                        meta.OffsetHdr = rd(tn, td);
                    }
                }
            }

            // HDR 基图推断：负 log2 增益上限 → 基图已是 HDR（由基图向 SDR 下映射）
            if (meta.GainMapMax <= 0f && meta.GainMapMin < 0f) meta.BaseRenditionIsHdr = true;
            if (meta.Gamma <= 0f) meta.Gamma = 1f;
            return meta;
        }
        catch { return null; }
    }

    // <summary>
    // 容量存储语义: ISO 21496-1 的 baseHdrHeadroom/alternateHdrHeadroom 分数值本身即
    // log2(headroom)（libultrahdr 转 float 时取 2^(N/D) 得线性亮度比）；hdrgm XMP 的
    // HDRCapacityMin/Max 亦以 log2 存储 → GainMapMetadata.HdrCapacity* 统一存对数值（=分数值）。
    // </summary>
}