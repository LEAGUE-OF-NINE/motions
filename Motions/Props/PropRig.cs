using System;
using System.Collections.Generic;
using Lethe.Patches;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace Motions;

/// <summary>
/// Owns every unit-anchored prop for one character. Lives on the sidecar object so it inherits the
/// sidecar's lifetime and reads the same director clock that steps sprite frames.
/// <para>
/// What is left here is the part that needs to be a MonoBehaviour: per-instance state
/// (<see cref="Slot"/>, <see cref="Live"/>), how many instances should exist, and where each one
/// goes this frame. The two things that do not need the component are next door -
/// <see cref="PropAnchors"/> turns anchor names into world points, and <see cref="ActiveStrike"/>
/// knows a strike's own shape over time.
/// </para></summary>
public class PropRig : MonoBehaviour
{
    public PropRig(IntPtr ptr) : base(ptr) { }

    public SidecarSyncBehavior Sync;
    public string AppearanceID;

    private class Slot
    {
        public GameObject Obj;
        public SpriteRenderer Renderer;
        public SpriteMotion Art;
        public float Born;
        /// <summary>Mirrors Obj.activeSelf, so a consume/return only costs an interop call
        /// on the frame it actually changes rather than on every frame of the motion.</summary>
        public bool Hidden;

        /// <summary>Spent by a consume: "gate" strike. Unlike the motion-scoped hide it survives
        /// the motion ending, clearing only when RefreshCounts sees the entry's target count change
        /// - so on a fixed-count entry with no keyword the instance is gone for the battle.</summary>
        public bool Consumed;

        /// <summary>The action that parked this slot, re-resolved each frame so the spot tracks its
        /// anchor. Non-null IS "parked": the only state besides Consumed that outlives a motion,
        /// since everything else about a prop's position is a pure function of the motion's clock
        /// and snaps back when it ends. A Parked flag and a copy of the park mode used to live here;
        /// they were this field's null-ness and this action's park mode, kept in sync by hand across
        /// three sites, one of which had stopped clearing one of them.</summary>
        public PropAction ParkAction;
        /// <summary>Ring time frozen at the moment of parking. Only "hold" reads it.</summary>
        public double ParkT;

        /// <summary>The parking strike used face, so the angle it landed at is the one to hold.
        /// Rotation is written every frame (see Place), so without this a faced park loses its
        /// facing on the frame after it parks - one frame of "aimed", then flat art.</summary>
        public bool ParkFaced;
        public double ParkDegrees;
        /// <summary>Last resolved park centre, kept as the fallback for when the anchor dies.</summary>
        public Vector3 ParkPoint;

        /// <summary>Which target of a spread strike this slot parked on, or -1 for a park with one
        /// shared point. Kept so a formation spread over three enemies keeps tracking three enemies
        /// rather than collapsing onto whichever one the game lists first.</summary>
        public int ParkTargetIndex = -1;

        /// <summary>Last art frame written, so a 12fps prop stops re-assigning its sprite sixty
        /// times a second. Same mirror-what-you-wrote trick as Hidden above.</summary>
        public int Frame = -1;

        /// <summary>Ends a park. One place, because the fields below ParkAction are meaningful only
        /// while it is set and every one is rewritten on the next park - so forgetting one here is
        /// invisible until the day something reads it ungated.</summary>
        public void Unpark()
        {
            ParkAction = null;
            ParkTargetIndex = -1;
        }
    }

    private class Live
    {
        public PropEntry Entry;
        public List<Slot> Slots = new();

        /// <summary>Entry.actions[i].motion parsed once, parallel to Entry.actions. Enum.TryParse
        /// over MOTION_DETAIL is a case-insensitive linear scan of a large game enum whose answer
        /// never changes, so it has no business running per slot per frame. The parsed value cannot
        /// live on PropEntry: PropSpec must stay free of Il2Cpp types.</summary>
        public MOTION_DETAIL[] Motions;
        public bool[] MotionKnown;

        /// <summary>Entry.actions[i]'s kind, resolved once for the same reason Motions is: matching
        /// `do` by string ran twice per action per frame, and every kind added since has been one
        /// more string compare on that path. Never null - Validate rejects an unknown kind at load,
        /// so by the time an entry is live every action has one.</summary>
        public PropActionKind[] Kinds;

