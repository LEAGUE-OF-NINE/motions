using UnityEngine;

namespace Motions;

/// <summary>
/// One strike action, resolved once for the whole rig this frame. Everything a strike needs beyond
/// the slot's own ring position depends only on the action and the clock, so it is worked out above
/// the slot loop instead of per slot. Instances are pooled and reused across frames - see
/// <see cref="PropRig"/>'s strike list - so every field is rewritten by <see cref="Load"/> rather
/// than set at construction.
/// </summary>
internal class ActiveStrike
{
    public PropAction Action;
    public int Ordinal;
    public Vector3 Target;

    /// <summary>Eased progress along whichever leg is running.</summary>
    public double Progress;

    /// <summary>True once past 'arrive' with a returnAt: Progress runs target -> home.</summary>
    public bool Returning;
    public bool Hidden;
    public bool HasSpin;
    public double SpinDegrees;

    /// <summary>Where this leg starts, in world space. Usually the slot's own ring position,
    /// resolved in the slot loop - HasOrigin says the action named a fixed one instead, in which
    /// case every slot it owns launches from the same point.</summary>
    public bool HasOrigin;
    public Vector3 Origin;

    /// <summary>Vertical bow to add at this progress, already evaluated.</summary>
    public double Arc;

    /// <summary>True while the instance is drawing back rather than travelling.</summary>
    public bool DrawingBack;

    /// <summary>How far back it draws, along the origin-to-target axis reversed.</summary>
    public double WindUp;

    /// <summary>Set when the action asked to lead along its travel and spin did not override it.</summary>
    public bool HasFace;

    /// <summary>Consumed on arrival and staying gone until the gate's count changes.</summary>
    public bool ConsumeToGate;

    /// <summary>Non-null when this strike ends by parking: the formation mode to park with.</summary>
    public string ParkMode;

    /// <summary>True once the strike has reached its target, so arrival effects can fire.</summary>
    public bool Arrived;

    /// <summary>The action aimed at the prop's own ring slot, which only the slot loop can resolve.
    /// Arriving there is what un-parks a parked prop.</summary>
    public bool TargetIsSlot;

    /// <summary>One target per slot, dealt round-robin: slot i takes target i % count. Also only
    /// the slot loop can resolve it, for the same reason TargetIsSlot cannot be hoisted.</summary>
    public bool Spread;

    /// <summary>action.offset as a Vector3, converted once rather than per slot.</summary>
    public Vector3 Offset;

    /// <summary>Fills this pooled instance in for one action on one frame. Every field is written,
    /// so nothing carries over from whichever strike used the slot last.</summary>
    public void Load(PropAction action, int ordinal, double fraction, double timebase,
                     Vector3 target, bool targetIsSlot, bool spread, bool returning,
                     PropAnchors anchors)
    {
        Action = action;
        Ordinal = ordinal;
        Target = target;
        TargetIsSlot = targetIsSlot;
        Spread = spread;
        Offset = PropAnchors.Vec3(action.offset);
        Returning = returning;
        Arrived = fraction >= action.arrive;
        ParkMode = string.IsNullOrEmpty(action.park) ? null : action.park;

        HasOrigin = anchors.StrikeOrigin(action, out Vector3 origin);
        Origin = origin;

        SetProgress(action, fraction, returning);

        // Consumed on arrival. Hidden rather than destroyed: props cannot spend buff stacks, so the
        // gate would respawn it within a frame. A parking strike is exempt - park and consume are
        // both "no returnAt" endings, and the hide rule keys off exactly the field park leaves
        // unset, so without this check every parked prop vanishes on arrival.
        Hidden = fraction >= action.arrive
                 && action.returnAt < 0
                 && string.IsNullOrEmpty(action.park);
        ConsumeToGate = Hidden && PropSpec.Is(action.consume, "gate");

        HasSpin = action.spin != 0;
        SpinDegrees = action.spin * fraction * timebase;

        // spin beats face: a rate and an orientation rule cannot both own the rotation, and spin is
        // the more explicit of the two.
        HasFace = action.face && !HasSpin;
    }

