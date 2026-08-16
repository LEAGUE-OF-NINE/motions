using System;

namespace Motions;

/// <summary>
/// One prop action: a strike (move ring instances to a target and back) or a plant (spawn a
/// world instance and forget it). Times are fractions of the motion's duration, as everywhere here.
/// </summary>
[System.Serializable]
public class PropAction
{
    /// <summary>"strike" or "plant". Named @do because "do" is the JSON key and a C# keyword.</summary>
    public string @do = "strike";
    public string motion;
    /// <summary>-1 matches any coin.</summary>
    public int coin = -1;
    /// <summary>"next", "all", or a 0-based slot index. Strike only.</summary>
    public string slot = "next";

    public double start;
    public double arrive;
    /// <summary>Negative means the instance is consumed on arrival rather than returning.</summary>
    public double returnAt = -1;

    /// <summary>Plant only: when the world instance appears.</summary>
    public double at;

    public string to = "enemy";
    public double[] offset;
    public double[] pos;
    public string ease;
    public double spin;
    public int rounds = 1;

    /// <summary>What becomes of a consumed instance. "motion" hides it until the motion ends, as
    /// omitting returnAt always has. "gate" keeps it gone until the gate's target count changes -
    /// which on a fixed-count keywordless entry is never, so it is gone for the battle. Deliberately
    /// sharp, and opt-in.</summary>
    public string consume = "motion";

    /// <summary>Where the strike launches from: "slot" (its ring position), "self", "center" or "enemy".</summary>
    public string from = "slot";
    /// <summary>Offset on that origin. "self" alone launches from the character's feet.</summary>
    public double[] fromOffset;

    /// <summary>World units the path bows. Positive arcs over, negative dips under.</summary>
    public double arc;

    /// <summary>World units drawn back away from the target before the throw.</summary>
    public double windUp;
    /// <summary>Fraction of the outbound window spent drawing back. Ignored when windUp is 0.</summary>
    public double windUpTime = 0.3;

    /// <summary>Rotate the instance so it leads along its direction of travel. spin overrides this.</summary>
    public bool face;

    /// <summary>Ends the strike by leaving the instance at its target, and that placement outlives
    /// the motion. "ring" recentres the entry's orbit on the park point, "hold" freezes each slot at
    /// its parked angle, "stack" puts every slot on the point. Null or empty is the ordinary
    /// return/hide ending.</summary>
    public string park;
}

/// <summary>
/// One prop declaration from CharacterVFX.json. Art comes from a PNG folder (resolved relative
/// to the JSON that declared it) or a bundle prefab; folder wins when both are set.
/// </summary>
[System.Serializable]
public class PropEntry
{
    public string folder;
    public string prefab;

    /// <summary>"unit" rings the character, "world" places one instance in the scene, "target"
    /// rings every enemy carrying this entry's keyword.</summary>
    public string anchor = "unit";

    /// <summary>Target rings only: point every instance at the unit it orbits instead of letting
    /// it follow the ambient spin. The strike-level 'face' is the same idea for a flight.</summary>
    // ponytail: target rings only. Wire it into the caster's own ring when someone wants it there.
    public bool face;
    public bool front = true;

    /// <summary>Unit: the ring centre, local to the effect root. World: the placement point.</summary>
    public double[] pos;
    /// <summary>World anchor only: treat pos as absolute scene coordinates, not caster-relative.</summary>
    public bool world;

    public string layer = "Front";
    /// <summary>0 means "pick the default for this anchor and side" at spawn time.</summary>
    public int order;

    /// <summary>Uniform size multiplier on every instance. 0 or less reads as 1, so an omitted
    /// or zeroed scale leaves the art at its natural size rather than making it invisible.</summary>
    public double scale = 1;

    public int count = 1;
    public double radius;
    /// <summary>Ring radius while parked, for when a formation should hang wider around an enemy
    /// than it orbits its own caster. 0 means "same as radius".</summary>
    public double parkRadius;
    /// <summary>Degrees per second around the ring. 0 pins every slot.</summary>
    public double speed;
    /// <summary>Ring start angle, degrees.</summary>
    public double phase;
    public double bob;
    public double bobPeriod;
    /// <summary>Self-rotation, degrees per second.</summary>
    public double spin;

    /// <summary>Omit for an always-on prop.</summary>
    public string keyword;
    public int stackThres;
    public int turnThres;
    public int maxCount = PropSpec.HardCountCeiling;