        /// <summary>Set when Spawn returns null for this entry. Per-rig, not written back to Entry:
        /// Entry is cached in MotionData.Props and shared by every character wearing this appearance
        /// for the whole battle, so one unit's missing-art failure must not disable the prop for the
        /// rest of them. This flag only stops this rig from retrying every poll.</summary>
        public bool Broken;

        /// <summary>World-anchored entries hold a PropWorld handle instead of ring slots.</summary>
        public bool IsWorld;
        public PropWorld.Placed WorldHandle;

        /// <summary>Target-anchored entries hold a ring per enemy instead of either.</summary>
        public bool IsTarget;
        public PropTargetRings TargetRings;

        /// <summary>Last target count RefreshCounts computed. Starts at -1 so the first poll always
        /// counts as a change; a gate-consumed slot is restored when this moves.</summary>
        public int LastWant = -1;
    }

    private readonly List<Live> _live = new();

    /// <summary>Reused across frames and across entries; _strikeCount is the live length.</summary>
    private readonly List<ActiveStrike> _strikes = new();
    private int _strikeCount;

    private bool _initialized;
    private bool _broken;
    private float _nextPoll;

    /// <summary>This frame's action timebase in seconds, resolved once in Update.</summary>
    private double _timebase;

    /// <summary>Anchor names to world points, over a target list it refreshes once a frame. Built in
    /// Initialize because Sync and the transform are both set before the first Step and never
    /// reassigned after.</summary>
    private PropAnchors _anchors;

    /// <summary>PropWorld.RoundToken as of the last poll, for parkUntil: "round".</summary>
    private int _roundToken = -1;

    /// <summary>Last motion fraction seen, so a plant fires on the frame it is crossed and not again.</summary>
    private double _lastFraction = -1.0;
    private MOTION_DETAIL _lastMotion;
    private int _lastCoin = -1;

    /// <summary>Gate polling interval. Hooking ViewAbilityTypo the way CharVFXParse does would miss
    /// a stack change arriving by any other path, and a prop count that lags a turn is the kind of
    /// bug nobody reports and everybody notices.</summary>
    // ponytail: 10 Hz gate poll. Hook the buff-apply path if it ever shows in a profile.
    private const float PollInterval = 0.1f;

    private void Initialize()
    {
        _initialized = true;
        _anchors = new PropAnchors(Sync, transform);

        var entries = MotionData.GetProps(AppearanceID);
        if (entries == null) return;

        foreach (var entry in entries)
        {
            var actions = entry.actions;
            var motions = new MOTION_DETAIL[actions.Length];
            var known = new bool[actions.Length];
            var kinds = new PropActionKind[actions.Length];

            for (int i = 0; i < actions.Length; i++)
            {
                known[i] = Enum.TryParse<MOTION_DETAIL>(actions[i].motion, true, out motions[i]);

                // A MOTION_DETAIL typo is the likeliest mistake in the whole format - it loads
                // clean and then never fires. The answer is already here, so say it rather than
                // leave authors comparing the string against their own filenames.
                if (!known[i])
                    Logger.LogWarning($"[Props] {AppearanceID}: action names motion " +
                                      $"'{actions[i].motion}', which this game has no such motion " +
                                      $"for - that action will never fire.");

                // Validate refuses an unknown kind before an entry ever reaches here, so this is a
                // lookup rather than a check. Falling back to strike would resurrect exactly the
                // "everything that is not a plant is a strike" rule this replaced.
                kinds[i] = PropActionKind.Find(actions[i].@do) ?? PropActionKind.All[0];
            }

            bool isTarget = PropSpec.Is(entry.anchor, "target");

            _live.Add(new Live
            {
                Entry = entry,
                Motions = motions,
                MotionKnown = known,
                Kinds = kinds,
                IsWorld = PropSpec.Is(entry.anchor, "world"),
                IsTarget = isTarget,
                TargetRings = isTarget ? new PropTargetRings(AppearanceID, entry) : null
            });
        }

        Logger.LogInfo($"[Props] Rig for {AppearanceID} tracking {_live.Count} unit-anchored prop(s).");
    }

    private Transform EffectRoot(bool front)
    {
        if (Sync == null || Sync.Appearance == null) return transform;

        var view = Sync.Appearance.GetView();
        if (view == null) return transform;

        var root = front ? view.viewEffectRootDirection : view.viewEffectRootBack;
        return root != null ? root : transform;
    }

