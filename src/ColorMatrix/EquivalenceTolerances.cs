namespace ColorMatrix;

/// <summary>
/// **等价判定阈值集中处** —— 本库内所有「两个色彩描述是否等价」的判据都应引用这里的常量，
/// 不允许在调用点散落魔数（散落的魔数会各自漂移，本仓历史上因此修过多次）。
///
/// <para>
/// ⚠ 本文件是从宿主项目 `ffmpegGui` 的 `Services/ColorMapping/ColorIntent.cs` 中**抽取**的最小依赖：
/// <see cref="ColorSpaceDescriptor"/> 需要该类，而 `ColorIntent.cs` 的其余内容属于宿主的规划层
/// （依赖 `FfmpegOptions` 等），不在本库范围内。
/// </para>
/// </summary>
public static class EquivalenceTolerances
{
    /// <summary>|MatrixTo(dst) − I|∞ ≤ 此值 ⇒ 判为同原色（跳矩阵）。</summary>
    public const double MatrixToIdentity = 1e-5;
    /// <summary>逐元素比较两个 RGB→XYZ(D50) 的容差（等价判定）。</summary>
    public const double MatrixElements = 1e-5;
    /// <summary>曲线参数逐项容差（ICC s15Fixed16 量化决定了不能更严）。</summary>
    public const double CurveParams = 1e-4;
    /// <summary>原色色度 xy 逐项容差 ⇒ 判为同原色。</summary>
    public const double Chromaticity = 1e-4;
    /// <summary>几何包含判据的边界松弛。</summary>
    public const double ContainmentEpsilon = 1e-6;
    /// <summary>映射前置条件：源置信度下限（手动模式例外，由调用方担保）。</summary>
    public const DescriptorConfidence MinConfidenceForMapping = DescriptorConfidence.CicpTag;
}