    /// <summary>How far along its current leg the strike is, and whether that leg is the draw-back.
    /// Three shapes: coming home, throwing after a wind-up, or a plain throw.</summary>
    private void SetProgress(PropAction action, double fraction, bool returning)
    {
        // The wind-up only shapes the outbound leg. Coming home there is nothing to draw back from,
        // so the return runs its own progress straight through.
        DrawingBack = false;
        WindUp = action.windUp;

        if (returning)
        {
            Progress = PropSpec.Ease(
                PropSpec.ActionProgress(fraction, action.arrive, action.returnAt), action.ease);
        }
        else
        {
            double outbound = PropSpec.ActionProgress(fraction, action.start, action.arrive);

            if (action.windUp != 0.0)
            {
                double leg = PropSpec.WindUpSplit(outbound, action.windUpTime, out bool drawingBack);
                DrawingBack = drawingBack;
                // The draw-back is not eased: easing shapes the throw, and running the same curve
                // over a pull-back makes the wind-up drift instead of snapping taut.
                Progress = drawingBack ? leg : PropSpec.Ease(leg, action.ease);
            }
            else
            {
                Progress = PropSpec.Ease(outbound, action.ease);
            }
        }

        // Arc rides the travelling leg, not the draw-back: the wind-up restarts progress at 0 for
        // the throw, so arcing both halves bowed the prop over the pull-back and again over the
        // throw. The pull is straight back along the throw's axis; the throw keeps the bow.
        Arc = DrawingBack ? 0.0 : PropSpec.ArcOffset(action.arc, Progress);
    }

    /// <summary>Where this strike puts an instance at its current progress: drawing back, travelling
    /// out, or coming home. Pure - the three legs are the whole of a strike's shape.
    /// <para>
    /// <paramref name="origin"/> and <paramref name="target"/> are per-slot, which is why they come
    /// in rather than being read off the fields above: a recall aims at the slot's own ring
    /// position, and a spread deals each slot its own enemy.
    /// </para></summary>
    public Vector3 PositionAt(Vector3 origin, Vector3 target, Vector3 slotHome)
    {
        Vector3 toTarget = target - origin;
        Vector3 at;

        if (DrawingBack)
        {
            // Away from the target along the same axis it will travel, so the throw reads as one
            // motion reversed rather than a detour.
            Vector3 back = toTarget.sqrMagnitude > 0f
                ? -toTarget.normalized * (float)WindUp
                : Vector3.zero;
            at = Vector3.Lerp(origin, origin + back, (float)Progress);
        }
        else if (Returning)
        {
            // Home is the parked spot when there is one: park is sticky, so an ordinary strike
            // borrows a parked prop and gives it back rather than un-parking it.
            at = Vector3.Lerp(target, slotHome, (float)Progress);
        }
        else
        {
            // Past the draw-back, the throw starts from where it drew back to.
            Vector3 launch = origin;
            if (WindUp != 0.0 && toTarget.sqrMagnitude > 0f)
                launch = origin - toTarget.normalized * (float)WindUp;

            at = Vector3.Lerp(launch, target, (float)Progress);
        }

        if (Arc != 0.0) at += new Vector3(0f, (float)Arc, 0f);

        return at;
    }

    /// <summary>The rotation a strike that asked to lead along its travel should wear. Taken off the
    /// leg's own axis, not the frame's movement delta: the delta is zero on the frame a leg starts,
    /// and a knife flipping to 0 degrees for one frame at launch is more visible than any accuracy
    /// that buys.</summary>
    public double FaceDegrees(Vector3 origin, Vector3 target, Vector3 slotHome)
    {
        Vector3 facing = Returning ? slotHome - target : target - origin;
        return PropSpec.FaceDegrees(facing.x, facing.y);
    }

    /// <summary>Which slots this action moves. "next" hands the i-th matching action the i-th slot,
    /// deterministic without per-play bookkeeping: a 3-knife entry with three strikes on one motion
    /// throws a different knife each time, in the order they were written.</summary>
    public bool OwnsSlot(int slot, int count)
    {
        if (PropSpec.Is(Action.slot, "all")) return true;
        if (PropSpec.Is(Action.slot, "next")) return count > 0 && slot == Ordinal % count;

        return int.TryParse(Action.slot, out int index) && index == slot;
    }
}