    /// <summary>Matches action <paramref name="index"/> of <paramref name="live"/> against the
    /// motion playing now, using the MOTION_DETAIL parsed at Initialize.</summary>
    [HideFromIl2Cpp]
    private bool MotionMatches(Live live, int index)
    {
        if (Sync == null || !Sync.IsModdedSkillActive) return false;
        if (!live.MotionKnown[index]) return false;
        if (live.Motions[index] != Sync.CurrentMotion) return false;

        int coin = live.Entry.actions[index].coin;
        return coin < 0 || coin == Sync.CurrentCoin;
    }

    /// <summary>Reads the buff the entry names. Mirrors CharVFXParse.SatisfiesVFXRequirement,
    /// including its rule that a buff below either threshold fails outright rather than counting
    /// partially.</summary>
    [HideFromIl2Cpp]
    private bool GateStack(PropEntry entry, out int stack)
    {
        stack = 0;

        var model = CasterModel();
        if (model == null) return false;

        // The reading lives with the target rings: the same question asked of a different unit is
        // all that separates "gated on my buff" from "gated on theirs", and two copies of a
        // threshold rule is how the two drift apart.
        return !string.IsNullOrEmpty(entry.keyword)
               && PropTargetRings.GateStack(model, entry, out stack);
    }

    /// <summary>The caster's own battle model, or null before the view exists.</summary>
    private BattleUnitModel CasterModel()
    {
        if (Sync == null || Sync.Appearance == null) return null;

        var view = Sync.Appearance.GetView();
        if (view == null) return null;

        return view.unitModel;
    }

    /// <summary>One ring instance, in its slot. PropLoader.Build owns the build sequence and its
    /// try/catch - a bad prop must never throw into a battle, and a half-built GameObject has to be
    /// destroyed while still in scope rather than leaking into the scene. All this adds is where the
    /// instance goes and the ring's bookkeeping about it.</summary>
    [HideFromIl2Cpp]
    private Slot Spawn(PropEntry entry, int index, int count)
    {
        // Born where it belongs rather than at the prefab's origin: PropLoader.Build activates the
        // instance last, and a world-space particle system emits its first frame from wherever it
        // stands when that happens.
        PropSpec.SlotPosition(entry, index, count, Time.time, out double x, out double y, out double z);

        var obj = PropLoader.Build(AppearanceID, entry, EffectRoot(entry.front),
                                   new Vector3((float)x, (float)y, (float)z),
                                   out var art, out var renderer);

        return obj == null
            ? null
            : new Slot { Obj = obj, Renderer = renderer, Art = art, Born = Time.time };
    }

    private void RefreshCounts()
    {
        // One comparison per poll, against a token PropWorld already bumps from the round-start
        // hook it patches. A second Harmony patch for the same fact would be waste.
        bool roundTurned = _roundToken != PropWorld.RoundToken;
        _roundToken = PropWorld.RoundToken;

        foreach (var live in _live)
        {
            if (roundTurned && PropSpec.Is(live.Entry.parkUntil, "round"))
            {
                foreach (var slot in live.Slots) slot.Unpark();
            }

            if (live.IsTarget)
            {
                // Its own gate, read off each enemy rather than off the caster.
                live.TargetRings.Refresh(CasterModel());
                continue;
            }

            bool passes = true;
            int stack = 0;

            if (!string.IsNullOrEmpty(live.Entry.keyword))
                passes = GateStack(live.Entry, out stack);

            int want = PropSpec.TargetCount(live.Entry, passes, stack);

            if (live.IsWorld) RefreshWorldEntry(live, want);
            else RefreshRingSlots(live, want);
        }
    }

    /// <summary>A world-anchored entry is one instance or none, so its gate is a yes/no rather than
    /// a count: it is placed while the gate passes and removed the moment it stops.</summary>
    [HideFromIl2Cpp]
    private void RefreshWorldEntry(Live live, int want)
    {
        bool shouldExist = want > 0;

        if (shouldExist && live.WorldHandle == null && !live.Broken)
        {
            var pos = PropAnchors.Vec3(live.Entry.pos);

            // Caster-relative unless the author asked for absolute scene coordinates.
            if (!live.Entry.world) pos += transform.position;

            // Rounds is 0 here: a gated prop lives exactly as long as its gate passes, so letting
            // the round counter also kill it would give it two owners.
            live.WorldHandle = PropWorld.Place(AppearanceID, live.Entry, pos, 0,
                                               out bool ceilingHit, gated: true);

            // Art or prefab missing (a real failure, already logged by PropWorld/PropLoader) stops
            // this rig retrying every poll - same flag and reasoning as the grow loop in
            // RefreshRingSlots. A ceiling hit is transient: space frees as planted instances expire,
            // so it must not permanently suppress an entry that merely lost a race for slots.
            if (live.WorldHandle == null && !ceilingHit) live.Broken = true;
        }
        else if (!shouldExist && live.WorldHandle != null)
        {
            PropWorld.Remove(live.WorldHandle);
            live.WorldHandle = null;
        }
    }

