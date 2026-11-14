using LsrXmlEditor.PedViewer.GameData;

namespace LsrXmlEditor.PedViewer.Rendering;

public sealed record HeadMorph(ComponentPart Base, IReadOnlyList<float[]> Positions, TextureData? Skin);

public static class HeadBlender
{
    public static HeadMorph? Build(PedCatalog catalog, HeadBlend blend)
    {
        var shapeWeights = Weights(blend.ShapeMix, blend.ThirdMix);
        var shapes = Resolve(catalog, shapeWeights, blend.ShapeFirst, blend.ShapeSecond, blend.ShapeThird);
        var head = shapes[0];
        if (head?.Drawable is null)
        {
            return null;
        }

        var skinWeights = Weights(blend.SkinMix, blend.ThirdMix);
        var skins = Resolve(catalog, skinWeights, blend.SkinFirst, blend.SkinSecond, blend.SkinThird);
        return new HeadMorph(head, BlendShapes(shapes, shapeWeights), BlendSkins(skins, skinWeights));
    }

    private static float[] Weights(float mix, float third) => [(1 - mix) * (1 - third), mix * (1 - third), third];

    private static ComponentPart?[] Resolve(PedCatalog catalog, float[] weights, params int[] heads) =>
        heads.Select((head, i) => i == 0 || weights[i] > 0 ? catalog.Resolve(PartData.HeadComponent, head, 0) : null).ToArray();

    private static List<float[]> BlendShapes(ComponentPart?[] shapes, float[] weights)
    {
        var geometries = shapes.Select(s => MeshExtractor.Geometries(s?.Drawable).ToArray()).ToArray();
        var result = new List<float[]>(geometries[0].Length);
        for (var g = 0; g < geometries[0].Length; g++)
        {
            var basePositions = MeshExtractor.ReadPositions(geometries[0][g]);
            var blended = new float[basePositions.Length];
            var total = 0f;
            for (var s = 0; s < shapes.Length; s++)
            {
                if (weights[s] <= 0 || g >= geometries[s].Length)
                {
                    continue;
                }

                var source = s == 0 ? basePositions : MeshExtractor.ReadPositions(geometries[s][g]);
                if (source.Length != blended.Length)
                {
                    continue;
                }

                Accumulate(blended, source, weights[s]);
                total += weights[s];
            }

            result.Add(total > 0 ? Normalize(blended, total) : basePositions);
        }

        return result;
    }

    private static TextureData? BlendSkins(ComponentPart?[] skins, float[] weights)
    {
        var layers = new List<(TextureData Texture, float Weight)>();
        for (var s = 0; s < skins.Length; s++)
        {
            if (weights[s] > 0 && skins[s]?.DiffuseTexture is { } texture && MeshExtractor.ExtractTexture(texture) is { } data)
            {
                layers.Add((data, weights[s]));
            }
        }

        if (layers.Count == 0)
        {
            return skins[0]?.DiffuseTexture is { } fallback ? MeshExtractor.ExtractTexture(fallback) : null;
        }

        if (layers.Count == 1)
        {
            return layers[0].Texture;
        }

        var target = layers[0].Texture;
        var total = layers.Sum(l => l.Weight);
        var bytes = new byte[target.Bgra.Length];
        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                var i = (y * target.Width + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    var sum = 0f;
                    foreach (var (texture, weight) in layers)
                    {
                        sum += Sample(texture, x, y, target, c) * weight;
                    }

                    bytes[i + c] = (byte)Math.Clamp(sum / total + 0.5f, 0, 255);
                }
            }
        }

        return new TextureData(target.Width, target.Height, bytes);
    }

    private static byte Sample(TextureData texture, int x, int y, TextureData target, int channel)
    {
        var sx = x * texture.Width / target.Width;
        var sy = y * texture.Height / target.Height;
        return texture.Bgra[(sy * texture.Width + sx) * 4 + channel];
    }

    private static void Accumulate(float[] target, float[] source, float weight)
    {
        for (var i = 0; i < target.Length; i++)
        {
            target[i] += source[i] * weight;
        }
    }

    private static float[] Normalize(float[] values, float total)
    {
        if (MathF.Abs(total - 1) < 1e-5f)
        {
            return values;
        }

        for (var i = 0; i < values.Length; i++)
        {
            values[i] /= total;
        }

        return values;
    }
}
