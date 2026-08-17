using System;
using System.Collections.Generic;
using Lethe.Patches;
using UnityEngine;

namespace Motions;

/// <summary>
/// Rings that hang off enemies instead of off the caster: one ring per unit carrying the entry's
/// keyword, sized by that unit's own stack of it. One of these exists per (rig, entry).
/// <para>
/// This is the answer to "leave a set of knives on everything I have debuffed"; the unit-anchored
/// park is not, since an entry owns a fixed pool of instances and parking the same three on a second
/// enemy takes them off the first. Here the props are a pure function of who is standing there and
/// what they carry - nothing is moved or cleaned up by hand, the buff expiring removes them.
/// </para>
/// </summary>
internal sealed class PropTargetRings
{
    /// <summary>One prop. Smaller than PropRig.Slot on purpose: a target ring has no strikes, so
    /// none of the park, consume or hidden state applies.</summary>
    private sealed class Instance
    {
        public GameObject Obj;
        public SpriteRenderer Renderer;
        public SpriteMotion Art;
        public float Born;
        /// <summary>Last art frame written, so a 12fps prop stops re-assigning its sprite sixty
        /// times a second. Same mirror-what-you-wrote trick as PropRig's Slot.Hidden.</summary>
        public int Frame = -1;
    }

    private sealed class Ring
    {
        /// <summary>The unit's Il2Cpp pointer, not the wrapper. Two managed wrappers for one
        /// native object are not reference-equal, so wrapper identity would spawn a fresh ring
        /// every poll and leak the old one.</summary>
        public IntPtr Key;
        public Transform Anchor;
        public List<Instance> Instances = new();
    }

    private readonly string _appearanceID;
    private readonly PropEntry _entry;
    private readonly List<Ring> _rings = new();
    private readonly HashSet<IntPtr> _seen = new();

    /// <summary>Set when spawning failed once. Same reasoning as PropRig's per-entry flag: retrying
    /// missing art ten times a second for the rest of a battle helps nobody.</summary>
    private bool _broken;
    private bool _announced;

    /// <summary>
    /// Bumped whenever the game announces a buff on a unit, or a round starts. Rings follow this and
    /// not the buff model, and the difference is a whole turn: Limbus resolves every buff the instant
    /// commands are confirmed and only replays it as animation later, so a model poll puts knives on
    /// an enemy seconds before the attack that sank them. ViewAbilityTypo is the moment the buff is
    /// shown, in animation time, and is the signal CharVFXParse's buff-gated VFX already ride.
    /// </summary>
    public static int ViewToken;
    private int _token = -1;

    /// <summary>
    /// Every faction value the game defines, resolved once. Deliberately not naming members: the
    /// caster's faction is read off the caster and everything else is a target, so this keeps
    /// working whatever the enum calls its sides.
    /// </summary>
    private static UNIT_FACTION[] _factions;

    public PropTargetRings(string appearanceID, PropEntry entry)
    {
        _appearanceID = appearanceID;
        _entry = entry;
    }

    /// <summary>
    /// Reads <paramref name="entry"/>'s keyword off one unit. Mirrors CharVFXParse's rule that a buff
    /// below either threshold fails outright rather than counting partially. Static and unit-taking
    /// because PropRig asks the same question about the caster; only whose buffs are read differs.
    /// </summary>
    public static bool GateStack(BattleUnitModel unit, PropEntry entry, out int stack)
    {
        stack = 0;
        if (unit == null) return false;

        // No keyword is "always on", and for a target ring that means every living enemy.
        if (string.IsNullOrEmpty(entry.keyword)) return true;

        if (unit._buffDetail == null) return false;

        var keyword = CustomBuffs.ParseBuffUniqueKeyword(entry.keyword);

        foreach (var buff in unit._buffDetail.GetBuffInfoAll(0))
        {
            if (!buff.IsKeyword(keyword)) continue;
            if (buff._stack < entry.stackThres || buff._turn < entry.turnThres) return false;

            stack = buff._stack;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Brings the rings in line with who is alive and carrying the keyword. Called from the rig's
    /// existing 10 Hz poll, not per frame: a buff cannot change visibly between two frames, and this
    /// walks every unit in the battle.
    /// </summary>
    public void Refresh(BattleUnitModel caster)
    {
        if (caster == null) { Clear(); return; }

        // A ring whose unit is gone goes now, whatever the view is doing. Waiting for the next
        // buff popup to notice a corpse would leave knives orbiting one.
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            if (_rings[i].Anchor != null) continue;

            DestroyRing(_rings[i]);
            _rings.RemoveAt(i);
        }

        // Everything else waits for the view to catch up with the model. See ViewToken.
        if (_token == ViewToken) return;
        _token = ViewToken;

        // The one authoring mistake that is otherwise silent: a keyword the game does not know
        // rings nobody, forever, and looks exactly like a buff nobody has.
        if (!_announced && !string.IsNullOrEmpty(_entry.keyword))
        {
            _announced = true;

            if ((int)CustomBuffs.ParseBuffUniqueKeyword(_entry.keyword) == 0)
                Logger.LogWarning($"[Props] {_appearanceID}: keyword '{_entry.keyword}' is not a " +
                                  $"buff this game knows - that target entry will never appear.");
        }

        var manager = BattleObjectManager.Instance;
        if (manager == null) { Clear(); return; }

        _factions ??= (UNIT_FACTION[])Enum.GetValues(typeof(UNIT_FACTION));
        var mine = caster._faction;

        _seen.Clear();

        // Every side that is not the caster's - see _factions. Leaves the caster's party out free.
        foreach (var faction in _factions)
        {
            // == and not Equals: Enum has no strongly-typed Equals, so the latter boxes both sides.
            if (faction == mine) continue;

            var alive = manager.GetAliveList(false, faction);
            if (alive == null) continue;

            for (int i = 0; i < alive.Count; i++)
            {
                var unit = alive[i];
                if (unit == null) continue;
                if (!GateStack(unit, _entry, out int stack)) continue;

                // Same clamp the caster's ring uses, so maxCount and the hard 16 mean the same
                // thing here - except per enemy, the only reading that makes sense when the count
                // comes from that enemy's own stack.
                int want = PropSpec.TargetCount(_entry, true, stack);
                if (want <= 0) continue;

                var ring = Resolve(manager, unit);
                if (ring == null) continue;

                _seen.Add(ring.Key);
                Resize(ring, want);
            }
        }

        // Anything not seen this poll is a unit that died, lost the buff, or left the field.
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            if (_seen.Contains(_rings[i].Key)) continue;

            DestroyRing(_rings[i]);
            _rings.RemoveAt(i);
        }
    }