    /// <summary>Grows or shrinks a unit-anchored entry's ring to <paramref name="want"/> instances.
    /// </summary>
    [HideFromIl2Cpp]
    private void RefreshRingSlots(Live live, int want)
    {
        // A slot whose GameObject was destroyed from outside - the effect root it hangs off being
        // rebuilt mid-battle - still counts towards the target, so without this the entry sits one
        // instance short for the rest of the battle. PropWorld.Tick self-heals too.
        live.Slots.RemoveAll(s => s.Obj == null);

        // A consume: "gate" instance comes back when, and only when, the gate's answer moves. On a
        // keyword entry that is the stack changing; on a fixed-count entry with no keyword it never
        // moves, which is the point of that mode.
        if (want != live.LastWant)
        {
            live.LastWant = want;
            foreach (var slot in live.Slots) slot.Consumed = false;
        }

        while (live.Slots.Count > want)
        {
            var doomed = live.Slots[live.Slots.Count - 1];
            live.Slots.RemoveAt(live.Slots.Count - 1);
            if (doomed.Obj != null) Destroy(doomed.Obj);
        }

        // A broken entry (missing art, or a throw from Spawn) still has to shrink to zero when its
        // gate drops - only spawning is suppressed, so "count follows the gate" stays true rather
        // than freezing at whatever count existed when it broke.
        while (!live.Broken && live.Slots.Count < want)
        {
            var slot = Spawn(live.Entry, live.Slots.Count, want);
            if (slot == null)
            {
                // Art or prefab missing, or Spawn threw. Marking Broken (not Entry) stops this rig
                // retrying every poll without touching the shared cache entry other characters are
                // still reading.
                live.Broken = true;
                break;
            }
            live.Slots.Add(slot);
        }
    }

    /// <summary>Works out, once for the whole entry, which strike actions are live this frame and
    /// where they are aiming. Everything here depends only on the action and the clock, so doing it
    /// inside the slot loop meant an enum parse, three interop calls and an ease per slot per frame
    /// - multiplied by party size, and Idle runs through the same clock.</summary>
    [HideFromIl2Cpp]
    private void ResolveStrikes(Live live, double fraction)
    {
        _strikeCount = 0;
        if (fraction < 0) return;

        var actions = live.Entry.actions;
        int ordinal = 0;

        for (int a = 0; a < actions.Length; a++)
        {
            var action = actions[a];
            if (!live.Kinds[a].MovesSlots) continue;
            if (!MotionMatches(live, a)) continue;

            // Counted for every matching strike, live or not: "next" hands the i-th matching action
            // the i-th slot, and dropping a finished action out of the count would reassign the
            // slots of every action written after it.
            int mine = ordinal++;

            if (fraction < action.start) continue;

            bool returning = fraction >= action.arrive && action.returnAt >= 0;

            // Done moving and not consumed: stop matching so the slot falls back to the cheap
            // ring path instead of re-resolving a target that has stopped changing.
            if (returning && fraction >= action.returnAt) continue;

            // A strike aimed at "slot" comes home, and home is per-slot - the slot loop resolves
            // it. Everything else resolves to a point the whole action shares.
            bool targetIsSlot = PropSpec.Is(action.to, "slot");

            // "enemies" deals one target per slot, so like "slot" it has no single point to hoist.
            // The target list is still resolved here: an action with nothing to aim at is dropped
            // once rather than by every slot for itself.
            bool spread = PropSpec.Is(action.to, "enemies");
            if (spread && _anchors.Count == 0) continue;

            Vector3 target = Vector3.zero;
            if (!targetIsSlot && !spread && !_anchors.StrikeTarget(action, out target)) continue;

            if (_strikeCount == _strikes.Count) _strikes.Add(new ActiveStrike());

            _strikes[_strikeCount++].Load(action, mine, fraction, _timebase, target,
                                          targetIsSlot, spread, returning, _anchors);
        }
    }

