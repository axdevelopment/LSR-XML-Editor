namespace LsrXmlEditor.PedViewer.Rendering;

public static class HairPalette
{
    private static readonly (byte R, byte G, byte B)[] Colors =
    [
        (28, 31, 33), (39, 42, 44), (49, 46, 44), (53, 38, 28), (75, 50, 31), (92, 59, 36), (109, 76, 53), (107, 80, 59),
        (118, 92, 69), (127, 104, 78), (153, 129, 93), (167, 147, 105), (175, 156, 112), (187, 160, 99), (214, 185, 123), (218, 195, 142),
        (159, 127, 89), (132, 80, 57), (104, 43, 31), (97, 18, 12), (100, 15, 10), (124, 20, 15), (160, 46, 25), (182, 75, 40),
        (162, 80, 47), (170, 78, 43), (98, 98, 98), (128, 128, 128), (170, 170, 170), (197, 197, 197), (70, 57, 85), (90, 63, 107),
        (118, 60, 118), (237, 116, 227), (235, 75, 147), (242, 153, 188), (4, 149, 158), (2, 95, 134), (2, 57, 116), (63, 161, 106),
        (33, 124, 97), (24, 92, 85), (182, 192, 52), (112, 169, 11), (67, 157, 19), (220, 184, 87), (229, 177, 3), (230, 145, 2),
        (242, 136, 49), (251, 128, 87), (226, 139, 88), (209, 89, 60), (206, 49, 32), (173, 9, 3), (136, 3, 2), (31, 24, 20),
        (41, 31, 25), (46, 34, 27), (55, 41, 30), (46, 34, 24), (35, 27, 21), (2, 2, 2), (112, 108, 102), (157, 122, 80)
    ];

    public const int DefaultPrimary = 4;

    public static int Count => Colors.Length;

    public static (byte R, byte G, byte B) Get(int index) => Colors[Math.Clamp(index, 0, Colors.Length - 1)];

    public static void Tint(byte[] bgra, int primary, int secondary)
    {
        var (pr, pg, pb) = Get(primary);
        var (sr, sg, sb) = Get(secondary);
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var strand = bgra[i + 1] / 255f;
            var highlight = bgra[i + 2] / 255f * 0.45f;
            var shade = 0.35f + strand * 0.9f;
            bgra[i] = Mix(pb * shade, sb, highlight);
            bgra[i + 1] = Mix(pg * shade, sg, highlight);
            bgra[i + 2] = Mix(pr * shade, sr, highlight);
        }
    }

    private static byte Mix(float baseValue, byte highlightValue, float amount) =>
        (byte)Math.Clamp(baseValue * (1 - amount) + highlightValue * amount, 0, 255);
}
