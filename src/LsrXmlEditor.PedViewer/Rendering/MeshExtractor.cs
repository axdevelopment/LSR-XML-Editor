using CodeWalker.GameFiles;
using CodeWalker.Utils;
using LsrXmlEditor.PedViewer.GameData;

namespace LsrXmlEditor.PedViewer.Rendering;

public sealed record TextureData(int Width, int Height, byte[] Bgra);

public sealed record SkinData(byte[] Indices, byte[] Weights, ushort[]? BoneIds);

public sealed record MeshData(float[] Positions, float[] TexCoords, int[] Indices, SkinData? Skin = null);

public sealed record DecalData(MeshData Mesh, TextureData Texture);

public sealed record PartData(int Component, IReadOnlyList<MeshData> Meshes, TextureData? Texture, IReadOnlyList<DecalData> Decals, bool? Transparent = null)
{
    public const int HeadComponent = 0;
    public const int HairComponent = 2;
    public const int DecalComponent = 10;

    public bool IsTransparent => Transparent ?? Component is HairComponent or DecalComponent;
}

public sealed record HairColors(int Primary, int Secondary);

public sealed record PartStyle(HairColors Hair, OverlaySet Overlays);

public static class MeshExtractor
{
    private const int PositionSemantic = 0;
    private const int BlendWeightSemantic = 1;
    private const int BlendIndexSemantic = 2;
    private const int NormalSemantic = 3;
    private const int TexCoordSemantic = 6;
    private const int DecalCoordSemantic = 7;
    private const int MaxTextureSize = 1024;
    private const float DecalOffset = 0.0018f;

    public static PartData Extract(ComponentPart part, PartStyle style) =>
        Extract(part, style, null, part.DiffuseTexture is null ? null : ExtractTexture(part.DiffuseTexture));

    public static PartData Extract(HeadMorph morph, PartStyle style) =>
        Extract(morph.Base, style, morph.Positions, morph.Skin);

    internal static IEnumerable<DrawableGeometry> Geometries(Drawable? drawable) =>
        drawable?.DrawableModels?.High?.SelectMany(m => m.Geometries ?? []) ?? [];

    internal static float[] ReadPositions(DrawableGeometry geometry)
    {
        var data = geometry.VertexData;
        if (data?.Info is null || data.VertexCount == 0)
        {
            return [];
        }

        var positions = new float[data.VertexCount * 3];
        for (var v = 0; v < data.VertexCount; v++)
        {
            var p = data.GetVector3(v, PositionSemantic);
            positions[v * 3] = p.X;
            positions[v * 3 + 1] = p.Y;
            positions[v * 3 + 2] = p.Z;
        }

        return positions;
    }

