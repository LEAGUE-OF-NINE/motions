using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Motions;

[System.Serializable]
public class FrameSpec
{
    public double t;
    public string sprite;
    /// <summary>World-unit nudge from where this frame would sit by default. Baked into the sprite pivot.</summary>
    public double[] offset;
    public double scale = 1.0;
}

[System.Serializable]
public class SfxSpec
{
    public double t;
    public string file;
    public double clipIn = 0.0;
    public double duration = 0.0;
}

[System.Serializable]
public class AnimationSpec
{
    public double duration;
    public double ppu = SpriteMotionSpec.DefaultPpu;
    /// <summary>"point" for pixel art, anything else for bilinear.</summary>
    public string filter;
    public List<FrameSpec> frames;
    public List<SfxSpec> sfx;
}

/// <summary>
/// Pure parsing and maths for bundle-free sprite motions. Deliberately free of UnityEngine and
/// interop types so it can be exercised by Motions.Tests without a running game, which is also
/// why folder parsing yields a motion name string rather than a MOTION_DETAIL.
/// </summary>
public static class SpriteMotionSpec
{
    /// <summary>Measured against Yi Sang in Task 1. Halve it to render every frame twice as big.</summary>
    public const double DefaultPpu = 200.0;

    public const double DefaultFps = 12.0;

    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// "S1_1" -> ("S1", 1). The suffix counts as an index only if it parses as a non-negative
    /// integer, so motion names that legitimately contain underscores survive intact.
    /// </summary>
    /// <param name="isKnownName">
    /// Optional test for "is this whole string already a motion name?". Damaged_2 and Damaged_3
    /// are real MOTION_DETAIL values, so a whole-name match has to beat the _N rule or the loader
    /// treats them as coin variants of Damaged and skips them. Passed in rather than looked up
    /// here because this file must stay free of MOTION_DETAIL - see Motions.Tests.csproj.
    /// </param>
    public static (string name, int index) ParseFolderName(string folderName,
                                                           Func<string, bool> isKnownName = null)
    {
        if (string.IsNullOrEmpty(folderName)) return (folderName, 0);

        if (isKnownName != null && isKnownName(folderName)) return (folderName, 0);

        int cut = folderName.LastIndexOf('_');
        if (cut <= 0 || cut == folderName.Length - 1) return (folderName, 0);

        string suffix = folderName.Substring(cut + 1);
        if (!int.TryParse(suffix, System.Globalization.NumberStyles.None,
                          System.Globalization.CultureInfo.InvariantCulture, out int index))
            return (folderName, 0);

        return (folderName.Substring(0, cut), index);
    }

    /// <summary>
    /// Orders "frame_2" before "frame_10" by comparing runs of digits numerically. Plain string
    /// comparison would put 10 first, which silently scrambles any animation not zero-padded.
    /// </summary>
    public static int CompareNatural(string a, string b)
    {
        if (a == null || b == null) return string.CompareOrdinal(a, b);

        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;

                string da = a.Substring(si, i - si).TrimStart('0');
                string db = b.Substring(sj, j - sj).TrimStart('0');

                if (da.Length != db.Length) return da.Length - db.Length;
                int cmp = string.CompareOrdinal(da, db);
                if (cmp != 0) return cmp;
            }
            else
            {
                if (a[i] != b[j]) return a[i] - b[j];
                i++; j++;
            }
        }
        return (a.Length - i) - (b.Length - j);
    }

    /// <summary>Scaling up means fewer pixels per world unit, so the sprite covers more ground.</summary>
    public static double EffectivePpu(double ppu, double scale)
        => scale <= 0 ? ppu : ppu / scale;

    /// <summary>
    /// Normalised pivot placing the frame <paramref name="offsetX"/> world units from the transform,
    /// horizontally centred. The pivot is the point pinned to the transform, so it moves opposite the
    /// offset. Values outside 0..1 are legal in Unity.
    /// </summary>
    public static double PivotX(double offsetX, int width, double effectivePpu)
        => width <= 0 ? 0.5 : 0.5 - offsetX * effectivePpu / width;

    /// <summary>
    /// Vertically the frame is anchored by its BOTTOM edge, not its centre: measured in Task 1, the
    /// character transform sits at the feet, so centring buries half of every frame underground.
    /// An offset.y of 0 therefore means "standing on the ground".
    /// </summary>
    public static double PivotY(double offsetY, int height, double effectivePpu)
        => height <= 0 ? 0.0 : -offsetY * effectivePpu / height;

    /// <summary>
    /// Index of the frame showing at <paramref name="t"/>, stepped. Clamps to the first frame before
    /// the start: the sandbox renderer replaces the original, so showing nothing means an invisible
    /// character, which is always worse than showing frame zero early.
    /// </summary>
    public static int FrameIndexAt(double[] times, double t)
    {
        if (times == null || times.Length == 0) return -1;

        int result = 0;
        for (int i = 1; i < times.Length; i++)
        {
            if (times[i] <= t) result = i;
            else break;
        }
        return result;
    }

    /// <summary>Zero-config fallback: every PNG in the folder, natural order, evenly spaced.</summary>
    public static AnimationSpec DefaultSpec(IEnumerable<string> pngFileNames)
    {
        var ordered = pngFileNames.ToList();
        ordered.Sort(CompareNatural);

        var spec = new AnimationSpec
        {
            ppu = DefaultPpu,
            duration = ordered.Count / DefaultFps,
            frames = new List<FrameSpec>(ordered.Count),
            sfx = new List<SfxSpec>()
        };

        for (int i = 0; i < ordered.Count; i++)
            spec.frames.Add(new FrameSpec
            {
                t = i / DefaultFps,
                sprite = ordered[i],
                scale = 1.0,
                offset = new double[] { 0, 0 }
            });

        return spec;
    }

    /// <summary>
    /// Parses and validates. Returns null with a human-readable reason rather than throwing, so the
    /// caller can log it and fall back to the bundle instead of taking down a battle.
    /// </summary>
    public static AnimationSpec Parse(string json, out string error)
    {
        error = null;
        AnimationSpec spec;

        try
        {
            spec = JsonSerializer.Deserialize<AnimationSpec>(json, Options);
        }
        catch (Exception ex)
        {
            error = $"malformed JSON: {ex.Message}";
            return null;
        }

        if (spec == null) { error = "JSON parsed to nothing"; return null; }
        if (spec.frames == null || spec.frames.Count == 0) { error = "no frames"; return null; }
        if (spec.duration <= 0) { error = $"duration must be > 0, got {spec.duration}"; return null; }
        if (spec.ppu <= 0) { error = $"ppu must be > 0, got {spec.ppu}"; return null; }

        for (int i = 0; i < spec.frames.Count; i++)
        {
            var frame = spec.frames[i];
            if (frame == null || string.IsNullOrEmpty(frame.sprite)) { error = $"frame {i} has no sprite"; return null; }
            if (frame.scale <= 0) frame.scale = 1.0;
            if (frame.offset == null || frame.offset.Length < 2) frame.offset = new double[] { 0, 0 };
        }

        // Authoring order should not have to match time order.
        spec.frames.Sort((a, b) => a.t.CompareTo(b.t));

        spec.sfx ??= new List<SfxSpec>();
        spec.sfx.RemoveAll(sfx => sfx == null || string.IsNullOrEmpty(sfx.file));

        return spec;
    }
}
