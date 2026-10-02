using ColorMatrix;    // ColorSpaceRegistry 与其余内核类型均在此命名空间

// ══════════════════════════════════════════════════════════════════════════════
//  ColorMatrix 自检 —— 零依赖（不引 xunit / NUnit，只跑断言并回退出码）。
//
//  为什么用控制台而不是测试框架：本库刻意保持「零 NuGet 依赖」，测试框架会破坏这一点。
//  用法：dotnet run --project tests/ColorMatrix.SelfTest -c Release
//        退出码 0 = 全过；非 0 = 有失败（可直接接 CI）。
//
//  ⚠ 判据口径：**每条都带"反控"或明确量级**，不允许写成恒真式断言
//    （例如「不为 null」这种在实现被换成常量后仍会通过的写法）。
// ══════════════════════════════════════════════════════════════════════════════

int pass = 0, fail = 0;

void Check(bool ok, string msg)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {msg}");
    if (ok) pass++; else fail++;
}

double MaxAbsDiff(double[] a, double[] b)
{
    if (a.Length != b.Length) return double.PositiveInfinity;
    double m = 0;
    for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Abs(a[i] - b[i]));
    return m;
}

Console.WriteLine("=== 1. 原色矩阵：对撞 ITU 发布值 ===");

// bt2020 → bt709 的线性矩阵：期望值取自 ITU-R BT.2087 / 本库对撞 zimg 的入库常量。
// 容差 2e-3 与宿主 `matrix` 探针同口径（发布值只到 4 位小数）。
var bt2020To709 = ColorSpaceRegistry.LinearMatrixBetweenPrimaries("bt2020", "bt709");
double[] expect2020To709 =
{
    1.6605, -0.5876, -0.0728,
   -0.1246,  1.1329, -0.0083,
   -0.0182, -0.1006,  1.1187
};
Check(bt2020To709 != null, "bt2020→bt709 应返回矩阵（不得为 null）");
if (bt2020To709 != null)
{
    double d = MaxAbsDiff(bt2020To709, expect2020To709);
    Check(d <= 2e-3, $"bt2020→bt709 与发布值一致（maxErr={d:E2}，容差 2e-3）");
    // 反控：矩阵不得是单位阵（否则上面那条在实现退化成恒等时也会过）
    double identityDiff = MaxAbsDiff(bt2020To709, new[] { 1.0, 0, 0, 0, 1.0, 0, 0, 0, 1.0 });
    Check(identityDiff > 0.1, $"bt2020→bt709 不得是单位阵（与 I 的实差={identityDiff:F3}）");
}

Console.WriteLine();
Console.WriteLine("=== 2. 契约：同原色不映射 / 未收录 fail-closed ===");

Check(ColorSpaceRegistry.LinearMatrixBetweenPrimaries("bt709", "bt709") == null,
    "bt709→bt709 必须返回 null（同原色 = 不映射，绝不生成单位阵去乘一遍）");
Check(ColorSpaceRegistry.LinearMatrixBetweenPrimaries("film", "bt709") == null,
    "film→bt709 必须返回 null（有意不收录，fail-closed）");
Check(ColorSpaceRegistry.LinearMatrixBetweenPrimaries("smpte428", "bt709") == null,
    "smpte428→bt709 必须返回 null（XYZ 三角，xy 参数化无法表达）");
Check(ColorSpaceRegistry.LinearMatrixBetweenPrimaries("bogus-token", "bt709") == null,
    "未知 token 必须返回 null（不得猜一个矩阵去乘像素）");

Console.WriteLine();
Console.WriteLine("=== 3. 往返互逆：M(a→b)·M(b→a) ≈ I ===");

var fwd = ColorSpaceRegistry.LinearMatrixBetweenPrimaries("bt2020", "bt709");
var bwd = ColorSpaceRegistry.LinearMatrixBetweenPrimaries("bt709", "bt2020");
Check(fwd != null && bwd != null, "双向矩阵都应可求");
if (fwd != null && bwd != null)
{
    var prod = ColorSpaceRegistry.MultiplyMatrices(fwd, bwd);
    double d = MaxAbsDiff(prod, new[] { 1.0, 0, 0, 0, 1.0, 0, 0, 0, 1.0 });
    Check(d <= 1e-9, $"bt2020↔bt709 往返应还原单位阵（maxErr={d:E2}，容差 1e-9）");
}

