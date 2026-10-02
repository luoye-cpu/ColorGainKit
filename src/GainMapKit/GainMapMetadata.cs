// ⚠ 本文件抽自 ffmpegPictureUI（宿主为权威副本），锚点见仓库 README §7。
//    宿主内的对应位置：https://github.com/luoye-cpu/ffmpegPictureUI/blob/main/src/FfmpegGui/Services/GainMapDecoder.cs 第 41-69 行（含第 41 行的文档行）。
//    本副本的既定差异：命名空间 → GainMapKit；JpegContainer 提升为顶层 JpegGainMapContainer；
//    GainMapDecoder.cs:462 的 IsoNamespace 随件带入。
namespace GainMapKit;

/// <summary>解析后的 Gain Map 元数据</summary>
public sealed class GainMapMetadata
{
    public float GainMapMin;
    public float GainMapMax;
    public float Gamma = 1.0f;
    public float OffsetSdr;
    public float OffsetHdr;
    public int Channels = 1;          // 1=灰度, 3=RGB
    public float HdrCapacityMax = 0;  // log2 容量上限 (ISO alternateHdrHeadroom)
    public float HdrCapacityMin = 0;  // log2 容量下限 (ISO baseHdrHeadroom; SDR 基图恒 0 = 1×)
    public bool BaseRenditionIsHdr;   // true=基础图是 HDR (通常 false)
    /// <summary>
    /// useBaseColorSpace（ISO flags bit6 / XMP hdrgm:UseBaseRenderingColorSpace）：
    /// true = 增益图定义在**底图色域**内（标准写法，本项目产出恒 true）；
    /// false = 定义在 alternate(HDR) 色域，严格应用前需先进色域转换（此时本解码器按底图色域近似）。
    /// </summary>
    public bool UseBaseColorSpace = true;
    public int GainMapWidth;
    public int GainMapHeight;
    // ── hdrgm XMP 声明的「逻辑尺寸」(AndroidX JpegImageCodec 读法; 0=元数据未声明) ──
    // JPEG 解码尺寸受 8/16 像素对齐影响（可能大于逻辑尺寸），若按解码尺寸归一化增益图坐标
    // 会引入半个宏块的偏移（边缘增益错位），故声明尺寸存在时优先用它。
    public int MapPrimaryWidth;
    public int MapPrimaryHeight;
    public int MapSecondaryWidth;
    public int MapSecondaryHeight;
    public float MapImageRatio = 1f;
}