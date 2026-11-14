using CodeWalker.GameFiles;
using LsrXmlEditor.PedViewer.GameData;
using SharpDX;

namespace LsrXmlEditor.PedViewer.Rendering;

public sealed record VehicleColors(int Primary, int Secondary);

public static class ModelExtractor
{
    private const int SolidSize = 4;

    private static readonly uint[] DiffuseSamplers =
    [
        JenkHash.GenHash("diffusesampler"),
        JenkHash.GenHash("platebgsampler"),
        JenkHash.GenHash("texturesampler"),
        JenkHash.GenHash("diffusesampler2")
    ];

    private static readonly uint PaletteSampler = JenkHash.GenHash("texturesamplerdiffpal");

    private static readonly string[] TransparentShaders = ["glass", "alpha", "decal", "cutout", "cloth", "hair", "emissive"];

    public static IReadOnlyList<PartData> Extract(GameArchive archive, GameModel model, VehicleColors colors)
    {
        var library = ModelLibrary.For(archive);
        var textures = new Dictionary<string, TextureData?>(StringComparer.Ordinal);
        var groups = new Dictionary<string, (List<MeshData> Meshes, TextureData? Texture, bool Transparent)>(StringComparer.Ordinal);

        foreach (var piece in model.Pieces)
        {
            foreach (var geometry in piece.Model.Geometries ?? [])
            {
                var mesh = ExtractGeometry(geometry, piece.Transform);
                if (mesh is null)
                {
                    continue;
                }

                var shader = geometry.Shader?.Name.ToCleanString() ?? string.Empty;
                var paint = PaintSlot(shader);
                string key;
                TextureData? texture;
                if (paint > 0)
                {
                    var id = paint == 2 ? colors.Secondary : colors.Primary;
                    key = "paint:" + id;
                    texture = Solid(VehiclePalette.Get(id));
                }
                else
                {
                    var source = DiffuseOf(geometry.Shader);
                    var palette = Sampler(geometry.Shader, PaletteSampler);
                    key = (source is null ? "none" : "tex:" + source.Name) + (palette is null ? string.Empty : "|pal:" + palette.Name);
                    if (!textures.TryGetValue(key, out texture))
                    {
                        texture = Resolve(library, model, piece, source);
                        if (texture is not null && palette is not null && Resolve(library, model, piece, palette) is { } swatches)
                        {
                            texture = ApplyPalette(texture, swatches);
                        }

                        textures[key] = texture;
                    }
                }

                var transparent = TransparentShaders.Any(t => shader.Contains(t, StringComparison.OrdinalIgnoreCase));
                key += transparent ? "|t" : "|o";
                if (!groups.TryGetValue(key, out var group))
                {
                    group = ([], texture, transparent);
                    groups[key] = group;
                }

                group.Meshes.Add(mesh);
            }
        }

        var parts = new List<PartData>(groups.Count);
        var index = 0;
        foreach (var (meshes, texture, transparent) in groups.Values)
        {
            var finalTexture = texture is null ? null : transparent ? texture : Opaque(texture);
            parts.Add(new PartData(index++, meshes, finalTexture, [], transparent));
        }

        return parts;
    }

    private static int PaintSlot(string shader)
    {
        if (!shader.StartsWith("vehicle_paint", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return shader.EndsWith('2') ? 2 : 1;
    }

    private static TextureBase? DiffuseOf(ShaderFX? shader) =>
        DiffuseSamplers.Select(sampler => Sampler(shader, sampler)).FirstOrDefault(t => t is not null)
        ?? shader?.ParametersList?.Parameters?.Select(p => p.Data).OfType<TextureBase>().FirstOrDefault();

    private static TextureBase? Sampler(ShaderFX? shader, uint sampler)
    {
        var parameters = shader?.ParametersList;
        if (parameters?.Parameters is null || parameters.Hashes is null)
        {
            return null;
        }

        for (var i = 0; i < parameters.Parameters.Length && i < parameters.Hashes.Length; i++)
        {
            if ((uint)parameters.Hashes[i] == sampler && parameters.Parameters[i].Data is TextureBase texture)
            {
                return texture;
            }
        }

        return null;
    }

    private static TextureData ApplyPalette(TextureData texture, TextureData palette)
    {
        var bytes = new byte[texture.Bgra.Length];
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var p = texture.Bgra[i + 3] * (palette.Width - 1) / byte.MaxValue * 4;
            var shade = texture.Bgra[i + 2];
            bytes[i] = (byte)(palette.Bgra[p] * shade / byte.MaxValue);
            bytes[i + 1] = (byte)(palette.Bgra[p + 1] * shade / byte.MaxValue);
            bytes[i + 2] = (byte)(palette.Bgra[p + 2] * shade / byte.MaxValue);
            bytes[i + 3] = byte.MaxValue;
        }

        return new TextureData(texture.Width, texture.Height, bytes);
    }

    private static TextureData? Resolve(ModelLibrary library, GameModel model, ModelPiece piece, TextureBase? source)
    {
        if (source is null)
        {
            return null;
        }

        if (source is Texture { Data: not null } embedded)
        {
            return MeshExtractor.ExtractTexture(embedded);
        }

        var hash = source.NameHash != 0 ? source.NameHash : JenkHash.GenHash(source.Name?.ToLowerInvariant() ?? string.Empty);
        if (piece.Embedded?.Dict is { } own && own.TryGetValue(hash, out var local))
        {
            return MeshExtractor.ExtractTexture(local);
        }

        foreach (var dictionary in model.Dictionaries)
        {
            if (library.Dictionary(dictionary)?.Dict is { } textures && textures.TryGetValue(hash, out var found))
            {
                return MeshExtractor.ExtractTexture(found);
            }
        }

        return null;
    }

    private static MeshData? ExtractGeometry(DrawableGeometry geometry, Matrix transform)
    {
        var data = geometry.VertexData;
        var indices = geometry.IndexBuffer?.Indices;
        if (data?.Info is null || indices is null || data.VertexCount == 0)
        {
            return null;
        }

        var positions = MeshExtractor.ReadPositions(geometry);
        if (!transform.IsIdentity)
        {
            for (var i = 0; i < positions.Length; i += 3)
            {
                var p = Vector3.TransformCoordinate(new Vector3(positions[i], positions[i + 1], positions[i + 2]), transform);
                (positions[i], positions[i + 1], positions[i + 2]) = (p.X, p.Y, p.Z);
            }
        }

        return new MeshData(positions, MeshExtractor.ReadTexCoords(data), MeshExtractor.Triangles(indices));
    }

    private static TextureData Opaque(TextureData texture)
    {
        var bytes = (byte[])texture.Bgra.Clone();
        MeshExtractor.MakeOpaque(bytes);
        return texture with { Bgra = bytes };
    }

    private static TextureData Solid((byte R, byte G, byte B) color)
    {
        var bytes = new byte[SolidSize * SolidSize * 4];
        for (var i = 0; i < bytes.Length; i += 4)
        {
            (bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3]) = (color.B, color.G, color.R, 255);
        }

        return new TextureData(SolidSize, SolidSize, bytes);
    }
}