Console.WriteLine();
Console.WriteLine("=== 4. 白点守恒：原色矩阵行和 ≈ 1 ===");

// 行主序 × 列向量 ⇒ 「白」= 每行元素之和应 ≈ 1（同白点、已色适应）
if (fwd != null)
{
    double r0 = fwd[0] + fwd[1] + fwd[2];
    double r1 = fwd[3] + fwd[4] + fwd[5];
    double r2 = fwd[6] + fwd[7] + fwd[8];
    double worst = Math.Max(Math.Abs(r0 - 1), Math.Max(Math.Abs(r1 - 1), Math.Abs(r2 - 1)));
    Check(worst <= 1e-6, $"bt2020→bt709 三行行和应≈1（最大偏离={worst:E2}）");
}

Console.WriteLine();
Console.WriteLine("=== 5. 传递函数：CICP 收录面（fail-closed） ===");

Check(TransferCurve.ForCicpTransfer("16") != null, "CICP 16 (PQ) 应被收录");
Check(TransferCurve.ForCicpTransfer("18") != null, "CICP 18 (HLG) 应被收录");
Check(TransferCurve.ForCicpTransfer("1") != null, "CICP 1 (BT.709) 应被收录");
Check(TransferCurve.ForCicpTransfer("9") == null, "CICP 9 (log100) 应 fail-closed 返回 null");
Check(TransferCurve.ForCicpTransfer("10") == null, "CICP 10 (log316) 应 fail-closed 返回 null");
Check(TransferCurve.ForCicpTransfer("9999") == null, "未知 CICP transfer 应返回 null");

Console.WriteLine();
Console.WriteLine("=== 6. HDR 算子：闭式常数可核 ===");

// BT.2446 Method A 的亮度系数之和应为 1（0.2627 + 0.6780 + 0.0593）
double lumaWhite = Bt2446MethodA.LumaPrime(1.0, 1.0, 1.0);
Check(Math.Abs(lumaWhite - 1.0) <= 1e-9, $"BT.2446A LumaPrime(1,1,1) 应=1（实 {lumaWhite:F12}）");
// 反控：中性灰（0.5）不得等于 1 ⇒ 证明它真的在算加权和而非返回常量
Check(Math.Abs(Bt2446MethodA.LumaPrime(0.5, 0.5, 0.5) - 0.5) <= 1e-9,
    "BT.2446A LumaPrime(0.5,0.5,0.5) 应=0.5（反控：非恒返回 1）");

// BT.2100 系统 gamma：随峰值亮度单调递增（1000nits 应 > 1）
double g1000 = Bt2100Ootf.SystemGamma(1000);
double g100 = Bt2100Ootf.SystemGamma(100);
Check(g1000 > 1.0, $"BT.2100 SystemGamma(1000nits) 应 > 1（实 {g1000:F4}）");
Check(g1000 > g100, $"SystemGamma 应随峰值亮度递增（1000→{g1000:F4} > 100→{g100:F4}）");

Console.WriteLine();
Console.WriteLine("=== 7. 逐像素内核：矩阵施加与恒等 ===");

var kern = new float[] { 0.2f, 0.4f, 0.6f, 1.0f };
ColorKernels.ApplyMatrix(kern, fwd);
Check(Math.Abs(kern[3] - 1.0f) <= 1e-6, "ApplyMatrix 不得改动 alpha 通道");
Check(Math.Abs(kern[0] - 0.2f) > 1e-4 || Math.Abs(kern[1] - 0.4f) > 1e-4,
    $"ApplyMatrix 应真的改变了颜色通道（实 r={kern[0]:F4} g={kern[1]:F4} b={kern[2]:F4}）");

var kernId = new float[] { 0.2f, 0.4f, 0.6f, 1.0f };
ColorKernels.ApplyMatrix(kernId, null);
Check(Math.Abs(kernId[0] - 0.2f) <= 1e-6 && Math.Abs(kernId[1] - 0.4f) <= 1e-6,
    "ApplyMatrix(null) 应是无操作（负控：不得误改像素）");