    /// <summary>How long a parked instance stays parked. "battle" keeps it until an action recalls
    /// it; "round" also snaps every parked slot home at the next round start, so no park leaks
    /// across a turn.</summary>
    public string parkUntil = "battle";

    public PropAction[] actions;
}

/// <summary>
/// Placement, gating and timing maths for props. Deliberately free of UnityEngine and interop
/// types so Motions.Tests can exercise it without a running game - see Motions.Tests.csproj.
/// </summary>
public static class PropSpec
{
    /// <summary>Instances per entry, whatever the JSON asks for. A typo'd threshold must not
    /// spawn hundreds of objects into a battle.</summary>
    public const int HardCountCeiling = 16;

    /// <summary>How many instances should exist right now.</summary>
    public static int TargetCount(PropEntry entry, bool gatePasses, int stack)
    {
        if (entry == null) return 0;

        int max = entry.maxCount > 0 ? Math.Min(entry.maxCount, HardCountCeiling) : HardCountCeiling;

        if (string.IsNullOrEmpty(entry.keyword))
            return Math.Clamp(entry.count, 0, max);

        return gatePasses ? Math.Clamp(stack, 0, max) : 0;
    }

    /// <summary>Where slot <paramref name="slot"/> of <paramref name="count"/> sits on the ring.</summary>
    public static double SlotAngleDegrees(int slot, int count, double phase, double speed, double t)
    {
        if (count <= 0) return phase;
        return phase + 360.0 * slot / count + speed * t;
    }

    /// <summary>Vertical bob. Slots are phase-shifted around the ring so a row of props breathes
    /// instead of rising and falling as one.</summary>
    public static double BobOffset(double bob, double bobPeriod, double t, int slot, int count)
    {
        if (bob == 0.0 || bobPeriod <= 0.0) return 0.0;

        double slotPhase = count > 0 ? 2.0 * Math.PI * slot / count : 0.0;
        return bob * Math.Sin(2.0 * Math.PI * t / bobPeriod + slotPhase);
    }

    /// <summary>The ring's shape at one slot: the offset from the formation's centre, no centre
    /// baked in. Split out of SlotPosition so a parked formation reuses it around a different point
    /// instead of growing a copy that drifts.</summary>
    public static void RingOffset(PropEntry entry, int slot, int count, double t,
                                  out double x, out double y,
                                  double radiusOverride = 0.0)
    {
        x = y = 0.0;
        if (entry == null) return;

        // 0 means "no opinion": a formation with no parkRadius keeps its radius when parked,
        // instead of collapsing onto the point.
        double radius = radiusOverride > 0.0 ? radiusOverride : entry.radius;

        double radians = SlotAngleDegrees(slot, count, entry.phase, entry.speed, t) * Math.PI / 180.0;

        x = Math.Cos(radians) * radius;
        y = Math.Sin(radians) * radius + BobOffset(entry.bob, entry.bobPeriod, t, slot, count);
    }

    /// <summary>Ring position of one slot, relative to whatever the instance is parented to.</summary>
    public static void SlotPosition(PropEntry entry, int slot, int count, double t,
                                    out double x, out double y, out double z)
    {
        double cx = 0, cy = 0, cz = 0;
        if (entry != null && entry.pos != null)
        {
            if (entry.pos.Length > 0) cx = entry.pos[0];
            if (entry.pos.Length > 1) cy = entry.pos[1];
            if (entry.pos.Length > 2) cz = entry.pos[2];
        }

        RingOffset(entry, slot, count, t, out double ox, out double oy);

        x = cx + ox;
        y = cy + oy;

        // The ring is drawn in the xy plane, so z is entirely the centre's. RingOffset used to
        // return a z too and it was always 0, threaded through two callers to add nothing.
        z = cz;
    }

    /// <summary>0 before <paramref name="start"/>, 1 after <paramref name="end"/>, linear between.
    /// An end at or before the start reads as instantaneous rather than dividing by zero - the same
    /// "clamp, don't reject" line TimelineBuilder takes with hit checkers.</summary>
    public static double ActionProgress(double t, double start, double end)
    {
        if (end <= start) return t >= start ? 1.0 : 0.0;
        return Math.Clamp((t - start) / (end - start), 0.0, 1.0);
    }

