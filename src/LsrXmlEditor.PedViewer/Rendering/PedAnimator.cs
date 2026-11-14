using CodeWalker.GameFiles;
using LsrXmlEditor.PedViewer.GameData;
using SharpDX;

namespace LsrXmlEditor.PedViewer.Rendering;

public sealed class PedAnimator
{
    private const int PositionTrack = 0;
    private const int RotationTrack = 1;
    private const int ScaleTrack = 2;

    private static readonly Dictionary<ushort, ushort> RollBones = new() { [23639] = 58271, [6442] = 51826 };

    private readonly Skeleton _skeleton;
    private readonly Bone[] _bones;
    private readonly Bone[] _sorted;
    private readonly ClipBase _clip;
    private readonly Matrix[] _skin;
    private Matrix _heading = Matrix.Identity;

    private PedAnimator(Skeleton skeleton, string dictionary, ClipBase clip, double duration)
    {
        Dictionary = dictionary;
        _skeleton = skeleton;
        _bones = skeleton.Bones.Items;
        _sorted = skeleton.BonesSorted ?? _bones;
        _clip = clip;
        _skin = new Matrix[_bones.Length];
        Duration = duration;
        _skeleton.ResetBoneTransforms();
        var bind = Heading(_bones[0].AnimTransform);
        Pose(0);
        _heading = Matrix.RotationZ(bind - Heading(_bones[0].AnimTransform));
    }

    public string Dictionary { get; }

    public double Duration { get; }

    public string ClipName => _clip.ShortName ?? _clip.Name ?? string.Empty;

    public static PedAnimator? Create(GameArchive archive, string pedModel, string dictionary, string clipName, out string? error)
    {
        error = null;
        var skeleton = archive.FindFile(pedModel.ToLowerInvariant() + ".yft") is { } yft
            ? archive.Load<YftFile>(yft)?.Fragment?.Drawable?.Skeleton
            : null;
        if (skeleton?.Bones?.Items is null)
        {
            error = $"No skeleton found for {pedModel}.";
            return null;
        }

        var female = pedModel.StartsWith("mp_f_", StringComparison.OrdinalIgnoreCase);
        if (AnimationLibrary.For(archive).Find(dictionary, clipName, female) is not { } found)
        {
            error = dictionary.Trim().Length == 0
                ? $"Animation \"{clipName}\" not found in the game's gesture dictionaries."
                : $"Animation \"{clipName}\" not found in {dictionary}.";
            return null;
        }

        var clip = found.Clip;
        var duration = clip switch
        {
            ClipAnimation single => (single.EndTime - single.StartTime) / Math.Max(0.01f, single.Rate),
            ClipAnimationList list => list.Duration,
            _ => 0
        };

        return duration > 0 ? new PedAnimator(skeleton, found.Dictionary, clip, duration) : null;
    }

    public void Pose(double time)
    {
        switch (_clip)
        {
            case ClipAnimation single:
                Apply(single.Animation, single.GetPlaybackTime(time));
                break;
            case ClipAnimationList list:
                foreach (var entry in list.Animations ?? [])
                {
                    Apply(entry?.Animation, entry?.GetPlaybackTime(time) ?? 0);
                }

                break;
        }

        foreach (var bone in _sorted)
        {
            if (RollBones.TryGetValue(bone.Tag, out var source) && _skeleton.BonesMap.TryGetValue(source, out var parent) && parent != bone.Parent)
            {
                bone.AnimRotation = parent.AnimRotation;
            }
        }

        foreach (var bone in _sorted)
        {
            bone.UpdateAnimTransform();
            bone.UpdateSkinTransform();
        }

        for (var i = 0; i < _bones.Length; i++)
        {
            _skin[i] = _bones[i].SkinTransform * _heading;
        }
    }

    public bool Skin(MeshData mesh, float[] result)
    {
        if (mesh.Skin is not { } skin || result.Length != mesh.Positions.Length)
        {
            return false;
        }

        var ids = skin.BoneIds is { } boneIds && boneIds.Length != _bones.Length ? boneIds : null;
        var source = mesh.Positions;
        var count = source.Length / 3;
        for (var v = 0; v < count; v++)
        {
            var position = new Vector3(source[v * 3], source[v * 3 + 1], source[v * 3 + 2]);
            var sum = Vector3.Zero;
            var total = 0f;
            for (var k = 0; k < 4; k++)
            {
                var weight = skin.Weights[v * 4 + k] / 255f;
                if (weight <= 0)
                {
                    continue;
                }

                var index = (int)skin.Indices[v * 4 + k];
                var bone = ids is null ? index : index < ids.Length ? ids[index] : 0;
                if (bone >= _skin.Length)
                {
                    continue;
                }

                sum += Vector3.TransformCoordinate(position, _skin[bone]) * weight;
                total += weight;
            }

            var final = total > 0 ? sum / total : position;
            result[v * 3] = final.X;
            result[v * 3 + 1] = final.Y;
            result[v * 3 + 2] = final.Z;
        }

        return true;
    }

    private static float Heading(Matrix transform)
    {
        var forward = Vector3.TransformNormal(Vector3.UnitY, transform);
        return MathF.Atan2(forward.Y, forward.X);
    }

    private void Apply(Animation? animation, float time)
    {
        var tracks = animation?.BoneIds?.data_items;
        if (animation?.Sequences?.data_items is null || tracks is null)
        {
            return;
        }

        var frame = animation.GetFramePosition(time);
        for (var i = 0; i < tracks.Length; i++)
        {
            if (!_skeleton.BonesMap.TryGetValue(tracks[i].BoneId, out var bone))
            {
                continue;
            }

            switch (tracks[i].Track)
            {
                case PositionTrack:
                    var position = animation.EvaluateVector4(frame, i, true);
                    bone.AnimTranslation = new Vector3(position.X, position.Y, position.Z);
                    break;
                case RotationTrack:
                    bone.AnimRotation = animation.EvaluateQuaternion(frame, i, true);
                    break;
                case ScaleTrack:
                    var scale = animation.EvaluateVector4(frame, i, true);
                    bone.AnimScale = new Vector3(scale.X, scale.Y, scale.Z);
                    break;
            }
        }
    }
}
