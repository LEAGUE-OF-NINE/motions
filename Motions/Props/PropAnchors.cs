using System.Collections.Generic;
using UnityEngine;

namespace Motions;

/// <summary>
/// Turns the prop format's anchor names - "self", "enemy", "center", "enemies"/"group" - into world
/// points. Split out of <see cref="PropRig"/> because it is the one part of the rig with no opinion
/// about slots, motions or timing: hand it a name and an offset, get a point back.
/// </summary>
internal class PropAnchors
{
    private readonly SidecarSyncBehavior _sync;

    /// <summary>The rig's own transform, which is what "self" means and what "center" measures from.
    /// </summary>
    private readonly Transform _self;

    /// <summary>Every current target's position, refilled at most once per frame. A spread strike
    /// reads it per slot and a "group" aim averages it, so resolving it through interop per slot -
    /// on a 16-slot ring, every frame - would be the expensive way to learn the same thing.</summary>
    private readonly List<Vector3> _targets = new();
    private int _frame = -1;

    public PropAnchors(SidecarSyncBehavior sync, Transform self)
    {
        _sync = sync;
        _self = self;
    }

    /// <summary>How many targets are on the field. Reading this is what refreshes the list, so a
    /// caller can check the count and then index straight away - which is the only order the
    /// indexer below is valid in, and now the only order that compiles naturally.</summary>
    public int Count
    {
        get
        {
            Refresh();
            return _targets.Count;
        }
    }

    /// <summary>One target's position. Valid after <see cref="Count"/>, which fills the list.</summary>
    public Vector3 this[int i] => _targets[i];

    /// <summary>A prop-format [x,y,z] as a Vector3. The JSON carries doubles and Unity wants floats,
    /// and every position and offset in the format makes that trip.</summary>
    public static Vector3 Vec3(double[] v) => new Vector3((float)v[0], (float)v[1], (float)v[2]);

    private void Refresh()
    {
        if (_frame == Time.frameCount) return;

        _frame = Time.frameCount;
        _targets.Clear();
        if (_sync != null) _sync.GetTargetPositions(_targets);
    }

    /// <summary>Names that mean "the target group" rather than one unit.</summary>
    private static bool IsGroupTarget(string to)
        => PropSpec.Is(to, "enemies") || PropSpec.Is(to, "group");

    /// <summary>Where a strike is aiming, in world space. Resolves to the same three anchor points
    /// as SidecarSyncBehavior.PositionVfx ("enemy" / "center" / "self"), but a struck prop is Lerped
    /// mid-flight and cannot reparent itself the way a VFX cue does, so <paramref name="action"/>'s
    /// offset is applied in world space here rather than in the target's local frame. The same
    /// offset on a strike and on a bundle VFX cue diverges when the target's transform carries
    /// non-identity rotation or scale.</summary>
    public bool StrikeTarget(PropAction action, out Vector3 world)
        => Resolve(action.to, action.offset, out world);

    /// <summary>Where a strike launches from, in world space. Returns false for the default "slot",
    /// which is not a fixed point: it is the slot's own ring position, which only the rig's per-slot
    /// pass knows. Past that guard it is the same vocabulary <see cref="StrikeTarget"/> reads,
    /// through the same resolver - written twice, "from" lost the group anchors "to" gained and the
    /// docs promised.</summary>
    public bool StrikeOrigin(PropAction action, out Vector3 world)
    {
        world = Vector3.zero;

        if (string.IsNullOrEmpty(action.from) || PropSpec.Is(action.from, "slot")) return false;

        return Resolve(action.from, action.fromOffset, out world);
    }

    /// <summary>One anchor name plus an offset to one world point. False means "nothing to measure
    /// from" - no targets on the field - and every caller falls back to the ring slot rather than
    /// the world origin, which would fling the prop off the map.
    /// <para>
    /// Anchors come out of the frame-cached target list rather than walking the view chain again:
    /// that walk is four interop hops plus an Il2Cpp list wrapper allocation per call, and it ran
    /// per strike per frame for an answer already sitting in the list. The list skips null targets,
    /// so "enemy" is the first *live* target rather than the first entry.
    /// </para></summary>
    public bool Resolve(string anchor, double[] offsets, out Vector3 world)
    {
        world = Vector3.zero;
        if (_sync == null) return false;

        var offset = Vec3(offsets);

        if (PropSpec.Is(anchor, "self"))
        {
            world = _self.position + offset;
            return true;
        }

        int count = Count;
        if (count == 0) return false;

        // The middle of the whole target group, not of the caster and one enemy. With targets at
        // different depths, "enemy" puts everything on whichever one the game lists first, which
        // reads as off-centre the moment there is more than one.
        if (IsGroupTarget(anchor))
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < count; i++) sum += _targets[i];

            world = sum / count + offset;
            return true;
        }

        world = PropSpec.Is(anchor, "center")
            ? (_self.position + _targets[0]) / 2f + offset
            : _targets[0] + offset;

        return true;
    }
}