    /// <summary>Finds this unit's ring, or starts one. Re-resolves the anchor every time: a view
    /// rebuilt mid-battle leaves the old transform fake-null and the props orbiting nothing.</summary>
    private Ring Resolve(BattleObjectManager manager, BattleUnitModel unit)
    {
        var view = manager.GetView(unit);
        Transform anchor = view != null ? view.transform : null;
        if (anchor == null) return null;

        IntPtr key = unit.Pointer;

        for (int i = 0; i < _rings.Count; i++)
        {
            if (_rings[i].Key != key) continue;

            _rings[i].Anchor = anchor;
            return _rings[i];
        }

        var ring = new Ring { Key = key, Anchor = anchor };
        _rings.Add(ring);
        return ring;
    }

    private void Resize(Ring ring, int want)
    {
        while (ring.Instances.Count > want)
        {
            var doomed = ring.Instances[ring.Instances.Count - 1];
            ring.Instances.RemoveAt(ring.Instances.Count - 1);
            if (doomed.Obj != null) UnityEngine.Object.Destroy(doomed.Obj);
        }

        // Destroyed from outside - the same self-heal RefreshCounts and PropWorld.Tick do.
        ring.Instances.RemoveAll(x => x.Obj == null);

        while (!_broken && ring.Instances.Count < want)
        {
            var made = Spawn(ring, ring.Instances.Count, want);
            if (made == null) { _broken = true; break; }

            ring.Instances.Add(made);
        }
    }

    /// <summary>
    /// Builds one prop in world space, where its ring will put it. Unparented on purpose: a ring is
    /// repositioned against its unit's transform every frame anyway, and parenting to another unit's
    /// view would hand that unit's scale, rotation and destruction to props the caster owns.
    /// </summary>
    private Instance Spawn(Ring ring, int index, int count)
    {
        PropSpec.SlotPosition(_entry, index, count, Time.time, out double x, out double y, out double z);

        var obj = PropLoader.Build(_appearanceID, _entry, null,
                                   ring.Anchor.position + new Vector3((float)x, (float)y, (float)z),
                                   out var art, out var renderer);

        return obj == null
            ? null
            : new Instance { Obj = obj, Renderer = renderer, Art = art, Born = Time.time };
    }

    /// <summary>Positions, rotates and animates every ring. Runs inside PropRig.Step, so a throw
    /// here is caught by the rig's own latch rather than repeating every frame.</summary>
    public void Tick(float now)
    {
        for (int r = 0; r < _rings.Count; r++)
        {
            var ring = _rings[r];
            if (ring.Anchor == null) continue;

            // One interop read per ring rather than per instance.
            Vector3 anchor = ring.Anchor.position;
            int count = ring.Instances.Count;

            for (int i = 0; i < count; i++)
            {
                var instance = ring.Instances[i];
                if (instance.Obj == null) continue;

                // Through SlotPosition, not RingOffset plus a hand-added centre: entry.pos means
                // "the ring's centre" in exactly one place, and this is not that place.
                PropSpec.SlotPosition(_entry, i, count, now, out double x, out double y, out double z);
                instance.Obj.transform.position = anchor + new Vector3((float)x, (float)y, (float)z);

                // Menacing rather than merely present: the direction from the prop to what it
                // circles is its ring offset negated. Same rule a "ring"-parked prop follows, and
                // the offset is the slot position less the centre entry.pos put it around.
                double degrees = _entry.face
                    ? PropSpec.FaceDegrees(_entry.pos[0] - x, _entry.pos[1] - y)
                    : _entry.spin * now;

                instance.Obj.transform.localRotation = Quaternion.Euler(0f, 0f, (float)degrees);

                PropLoader.Advance(instance.Art, instance.Renderer, now - instance.Born,
                                   ref instance.Frame);
            }
        }
    }

    public void Clear()
    {
        for (int i = 0; i < _rings.Count; i++) DestroyRing(_rings[i]);
        _rings.Clear();
    }

    private static void DestroyRing(Ring ring)
    {
        for (int i = 0; i < ring.Instances.Count; i++)
            if (ring.Instances[i].Obj != null) UnityEngine.Object.Destroy(ring.Instances[i].Obj);

        ring.Instances.Clear();
    }
}
