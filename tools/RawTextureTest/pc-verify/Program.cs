using System;
using System.IO;

/// <summary>
/// PC 侧验证：直接调用 RawTextureTest.dll 里的 RawTexture.TryDecodeImage（纯托管，不碰 IL2CPP），
/// 与 Pillow 生成的参考 RGBA 逐字节比对。用法: PcVerify <testdata 目录>
/// </summary>
internal static class Program
{
    /// <summary>mod 里内嵌的同一张 4x4 PNG（base64 必须与 RawTextureTestMod.cs 保持一致）。</summary>
    const string EmbeddedPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAM0lEQVR4nDWIsQkAIBDE8mBlbf04iqM5mptFFOVIAofgHapY4jg4N/E6kQEupUdtaWby2YK5FeHNPP51AAAAAElFTkSuQmCC";

    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "testdata";
        int total = 0, failed = 0;

        Console.WriteLine("== RawTexture.TryDecodeImage PC 验证 ==");
        Console.WriteLine("testdata: " + Path.GetFullPath(dir));
        Console.WriteLine();

        // ---- 1) 内嵌 PNG（与真机测试 mod 用的是同一串 base64）----
        {
            var png = Convert.FromBase64String(EmbeddedPngBase64);
            total++;
            if (!RawTexture.TryDecodeImage(png, out int w, out int h, out byte[] rgba))
            {
                Console.WriteLine("[FAIL] 内嵌 4x4 PNG 解码失败: " + RawTexture.LastError);
                failed++;
            }
            else
            {
                string hex = BitConverter.ToString(rgba).Replace("-", "");
                Console.WriteLine($"[ OK ] 内嵌 PNG -> {w}x{h}, {rgba.Length} 字节 RGBA, 首像素=({rgba[0]},{rgba[1]},{rgba[2]},{rgba[3]})");
                Console.WriteLine("       MD5=" + Md5(rgba) + "  HEX=" + hex);
                if (w != 4 || h != 4) { Console.WriteLine("       [FAIL] 期望 4x4"); failed++; }
            }
        }

        // ---- 2) testdata 下每个 .png 与同名 .raw（Pillow 的 RGBA 参考）比对 ----
        if (Directory.Exists(dir))
        {
            foreach (var png in Directory.GetFiles(dir, "*.png"))
            {
                string name = Path.GetFileName(png);
                string rawPath = Path.ChangeExtension(png, ".raw");
                string metaPath = Path.ChangeExtension(png, ".meta");
                if (!File.Exists(rawPath) || !File.Exists(metaPath))
                {
                    Console.WriteLine($"[SKIP] {name}（缺少 .raw/.meta 参考）");
                    continue;
                }

                total++;
                var meta = File.ReadAllText(metaPath).Trim().Split(' ');
                int ew = int.Parse(meta[0]), eh = int.Parse(meta[1]);
                var expect = File.ReadAllBytes(rawPath);
                var data = File.ReadAllBytes(png);
                string ihdr = DescribeIhdr(data);

                if (!RawTexture.TryDecodeImage(data, out int w, out int h, out byte[] rgba))
                {
                    Console.WriteLine($"[FAIL] {name} [{ihdr}] 解码失败: {RawTexture.LastError}");
                    failed++;
                    continue;
                }

                if (w != ew || h != eh)
                {
                    Console.WriteLine($"[FAIL] {name} [{ihdr}] 尺寸 {w}x{h} != 参考 {ew}x{eh}");
                    failed++;
                    continue;
                }
                if (rgba.Length != expect.Length)
                {
                    Console.WriteLine($"[FAIL] {name} [{ihdr}] 字节数 {rgba.Length} != 参考 {expect.Length}");
                    failed++;
                    continue;
                }

                int firstDiff = -1;
                for (int i = 0; i < rgba.Length; i++)
                    if (rgba[i] != expect[i]) { firstDiff = i; break; }

                if (firstDiff < 0)
                    Console.WriteLine($"[ OK ] {name,-14} [{ihdr}] {w}x{h} 与 Pillow 逐字节一致 (MD5={Md5(rgba)})");
                else
                {
                    Console.WriteLine($"[FAIL] {name,-14} [{ihdr}] 首个差异 @byte {firstDiff}: 我们={rgba[firstDiff]} 参考={expect[firstDiff]}");
                    failed++;
                }
            }
        }
        else Console.WriteLine("[WARN] 目录不存在，仅验证内嵌 PNG");

        Console.WriteLine();
        Console.WriteLine($"== 结果: {total - failed}/{total} 通过 ==");
        return failed == 0 ? 0 : 1;
    }

    static string Md5(byte[] b)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        return BitConverter.ToString(md5.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>自己按 PNG 规范读 IHDR，打印实际写入的 位深/颜色类型（用来确认真的是想测的分支）。</summary>
    static string DescribeIhdr(byte[] d)
    {
        if (d.Length < 33) return "IHDR?";
        int w = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
        int h = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
        return $"bit={d[24]} color={d[25]} interlace={d[28]} {w}x{h}";
    }
}