    /// <summary>Where a parked slot sits this frame. The park point is re-resolved from the action
    /// that parked it, so a formation left on an enemy tracks that enemy; when the anchor is gone -
    /// the target died - it falls back to the last point it resolved rather than the world origin.
    /// Cheap per slot: every anchor comes out of the frame's target list, so re-resolving a whole
    /// formation is arithmetic, not interop.
    /// <para>
    /// <paramref name="faceDegrees"/> is the rotation a parked slot wears if the strike that parked
    /// it used <c>face</c>. Only "ring" moves, so only "ring" re-aims, staying pointed at what it
    /// orbits; "hold" and "stack" keep the angle they landed with, making "hold" still and aimed.
    /// </para></summary>
    [HideFromIl2Cpp]
    private Vector3 ParkedPosition(Live live, Slot slot, int index, int count, float now,
                                   out double faceDegrees)
    {
        // The angle it arrived wearing, right for every mode that does not move.
        faceDegrees = slot.ParkDegrees;

        Vector3 centre = slot.ParkPoint;

        // Parked by a spread strike: this slot follows its own enemy rather than the action's
        // single shared point.
        if (slot.ParkTargetIndex >= 0)
        {
            if (slot.ParkTargetIndex < _anchors.Count)
            {
                centre = _anchors[slot.ParkTargetIndex] + PropAnchors.Vec3(slot.ParkAction.offset);
                slot.ParkPoint = centre;
            }
        }
        else
        {
            if (_anchors.StrikeTarget(slot.ParkAction, out Vector3 resolved)) centre = resolved;

            slot.ParkPoint = centre;
        }

        string mode = slot.ParkAction.park;

        if (PropSpec.Is(mode, "stack"))
            return centre;

        // "hold" freezes the formation at the moment it parked; "ring" keeps orbiting, around the
        // new centre. Both reuse the ring maths rather than approximating it.
        bool holds = PropSpec.Is(mode, "hold");
        double t = holds ? slot.ParkT : now;

        PropSpec.RingOffset(live.Entry, index, count, t, out double ox, out double oy,
                            live.Entry.parkRadius);

        // Orbiting and still aimed: the direction from the slot to what it circles is that offset
        // negated, so this is one Atan2 and no new state. Bob is already in the offset, so a
        // bobbing knife tips with it instead of drifting off the point.
        if (!holds) faceDegrees = PropSpec.FaceDegrees(-ox, -oy);

        return centre + new Vector3((float)ox, (float)oy, 0f);
    }

    [HideFromIl2Cpp]
    private void Place(Live live, float now, double fraction)
    {
        if (live.IsTarget) { live.TargetRings.Tick(now); return; }
        if (live.IsWorld) return;

        int count = live.Slots.Count;
        if (count == 0) return;

        ResolveStrikes(live, fraction);

        for (int i = 0; i < count; i++)
        {
            var slot = live.Slots[i];
            if (slot.Obj == null) continue;

            PropSpec.SlotPosition(live.Entry, i, count, now, out double x, out double y, out double z);
            var home = new Vector3((float)x, (float)y, (float)z);

            var moved = ApplyStrikes(live, slot, i, count, home, now);

            bool hidden = moved.Hidden;
            double spinDegrees = moved.SpinDegrees;

            // A gate-consumed slot stays where it fell out of the ring and stays hidden; putting
            // it back on the ring would draw the eye to an invisible object orbiting the character.
            if (slot.Consumed) hidden = true;
            else if (moved.Struck) slot.Obj.transform.position = moved.At;
            else if (slot.ParkAction != null)
            {
                slot.Obj.transform.position =
                    ParkedPosition(live, slot, i, count, now, out double parkedFace);

                // Only a strike that asked to lead along its travel keeps aiming once landed.
                // Without face a parked prop follows the entry's ambient spin, 0 when it has none.
                if (slot.ParkFaced) spinDegrees = parkedFace;
            }
            else slot.Obj.transform.localPosition = home;

            if (slot.Hidden != hidden)
            {
                // The whole object, not Renderer.enabled: a prefab-based prop has no
                // SpriteRenderer of ours to switch off, so returnAt: -1 never hid it.
                slot.Hidden = hidden;
                slot.Obj.SetActive(!hidden);
            }

            // Nothing below is visible on a hidden object, and a consume: "gate" instance stays
            // hidden for the rest of the battle - a whole ring's rotation and sprite writes per
            // frame, for nobody. The transform keeps what it held; writes resume when it comes back.
            if (hidden) continue;

            // Position above is world space, rotation here is local. That is deliberate: a strike
            // aims at a world point, but spin is about the prop's own axis and the effect root it
            // hangs off carries no rotation worth inheriting. Don't "fix" one half of this.
            //
            // Written unconditionally. Guarding it on "spin != 0 || struck" froze a prop that had
            // spun through an action's spin at whatever angle it held when the action ended, for
            // the rest of the battle - an entry with no ambient spin never wrote rotation again to
            // undo it. spinDegrees is entry.spin * now, 0 for such an entry, so writing every frame
            // is what returns the prop to its original facing when its action finishes.
            slot.Obj.transform.localRotation = Quaternion.Euler(0f, 0f, (float)spinDegrees);

            PropLoader.Advance(slot.Art, slot.Renderer, now - slot.Born, ref slot.Frame);
        }
    }

