using UnityEngine;

namespace Motions;

/// <summary>
/// The runtime half of <see cref="PlantKind"/>: spawn one world instance and forget it. Its field
/// rules live with the kind in PropSpec.cs, where the test suite can reach them.
/// <para>
/// Edge-triggered rather than time-derived like a strike, because a plant is a one-off: evaluating
/// it continuously would place a new instance every frame for the rest of the motion. That is the
/// whole difference between <see cref="PropActionKind.OneShot"/> and
/// <see cref="PropActionKind.MovesSlots"/>, and it is why the two are dispatched from different
/// loops in <see cref="PropRig"/>.
/// </para></summary>
internal static class PropPlant
{
    /// <summary>True on the single frame <paramref name="fraction"/> crosses the action's time.
    /// <paramref name="previous"/> is the last fraction seen, or -1 when the motion restarted - a
    /// plant authored early in a looping motion has to fire again on every pass.</summary>
    public static bool DueThisFrame(PropAction action, double fraction, double previous)
        => fraction >= action.at && previous < action.at;

    /// <summary>Places the instance. Silently does nothing when there is nothing to anchor to,
    /// having said so once - the alternative is a prop at the world origin.</summary>
    public static void Fire(string appearanceID, PropEntry entry, PropAction action,
                            PropAnchors anchors)
    {
        Vector3 pos = PropAnchors.Vec3(action.pos);
        Vector3 where;

        if (entry.world) where = pos;
        else if (anchors.StrikeTarget(action, out Vector3 anchor)) where = anchor + pos;
        else
        {
            Logger.LogInfo($"[Props] plant on '{action.motion}' had no target to anchor to, skipping.");
            return;
        }

        // Discarded here: a plant that hit the ceiling or failed to build gives the caller nothing
        // to do differently, and PropWorld already logged whichever it was.
        PropWorld.Place(appearanceID, entry, where, action.rounds, out _);
    }
}