    /// <summary>Shapes a 0..1 progress. Not DOTween: props need a curve, and four cases beat
    /// depending on DOVirtual being in this build's interop. OrdinalIgnoreCase rather than
    /// lowercasing: this runs per struck slot per frame, where ToLowerInvariant allocates.</summary>
    public static double Ease(double p, string name)
    {
        p = Math.Clamp(p, 0.0, 1.0);
        if (string.IsNullOrEmpty(name)) return p;

        if (string.Equals(name, "inquad", StringComparison.OrdinalIgnoreCase))
            return p * p;
        if (string.Equals(name, "outquad", StringComparison.OrdinalIgnoreCase))
            return 1.0 - (1.0 - p) * (1.0 - p);
        if (string.Equals(name, "inoutquad", StringComparison.OrdinalIgnoreCase))
            return p < 0.5 ? 2.0 * p * p : 1.0 - 2.0 * (1.0 - p) * (1.0 - p);

        return p;
    }

    /// <summary>Vertical bow of a strike's path at progress <paramref name="p"/>. A sine is zero at
    /// both ends and peaks at the authored height mid-flight, so an arc changes only how the strike
    /// travels, never where it starts or lands.</summary>
    public static double ArcOffset(double arc, double p)
        => arc == 0.0 ? 0.0 : arc * Math.Sin(Math.PI * Math.Clamp(p, 0.0, 1.0));

    /// <summary>Z rotation, in degrees, pointing a sprite along (<paramref name="dx"/>, <paramref name="dy"/>).
    /// Zero points right, the direction prop art faces by convention. A zero vector returns 0 rather
    /// than letting Atan2's undefined case become a NaN rotation.</summary>
    public static double FaceDegrees(double dx, double dy)
        => dx == 0.0 && dy == 0.0 ? 0.0 : Math.Atan2(dy, dx) * 180.0 / Math.PI;

    /// <summary>
    /// Splits a strike's outbound progress into a draw-back and a throw.
    /// <para>
    /// The first <paramref name="windUpTime"/> of the window draws back, the rest travels. Returns
    /// 0..1 within whichever phase <paramref name="p"/> falls in: the caller lerps origin-to-drawn-
    /// back while <paramref name="drawingBack"/> is set, drawn-back-to-target after. A wind-up
    /// filling the whole window still completes rather than dividing by zero.
    /// </para>
    /// </summary>
    public static double WindUpSplit(double p, double windUpTime, out bool drawingBack)
    {
        p = Math.Clamp(p, 0.0, 1.0);
        double split = Math.Clamp(windUpTime, 0.0, 1.0);

        if (split <= 0.0) { drawingBack = false; return p; }

        if (p < split)
        {
            drawingBack = true;
            return p / split;
        }

        drawingBack = false;
        return split >= 1.0 ? 1.0 : (p - split) / (1.0 - split);
    }

    /// <summary>Authored rounds to a live counter. -1 is the "until the battle ends" sentinel.</summary>
    public static int InitialRounds(int authored) => authored <= 0 ? -1 : authored;

    public static int TickRounds(int roundsLeft) => roundsLeft <= 0 ? roundsLeft : roundsLeft - 1;

    public static bool Expired(int roundsLeft) => roundsLeft == 0;