Console.WriteLine();
Console.WriteLine("=== 8. 原色表逐 token 对撞 zimg 参照（外部实现，非自述） ===");

// 参照值 = 本库在宿主项目里对撞 **zimg**（ffmpeg 的 zscale）实测得到的入库常量（5 位小数）。
// 容差 5e-4：参照本身只到 5 位小数（舍入地板 ~5e-6），留出 100× 余量以覆盖白点位数差异。
// ⚠ 这组的意义：**逐个 token 锁住原色表**。历史上该表出过「5 个不同标准被压成同一组常数」
//   的 P1 缺陷（bt470m/bt470bg/smpte170m/smpte240m 全拿 P22 算，逐元素实差最大 0.461）。
var primariesTable = new (string Token, double[] Expected)[]
{
    ("bt470m",    new[] { 1.4862, -0.4036, -0.0826, -0.0251, 0.9540, 0.0711, -0.0272, -0.0441, 1.0713 }),
    ("bt470bg",   new[] { 1.0440, -0.0440,  0.0000,  0.0000, 1.0000, 0.0000,  0.0000,  0.0118, 0.9882 }),
    ("smpte170m", new[] { 0.9395,  0.0502,  0.0103,  0.0178, 0.9658, 0.0164, -0.0016, -0.0044, 1.0060 }),
    ("smpte240m", new[] { 0.9395,  0.0502,  0.0103,  0.0178, 0.9658, 0.0164, -0.0016, -0.0044, 1.0060 }),
    ("bt2020",    new[] { 1.6605, -0.5876, -0.0729, -0.1246, 1.1329, -0.0084, -0.0182, -0.1006, 1.1187 }),
    ("smpte431",  new[] { 1.1575, -0.1550, -0.0026, -0.0415, 1.0456, -0.0041, -0.0181, -0.0786, 1.0966 }),
    ("smpte432",  new[] { 1.2249, -0.2249,  0.0000, -0.0421, 1.0421,  0.0000, -0.0196, -0.0786, 1.0983 }),
    ("jedec-p22", new[] { 1.0253, -0.0266,  0.0013,  0.0194, 0.9480, 0.0326, -0.0018, -0.0014, 1.0032 }),
};

int primOk = 0;
foreach (var (token, expected) in primariesTable)
{
    var got = ColorSpaceRegistry.LinearMatrixBetweenPrimaries(token, "bt709");
    if (got == null)
    {
        Check(false, $"{token}→bt709 应返回矩阵（实为 null）");
        continue;
    }
    double d = MaxAbsDiff(got, expected);
    Check(d <= 5e-4, $"{token}→bt709 对撞 zimg（maxErr={d:E2}，容差 5e-4）");
    if (d <= 5e-4) primOk++;
}

// 反控：8 个 token 必须算出 **至少 7 组不同矩阵**。
// 若原色表被退化成"所有 token 共用一组常数"（历史上的 P1 缺陷），本组会立刻塌成 1~2 组。
var distinct = new HashSet<string>();
foreach (var (token, _) in primariesTable)
{
    var m = ColorSpaceRegistry.LinearMatrixBetweenPrimaries(token, "bt709");
    if (m != null) distinct.Add(string.Join(",", m.Select(v => Math.Round(v, 6))));
}
Check(distinct.Count >= 7,
    $"8 个 token 应算出 ≥7 组不同矩阵（实 {distinct.Count} 组）—— 反控：防原色表退化成共用常数");

Console.WriteLine();
Console.WriteLine("=== 9. 传递函数：锚点 / 互逆 / 单调（ToLinear 归一化到**该曲线自身峰值**） ===");