    /// <summary>What this frame's strikes did to one slot. <see cref="Struck"/> false means no live
    /// strike owned it, and the other fields carry the resting values the caller falls back to.</summary>
    private struct SlotMotion
    {
        public bool Struck;
        public bool Hidden;
        public double SpinDegrees;
        public Vector3 At;
    }

    /// <summary>Runs every live strike that owns slot <paramref name="index"/>. Last owner wins
    /// outright - position, hidden and spin together: latching hidden across actions left a
    /// consume-then-return pair lerping an invisible prop.</summary>
    [HideFromIl2Cpp]
    private SlotMotion ApplyStrikes(Live live, Slot slot, int index, int count, Vector3 home, float now)
    {
        var moved = new SlotMotion
        {
            // The entry's ambient spin, 0 for an entry that has none - which is also what returns an
            // unspun prop to its original facing. A live strike overrides it below, as does a parked
            // slot that landed with face.
            SpinDegrees = live.Entry.spin * now
        };

        // Resolved lazily, at most once per slot: TransformPoint is an interop call, and most slots
        // aren't owned by any live strike on a given frame - a 16-slot ring with one "next" strike
        // has no business paying for 16 of these.
        bool haveHome = false;
        Vector3 worldHome = default;
        Vector3 slotHome = default;

        for (int s = 0; s < _strikeCount; s++)
        {
            var strike = _strikes[s];
            if (!strike.OwnsSlot(index, count)) continue;

            // A parking strike hands the slot over once parked; from then on the parked branch owns
            // the position. Without this the strike keeps pinning the prop to the exact target while
            // the formation tries to orbit it.
            if (strike.ParkMode != null && slot.ParkAction != null) continue;

            if (!haveHome)
            {
                var parent = slot.Obj.transform.parent;
                worldHome = parent != null ? parent.TransformPoint(home) : home;

                // A parked prop launches from where it stands, not from the ring slot it left - that
                // is what lets one motion move it out and a later one throw it from there. Resolved
                // from the park, never read off the transform: Place writes that transform, so
                // reading it would feed each frame's position into the next frame's launch point -
                // flight collapses into a few frames and a return leg pins the prop to its target
                // instead of lerping home. Same call the unstruck parked branch makes.
                slotHome = slot.ParkAction != null
                    ? ParkedPosition(live, slot, index, count, now, out _)
                    : worldHome;
                haveHome = true;
            }

            // "slot" launches from wherever this slot is parked, which is why the origin is resolved
            // here rather than with the rest of the strike.
            Vector3 origin = strike.HasOrigin ? strike.Origin : slotHome;

            // A recall aims at the ring slot itself, and a spread deals this slot its own enemy -
            // both are things only this per-slot pass knows.
            Vector3 strikeTarget = strike.TargetIsSlot ? worldHome
                : strike.Spread ? _anchors[index % _anchors.Count] + strike.Offset
                : strike.Target;

            moved.Struck = true;
            moved.At = strike.PositionAt(origin, strikeTarget, slotHome);
            moved.Hidden = strike.Hidden;
            moved.SpinDegrees = strike.HasSpin ? strike.SpinDegrees : live.Entry.spin * now;

            if (strike.HasFace)
                moved.SpinDegrees = strike.FaceDegrees(origin, strikeTarget, slotHome);

            if (strike.ConsumeToGate) slot.Consumed = true;
            if (strike.Arrived) Arrive(strike, slot, index, now, moved.SpinDegrees, strikeTarget);
        }

        return moved;
    }

