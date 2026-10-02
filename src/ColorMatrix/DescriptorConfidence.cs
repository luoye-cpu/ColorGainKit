namespace ColorMatrix;

/// <summary>
/// 色彩描述符的**置信度**（来源权威性分级）。
///
/// <para>
/// ⚠ 本文件是从宿主项目 `ffmpegGui` 的 `Services/ColorMapping/ColorIntent.cs` 中**抽取**的
/// 最小依赖：<see cref="ColorSpaceDescriptor"/> 需要该枚举，而 `ColorIntent.cs` 的其余内容
/// 依赖宿主的规划层（`FfmpegOptions` 等），不属于本库的独立内核范围。
/// </para>
/// </summary>
public enum DescriptorConfidence
{
    /// <summary>完全无描述（既无 ICC 也无 CICP）。</summary>
    None = 0,
    /// <summary>按容器/格式惯例**假定**（如未标注 PNG 假定 sRGB）。</summary>
    CodecDefault = 1,
    /// <summary>来自容器 CICP 标签（primaries/transfer）。</summary>
    CicpTag = 2,
    /// <summary>来自具名空间（Registry 已知空间）。</summary>
    NamedIcc = 3,
    /// <summary>来自 ICC 度量值（rXYZ/gXYZ/bXYZ + TRC 实测解算）—— 最权威。</summary>
    IccMetric = 4,
}