    /// <summary>Returns a human-readable complaint, or null when the entry is usable.</summary>
    public static string Validate(PropEntry entry)
    {
        if (entry == null) return "entry is null";

        if (string.IsNullOrEmpty(entry.folder) && string.IsNullOrEmpty(entry.prefab))
            return "neither 'folder' nor 'prefab' is set";

        bool isWorld = string.Equals(entry.anchor, "world", StringComparison.OrdinalIgnoreCase);
        bool isTarget = string.Equals(entry.anchor, "target", StringComparison.OrdinalIgnoreCase);
        bool isUnit = string.IsNullOrEmpty(entry.anchor)
                      || string.Equals(entry.anchor, "unit", StringComparison.OrdinalIgnoreCase);

        if (!isWorld && !isUnit && !isTarget)
            return $"unknown anchor '{entry.anchor}' - use \"unit\", \"world\" or \"target\"";

        // A target ring is a pure function of who is standing there and what they carry. Strikes
        // move the caster's own slots and plants place one instance at one point, so neither has
        // a reading here - and a silently ignored action is only found by staring at a battle.
        if (isTarget && entry.actions != null && entry.actions.Length > 0)
            return "a target-anchored entry takes no actions - its rings follow the keyword, "
                   + "not the motion clock";

        if (!string.IsNullOrEmpty(entry.parkUntil)
            && !string.Equals(entry.parkUntil, "battle", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.parkUntil, "round", StringComparison.OrdinalIgnoreCase))
            return $"unknown parkUntil '{entry.parkUntil}' - use \"battle\" or \"round\"";

        if (entry.actions != null)
        {
            foreach (var action in entry.actions)
            {
                if (action == null) return "an action is null";
                if (string.IsNullOrEmpty(action.motion)) return "an action has no 'motion'";

                // A slot the runtime cannot parse matches nothing, forever: the action loads and
                // resolves its target but moves no instance. Caught here for the same reason
                // park+returnAt is - in game it is invisible, and looks like three other failures.
                if (!string.IsNullOrEmpty(action.slot)
                    && !string.Equals(action.slot, "all", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(action.slot, "next", StringComparison.OrdinalIgnoreCase)
                    && !int.TryParse(action.slot, out _))
                    return $"action on motion '{action.motion}' has slot '{action.slot}' - "
                           + "use \"all\", \"next\", or a 0-based index";

                bool isStrike = !string.Equals(action.@do, "plant", StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(action.park))
                {
                    if (!isStrike)
                        return $"action on motion '{action.motion}' is a plant, and only a strike can park";

                    if (!string.Equals(action.park, "ring", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(action.park, "hold", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(action.park, "stack", StringComparison.OrdinalIgnoreCase))
                        return $"unknown park mode '{action.park}' - use \"ring\", \"hold\" or \"stack\"";

                    // Two endings for one strike. Picking either one silently would be a guess
                    // about which the author meant, and the wrong guess is invisible in game.
                    if (action.returnAt >= 0)
                        return $"action on motion '{action.motion}' sets both 'park' and 'returnAt', "
                               + "which are two different endings - drop one";
                }

                // A strike moves an instance out of a ring slot and puts it back. A world-anchored
                // entry has no ring, so there is no useful reading of this.
                if (isStrike && isWorld)
                    return $"action on motion '{action.motion}' is a strike, but the entry is world-anchored";
            }
        }

        return null;
    }

    /// <summary>A null or short vector as a usable [x,y,z]. Every consumer indexes [0], [1] and [2]
    /// unconditionally, so anything shorter is widened here or throws at the read.</summary>
    private static double[] Pad3(double[] v)
    {
        if (v != null && v.Length >= 3) return v;

        var padded = new double[] { 0, 0, 0 };
        if (v != null)
            for (int i = 0; i < v.Length && i < 3; i++) padded[i] = v[i];

        return padded;
    }

    /// <summary>Fills nulls and clamps authored fractions into 0..1, in place.</summary>
    public static void Normalize(PropEntry entry)
    {
        if (entry == null) return;

        entry.pos = Pad3(entry.pos);

        // A scale of 0 is what an author gets for writing the key and leaving it, and it renders an
        // invisible prop that looks like a load failure. Same "0 means the default" rule as maxCount.
        if (entry.scale <= 0.0) entry.scale = 1.0;

        // Filled, not filtered: consumers walk this array with no per-element null check, but a
        // null element cannot reach here - Validate rejects the whole entry on one, and it runs
        // first and unconditionally in the only load path. The strip that lived here was dead code,
        // its two tests pinning impossible input. The null-to-empty is not: Initialize reads
        // actions.Length unchecked.
        entry.actions ??= new PropAction[0];

        foreach (var action in entry.actions)
        {
            action.start = Math.Clamp(action.start, 0.0, 1.0);
            action.arrive = Math.Clamp(action.arrive, 0.0, 1.0);
            action.at = Math.Clamp(action.at, 0.0, 1.0);
            if (action.returnAt > 1.0) action.returnAt = 1.0;

            // Padded, not only null-checked. entry.pos got this from the start and the action
            // arrays did not, so a two-element "offset": [0, 0.5] - which the docs' [x,y,z] table
            // invites - reached PropRig intact and threw on [2]. Update catches that throw and sets
            // _broken, so one short array silently killed every prop on the character all battle.
            action.offset = Pad3(action.offset);
            action.pos = Pad3(action.pos);
            action.fromOffset = Pad3(action.fromOffset);

            action.windUpTime = Math.Clamp(action.windUpTime, 0.0, 1.0);
        }
    }
}