// ⚠ 用 `is { } x` 模式做收窄（`if (x != null)` 在本文件里对可空类未生效，实测 CS1061）
// ⚠⚠ **归一化约定（实测澄清，勿再假设）**：`ToLinear` 归一化到**该曲线自身的峰值**，
//     不是归一化到 SDR 白点 203nits ——
//       · PQ：1.0 = PqPeakNits = 10000 nits ⇒ ToLinear(0.580694) = 203/10000 = 0.0203
//       · HLG：1.0 = 该曲线峰值 ⇒ ToLinear(1.0) = 1.0
//     ⇒ 断言必须写成「乘回峰值换算成 nits」，这才是与约定无关的锚点。
//     （本组第一版我按"归一化到 203nits"写，实测 3 条转红 —— **是我的假设错，不是库错**。）
if (TransferCurve.ForCicpTransfer("16") is { } pq)
{
    // PQ 锚点：码值 0.580694 对应 **203 nits**（= SDR 参考白）。容差 0.6 nits（宿主探针用 0.5）。
    double nits = pq.ToLinear(0.580694) * TransferCurve.PqPeakNits;
    Check(Math.Abs(nits - TransferCurve.SdrWhiteNits) <= 0.6,
        $"PQ 锚点：ToLinear(0.580694)×10000nits 应≈{TransferCurve.SdrWhiteNits} nits（实 {nits:F3}，容差 0.6）");
    // 反控：不能恒返回常量 ⇒ 换一个码值必须给出不同结果
    double nits2 = pq.ToLinear(0.8) * TransferCurve.PqPeakNits;
    Check(Math.Abs(nits2 - TransferCurve.SdrWhiteNits) > 100,
        $"PQ ToLinear(0.8) 对应亮度应远高于 SDR 白点（反控，实 {nits2:F1} nits）");
    // 互逆：FromLinear(ToLinear(x)) ≈ x
    double x = 0.4, rt = pq.FromLinear(pq.ToLinear(x));
    Check(Math.Abs(rt - x) <= 1e-9, $"PQ 往返互逆 FromLinear(ToLinear({x})) 应≈{x}（实 {rt:F12}）");
    // 边界：黑电平不被抬高；峰值码值应给出归一化 1.0
    Check(Math.Abs(pq.ToLinear(0.0)) <= 1e-12, $"PQ ToLinear(0) 应=0（实 {pq.ToLinear(0.0):E2}）");
    Check(Math.Abs(pq.ToLinear(1.0) - 1.0) <= 1e-9,
        $"PQ ToLinear(1) 应=1.0（= 10000nits 峰值，实 {pq.ToLinear(1.0):F6}）");
}

// 单调性：任何合法传递函数在 [0,1] 上必须单调不减（否则色调会反转）
foreach (var (name, c) in new (string, TransferCurve?)[]
         { ("PQ", TransferCurve.ForCicpTransfer("16")),
           ("HLG", TransferCurve.ForCicpTransfer("18")),
           ("BT.709", TransferCurve.ForCicpTransfer("1")) })
{
    if (c is not { } curve) continue;
    bool mono = true;
    double prev = curve.ToLinear(0.0);
    for (int i = 1; i <= 100; i++)
    {
        double cur = curve.ToLinear(i / 100.0);
        if (cur < prev) { mono = false; break; }
        prev = cur;
    }
    Check(mono, $"{name} 在 [0,1] 上应单调不减（100 点扫描）");
}

// HLG：归一化到自身峰值 ⇒ ToLinear(1.0) = 1.0；且往返互逆
if (TransferCurve.ForCicpTransfer("18") is { } hlg)
{
    double peak = hlg.ToLinear(1.0);
    // 容差 1e-6：HLG 的 EOTF 走 pow 闭式，末位浮点误差大于 PQ 的分段式（实测 ~1e-9 量级）
    Check(Math.Abs(peak - 1.0) <= 1e-6,
        $"HLG ToLinear(1.0) 应=1.0（归一化到自身峰值，实 {peak:F12}，容差 1e-6）");
    Check(hlg.ToLinear(0.5) < peak, $"HLG 中点应低于峰值（反控：非恒返回 1.0，实 {hlg.ToLinear(0.5):F6}）");
    double rt2 = hlg.FromLinear(hlg.ToLinear(0.6));
    Check(Math.Abs(rt2 - 0.6) <= 1e-9, $"HLG 往返互逆应≈0.6（实 {rt2:F12}）");
}

Console.WriteLine();
Console.WriteLine($"===== ColorMatrix SelfTest: {pass} PASS / {fail} FAIL =====");
return fail == 0 ? 0 : 1;