    internal static TextureData? ExtractTexture(Texture texture)
    {
        try
        {
            var mip = 0;
            var width = (int)texture.Width;
            var height = (int)texture.Height;
            while ((width > MaxTextureSize || height > MaxTextureSize) && mip + 1 < texture.Levels)
            {
                mip++;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }

            var pixels = DDSIO.GetPixels(texture, mip);
            return pixels is null || pixels.Length < width * height * 4 ? null : new TextureData(width, height, pixels);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static PartData Extract(ComponentPart part, PartStyle style, IReadOnlyList<float[]>? morphs, TextureData? texture)
    {
        var meshes = new List<MeshData>();
        var decals = new List<DecalData>();
        var zones = DecalZones(part);
        var receivesDecals = style.Overlays.Zones.Count > 0 && zones.Length > 0;

        var index = 0;
        foreach (var geometry in Geometries(part.Drawable))
        {
            var morph = morphs is not null && index < morphs.Count ? morphs[index] : null;
            index++;
            var mesh = ExtractGeometry(geometry, morph);
            if (mesh is null)
            {
                continue;
            }

            meshes.Add(mesh);
            if (receivesDecals)
            {
                decals.AddRange(ExtractDecals(geometry, style.Overlays, zones, mesh.Positions));
            }
        }

        if (texture is not null)
        {
            switch (part.Component)
            {
                case PartData.HairComponent:
                    HairPalette.Tint(texture.Bgra, style.Hair.Primary, style.Hair.Secondary);
                    break;
                case PartData.DecalComponent:
                    break;
                default:
                    MakeOpaque(texture.Bgra);
                    break;
            }

            if (part.Component == PartData.HeadComponent)
            {
                foreach (var layer in style.Overlays.HeadLayers)
                {
                    Blend(texture, layer);
                }
            }
        }

        return new PartData(part.Component, meshes, texture, decals);
    }

    private static int[] DecalZones(ComponentPart part)
    {
        if (part.Component != PartData.HeadComponent && !part.DrawableName.EndsWith("_r", StringComparison.Ordinal))
        {
            return [];
        }

        return part.Component switch
        {
            PartData.HeadComponent => [OverlaySet.HeadZone],
            3 or 8 or 11 => [OverlaySet.TorsoZone, OverlaySet.LeftArmZone, OverlaySet.RightArmZone],
            4 or 6 => [OverlaySet.LeftLegZone, OverlaySet.RightLegZone],
            _ => []
        };
    }

    private static MeshData? ExtractGeometry(DrawableGeometry geometry, float[]? morph)
    {
        var data = geometry.VertexData;
        var indices = geometry.IndexBuffer?.Indices;
        if (data?.Info is null || indices is null || data.VertexCount == 0)
        {
            return null;
        }

        var count = data.VertexCount;
        var positions = morph?.Length == count * 3 ? morph : ReadPositions(geometry);
        return new MeshData(positions, ReadTexCoords(data), Triangles(indices), ReadSkin(geometry));
    }

    internal static float[] ReadTexCoords(VertexData data)
    {
        var texCoords = new float[data.VertexCount * 2];
        for (var v = 0; v < data.VertexCount; v++)
        {
            var (u, w) = ReadUv(data, v, TexCoordSemantic);
            texCoords[v * 2] = u;
            texCoords[v * 2 + 1] = w;
        }

        return texCoords;
    }

    internal static SkinData? ReadSkin(DrawableGeometry geometry)
    {
        var data = geometry.VertexData;
        if (data?.Info is null || !Has(data, BlendWeightSemantic) || !Has(data, BlendIndexSemantic))
        {
            return null;
        }

        var count = data.VertexCount;
        var indices = new byte[count * 4];
        var weights = new byte[count * 4];
        for (var v = 0; v < count; v++)
        {
            var index = data.GetColour(v, BlendIndexSemantic);
            var weight = data.GetColour(v, BlendWeightSemantic);
            (indices[v * 4], indices[v * 4 + 1], indices[v * 4 + 2], indices[v * 4 + 3]) = (index.R, index.G, index.B, index.A);
            (weights[v * 4], weights[v * 4 + 1], weights[v * 4 + 2], weights[v * 4 + 3]) = (weight.R, weight.G, weight.B, weight.A);
        }

        return new SkinData(indices, weights, geometry.BoneIds);
    }

    private static IEnumerable<DecalData> ExtractDecals(DrawableGeometry geometry, OverlaySet overlays, int[] allowedZones, float[] positions)
    {
        var data = geometry.VertexData;
        var indices = geometry.IndexBuffer?.Indices;
        if (data?.Info is null || indices is null || !Has(data, DecalCoordSemantic))
        {
            yield break;
        }

        var hasNormals = Has(data, NormalSemantic);
        var skin = ReadSkin(geometry);
        var perZone = new Dictionary<int, (List<float> Positions, List<float> Uvs, List<int> Indices, List<int> Sources)>();
        for (var t = 0; t + 2 < indices.Length; t += 3)
        {
            var (firstU, firstV) = ReadUv(data, indices[t], DecalCoordSemantic);
            var zone = OverlaySet.ZoneOf(firstV);
            if (zone < 0 || !allowedZones.Contains(zone) || !overlays.Zones.ContainsKey(zone))
            {
                continue;
            }

            if (!perZone.TryGetValue(zone, out var target))
            {
                target = ([], [], [], []);
                perZone[zone] = target;
            }

            var tileU = MathF.Floor(firstU);
            for (var k = 0; k < 3; k++)
            {
                var v = indices[t + k];
                var (x, y, z) = (positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2]);
                if (hasNormals)
                {
                    var n = data.GetVector3(v, NormalSemantic);
                    (x, y, z) = (x + n.X * DecalOffset, y + n.Y * DecalOffset, z + n.Z * DecalOffset);
                }

                var (u, w) = ReadUv(data, v, DecalCoordSemantic);
                target.Positions.AddRange([x, y, z]);
                target.Uvs.AddRange([OverlaySet.ZoneX(u, tileU), OverlaySet.ZoneY(w, zone)]);
                target.Indices.Add(target.Indices.Count);
                target.Sources.Add(v);
            }
        }

        foreach (var (zone, mesh) in perZone)
        {
            yield return new DecalData(new MeshData([.. mesh.Positions], [.. mesh.Uvs], [.. mesh.Indices], Gather(skin, mesh.Sources)), overlays.Zones[zone]);
        }
    }

    private static SkinData? Gather(SkinData? skin, List<int> sources)
    {
        if (skin is null)
        {
            return null;
        }

        var indices = new byte[sources.Count * 4];
        var weights = new byte[sources.Count * 4];
        for (var i = 0; i < sources.Count; i++)
        {
            Array.Copy(skin.Indices, sources[i] * 4, indices, i * 4, 4);
            Array.Copy(skin.Weights, sources[i] * 4, weights, i * 4, 4);
        }

        return new SkinData(indices, weights, skin.BoneIds);
    }

    internal static bool Has(VertexData data, int semantic) => (data.Info.Flags & (1u << semantic)) != 0;

    internal static (float U, float V) ReadUv(VertexData data, int vertex, int semantic)
    {
        if (!Has(data, semantic))
        {
            return (0, 0);
        }

        switch (data.Info.GetComponentType(semantic))
        {
            case VertexComponentType.Half2:
                var half = data.GetHalf2(vertex, semantic);
                return (half.X, half.Y);
            case VertexComponentType.Float2:
                var full = data.GetVector2(vertex, semantic);
                return (full.X, full.Y);
            default:
                return (0, 0);
        }
    }

    internal static int[] Triangles(ushort[] indices)
    {
        var triangles = new int[indices.Length - indices.Length % 3];
        for (var i = 0; i < triangles.Length; i++)
        {
            triangles[i] = indices[i];
        }

        return triangles;
    }

    private static void Blend(TextureData target, HeadLayer layer)
    {
        var mask = layer.Mask;
        for (var y = 0; y < target.Height; y++)
        {
            var my = y * mask.Height / target.Height;
            for (var x = 0; x < target.Width; x++)
            {
                var mx = x * mask.Width / target.Width;
                var m = (my * mask.Width + mx) * 4;
                var alpha = mask.Bgra[m + 3] / 255f * layer.Opacity;
                if (alpha <= 0.01f)
                {
                    continue;
                }

                var i = (y * target.Width + x) * 4;
                if (layer.Tint is { } tint)
                {
                    var shade = (mask.Bgra[m] + mask.Bgra[m + 1] + mask.Bgra[m + 2]) / 765f;
                    var strength = 0.55f + shade * 0.45f;
                    Mix(target.Bgra, i, tint.B * strength, tint.G * strength, tint.R * strength, alpha);
                }
                else
                {
                    Mix(target.Bgra, i, mask.Bgra[m], mask.Bgra[m + 1], mask.Bgra[m + 2], alpha);
                }
            }
        }
    }

    private static void Mix(byte[] target, int index, float b, float g, float r, float alpha)
    {
        target[index] = (byte)Math.Clamp(target[index] * (1 - alpha) + b * alpha, 0, 255);
        target[index + 1] = (byte)Math.Clamp(target[index + 1] * (1 - alpha) + g * alpha, 0, 255);
        target[index + 2] = (byte)Math.Clamp(target[index + 2] * (1 - alpha) + r * alpha, 0, 255);
    }

    internal static void MakeOpaque(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            bgra[i] = 255;
        }
    }
}
