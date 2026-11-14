using CodeWalker.GameFiles;
using SharpDX;

namespace LsrXmlEditor.PedViewer.GameData;

public enum ModelKind
{
    Vehicle,
    Weapon,
    Prop
}

public sealed record ModelPiece(DrawableModel Model, Matrix Transform, TextureDictionary? Embedded);

public sealed record GameModel(string Name, string FileName, ModelKind Kind, IReadOnlyList<ModelPiece> Pieces, IReadOnlyList<uint> Dictionaries);

public static class ModelLoader
{
    private const string WeaponPrefix = "weapon_";

    private static readonly HashSet<ushort> FrontWheels = [27922, 26418];
    private static readonly HashSet<ushort> RearWheels = [29921, 29922, 29923, 27902, 5857, 5858, 5859, 26398];
    private static readonly HashSet<ushort> RightWheels = [26418, 5857, 5858, 5859, 26398];

    public static GameModel? Load(GameArchive archive, string name)
    {
        var library = ModelLibrary.For(archive);
        var model = name.Trim().ToLowerInvariant();
        var kind = ModelKind.Prop;
        if (model.StartsWith(WeaponPrefix, StringComparison.Ordinal))
        {
            model = library.WeaponModel(model)?.ToLowerInvariant() ?? model;
            kind = ModelKind.Weapon;
        }

        if (archive.FindFile(model + ".yft") is { } fragmentPath && archive.Load<YftFile>(fragmentPath)?.Fragment is { } fragment)
        {
            var pieces = FragmentPieces(fragment, out var hasWheels);
            if (kind == ModelKind.Prop && (hasWheels || fragmentPath.Contains(@"\vehicles", StringComparison.OrdinalIgnoreCase)))
            {
                kind = ModelKind.Vehicle;
            }

            return new GameModel(name, model + ".yft", kind, pieces, Dictionaries(library, model, kind != ModelKind.Vehicle));
        }

        if (archive.FindFile(model + ".ydr") is { } drawablePath && archive.Load<YdrFile>(drawablePath)?.Drawable is { } drawable)
        {
            return new GameModel(name, model + ".ydr", kind, [.. DrawablePieces(drawable, null)], Dictionaries(library, model, true));
        }

        if (archive.FindFile(model + ".ydd") is { } dictionaryPath && archive.Load<YddFile>(dictionaryPath)?.Drawables?.FirstOrDefault() is { } first)
        {
            return new GameModel(name, model + ".ydd", kind, [.. DrawablePieces(first, null)], Dictionaries(library, model, true));
        }

        return null;
    }

    private static List<uint> Dictionaries(ModelLibrary library, string model, bool includeArchetype)
    {
        var result = new List<uint>();
        var roots = new List<uint> { JenkHash.GenHash(model) };
        if (includeArchetype && library.ArchetypeTextureDictionary(model) is { } archetype)
        {
            roots.Add(archetype);
        }

        foreach (var root in roots)
        {
            for (uint? current = root; current is { } hash && !result.Contains(hash); current = library.ParentDictionary(hash))
            {
                result.Add(hash);
            }
        }

        return result;
    }

    private static List<ModelPiece> FragmentPieces(FragType fragment, out bool hasWheels)
    {
        hasWheels = false;
        var pieces = new List<ModelPiece>();
        if (fragment.Drawable is { } body)
        {
            pieces.AddRange(DrawablePieces(body, Pose(fragment)));
        }

        var lod = fragment.PhysicsLODGroup?.PhysicsLOD1;
        var children = lod?.Children?.data_items ?? [];
        var front = children.FirstOrDefault(c => FrontWheels.Contains(c.BoneTag) && HasModels(c.Drawable1))?.Drawable1;
        var rear = children.FirstOrDefault(c => RearWheels.Contains(c.BoneTag) && HasModels(c.Drawable1))?.Drawable1;
        var transforms = lod?.FragTransforms?.Matrices;
        var offset = lod?.PositionOffset ?? Vector3.Zero;

        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            var isWheel = FrontWheels.Contains(child.BoneTag) || RearWheels.Contains(child.BoneTag);
            var drawable = isWheel
                ? (FrontWheels.Contains(child.BoneTag) ? front ?? rear : rear ?? front)
                : child.Drawable1;
            if (!HasModels(drawable))
            {
                continue;
            }

            hasWheels |= isWheel;
            var transform = ChildTransform(fragment, child, transforms, i, offset);
            foreach (var model in Models(drawable!))
            {
                pieces.Add(new ModelPiece(model, transform, drawable!.ShaderGroup?.TextureDictionary));
            }
        }

        return pieces;
    }

    private static Matrix ChildTransform(FragType fragment, FragPhysTypeChild child, Matrix[]? transforms, int index, Vector3 offset)
    {
        Matrix transform;
        if (transforms is not null && index < transforms.Length)
        {
            transform = transforms[index];
            transform.Row4 += new Vector4(offset, 0);
        }
        else if (fragment.Drawable?.Skeleton?.BonesMap?.TryGetValue(child.BoneTag, out var bone) == true)
        {
            transform = bone.AbsTransform;
        }
        else
        {
            transform = Matrix.Identity;
        }

        if (RightWheels.Contains(child.BoneTag))
        {
            var translation = transform.TranslationVector;
            transform = Matrix.Scaling(-1, 1, -1);
            transform.TranslationVector = translation;
        }

        transform.Column4 = Vector4.UnitW;
        return transform;
    }

    private static Matrix[]? Pose(FragType fragment)
    {
        var pose = fragment.BoneTransforms?.Items;
        if (pose is null)
        {
            return null;
        }

        return pose.Select(p => new Matrix(
            p.Row1.X, p.Row2.X, p.Row3.X, 0,
            p.Row1.Y, p.Row2.Y, p.Row3.Y, 0,
            p.Row1.Z, p.Row2.Z, p.Row3.Z, 0,
            p.Row1.W, p.Row2.W, p.Row3.W, 1)).ToArray();
    }

    private static IEnumerable<ModelPiece> DrawablePieces(DrawableBase drawable, Matrix[]? pose)
    {
        var bones = drawable.Skeleton?.Bones?.Items;
        foreach (var model in Models(drawable))
        {
            var binding = model.SkeletonBinding;
            var isSkin = ((binding >> 8) & 0xFF) > 0;
            var boneIndex = (int)((binding >> 24) & 0xFF);
            var transform = Matrix.Identity;
            if (!isSkin)
            {
                if (pose is not null && boneIndex < pose.Length)
                {
                    transform = pose[boneIndex];
                }
                else if (bones is not null && boneIndex < bones.Length)
                {
                    transform = bones[boneIndex].AbsTransform;
                }
            }

            transform.Column4 = Vector4.UnitW;
            yield return new ModelPiece(model, transform, drawable.ShaderGroup?.TextureDictionary);
        }
    }

    private static bool HasModels(DrawableBase? drawable) => drawable is not null && Models(drawable).Any();

    private static IEnumerable<DrawableModel> Models(DrawableBase drawable) =>
        drawable.DrawableModels?.High ?? drawable.DrawableModels?.Med ?? [];
}