    /// <summary>What reaching the target does to the slot: park there, or end a park by coming
    /// home. Any other target leaves an existing park alone, so an ordinary strike borrows a parked
    /// prop instead of stranding it.</summary>
    [HideFromIl2Cpp]
    private void Arrive(ActiveStrike strike, Slot slot, int index, float now, double spinDegrees,
                        Vector3 strikeTarget)
    {
        if (strike.ParkMode == null)
        {
            if (strike.TargetIsSlot) slot.Unpark();
            return;
        }

        slot.ParkAction = strike.Action;
        slot.ParkPoint = strikeTarget;
        slot.ParkTargetIndex = strike.Spread ? index % _anchors.Count : -1;
        // Frozen here so "hold" keeps the angle it arrived wearing, not one picked when the
        // formation is first drawn.
        slot.ParkT = now;
        slot.ParkFaced = strike.HasFace;
        slot.ParkDegrees = spinDegrees;
    }

    /// <summary>Fires every one-shot action whose time was crossed since the previous frame. What
    /// each kind actually does lives with the kind - this loop only decides whose turn it is.</summary>
    [HideFromIl2Cpp]
    private void TickOneShots(Live live, double fraction, bool motionRestarted)
    {
        var actions = live.Entry.actions;
        double previous = motionRestarted ? -1.0 : _lastFraction;

        for (int a = 0; a < actions.Length; a++)
        {
            var action = actions[a];
            if (!live.Kinds[a].OneShot) continue;
            if (!MotionMatches(live, a)) continue;

            if (live.Kinds[a] is PlantKind && PropPlant.DueThisFrame(action, fraction, previous))
                PropPlant.Fire(AppearanceID, live.Entry, action, _anchors);
        }
    }

    /// <summary>A throw out of an injected Update costs more than one prop: it aborts the rest of
    /// this frame's loop, so every other entry on the rig stops too, and it repeats every frame for
    /// the rest of the battle. Log once, then stay quiet - same shape as Live.Broken.</summary>
    void Update()
    {
        if (_broken) return;

        try { Step(); }
        catch (Exception ex)
        {
            _broken = true;
            Logger.LogError($"[Props] {AppearanceID}: rig threw, its props stop updating " +
                            $"for the rest of this battle: {ex}");
        }
    }

    private void Step()
    {
        if (!_initialized) Initialize();
        if (_live.Count == 0) return;

        float now = Time.time;

        if (now >= _nextPoll)
        {
            _nextPoll = now + PollInterval;
            RefreshCounts();
        }

        double fraction = -1.0;
        _timebase = 0.0;
        if (Sync != null && Sync.IsModdedSkillActive && Sync.SlaveDirector != null)
        {
            // Resolved once per frame: ActionTimebase reads the master's asset through interop.
            _timebase = Sync.ActionTimebase();

            // No wrapping. Measured in game against a looping Idle (base 1.68s): the director under
            // DirectorWrapMode.Loop resets its own time every pass, so this fraction comes back
            // inside 0..1 and motionRestarted trips on each wrap. A "fraction never wraps" review
            // finding was checked against the running game and did not hold.
            if (_timebase > 0) fraction = Sync.SlaveDirector.time / _timebase;
        }

        // A different motion, a different coin, or a rewound clock all mean "start over", or a
        // plant authored early in a motion would never fire again after the first play.
        bool motionRestarted = Sync == null
                               || Sync.CurrentMotion != _lastMotion
                               || Sync.CurrentCoin != _lastCoin
                               || fraction < _lastFraction;

        foreach (var live in _live)
        {
            Place(live, now, fraction);
            if (fraction >= 0) TickOneShots(live, fraction, motionRestarted);
        }

        if (Sync != null)
        {
            _lastMotion = Sync.CurrentMotion;
            _lastCoin = Sync.CurrentCoin;
        }
        _lastFraction = fraction;
    }

    void OnDestroy()
    {
        foreach (var live in _live)
        {
            foreach (var slot in live.Slots)
                if (slot.Obj != null) Destroy(slot.Obj);

            if (live.WorldHandle != null) PropWorld.Remove(live.WorldHandle);
            if (live.TargetRings != null) live.TargetRings.Clear();
        }

        _live.Clear();
    }
}
