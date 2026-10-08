namespace DontStarveRuneScape.Render;

/// <summary>
/// Stepping pattern shared by all feet of one gait rig.
/// </summary>
public enum GaitPattern
{
    /// <summary>Two feet, strict alternation: a foot swings only while the other is planted.</summary>
    Biped,

    /// <summary>Four feet in diagonal pairs (FL+RR together, then FR+RL), like a real walk gait.
    /// Foot order in <see cref="GaitConfig.FootOffsets"/> must be [front-left, rear-right, front-right, rear-left].</summary>
    QuadrupedWalk,
}

/// <summary>
/// Layout and tunables for one gait rig: how many feet, where they sit
/// relative to the body's ground point, and how they step. All offsets are
/// world px and rotate with the travel direction (lateral = left-perpendicular,
/// longitudinal = along travel).
/// </summary>
public sealed class GaitConfig
{
    public GaitPattern Pattern { get; init; } = GaitPattern.Biped;

    /// <summary>Per-foot (lateral, longitudinal) stance offsets from the body
    /// ground point, world px. Index order follows <see cref="GaitPattern"/>.</summary>
    public (float Lat, float Long)[] FootOffsets { get; init; } = [(-3f, 0f), (3f, 0f)];

    /// <summary>Seconds per step (one foot's swing).</summary>
    public float SwingDur { get; init; } = 0.1f;

    /// <summary>Peak swing lift, screen px.</summary>
    public float LiftPx { get; init; } = 4f;

    /// <summary>Idle replant threshold, world px: a planted foot that drifts
    /// this far off its stance replants (terrain contouring while idle).</summary>
    public float TriggerDist { get; init; } = 3f;

    /// <summary>halfStride floor, world px: keeps low speeds visibly stepping.</summary>
    public float MinStride { get; init; } = 3f;

    /// <summary>Base color of the dome boots/paws; the dome shader shades it
    /// per-vertex against the fixed light. Default is the player's boot
    /// leather tone.</summary>
    public (byte R, byte G, byte B) DomeColor { get; init; } = (139, 90, 43);

    /// <summary>Dome-boot dimensions (world px) for the projected spherical
    /// dome feet — the sized-to-entity counterpart of the legacy flat foot
    /// quad. Default is the player boot: round footprint, slightly longer at
    /// the toe than the heel, about a hemisphere tall, scaled up ~20% from
    /// the original (2.2/3.0 footprint) on user request 2026-10-04.</summary>
    public FootDomeDims DomeBoots { get; init; } = new(
        toe: 3.6f, heel: 2.65f, halfWidth: 2.9f, height: 3.85f);
}

/// <summary>
/// Reusable planted/swing stepping state machine for any number of feet —
/// the same gait the player's boots use, generalized by a <see cref="GaitConfig"/>.
/// A planted foot holds a world position until the moving body leaves it
/// halfStride behind along the travel direction, then swings to a landing
/// point ahead (halfStride + velocity lead), arcs up, and plants there.
/// Feet in the same phase group swing together; groups alternate.
/// Drives purely from velocity: call <see cref="Update"/> once per frame with
/// the entity's real motion, then read <see cref="GetFoot"/> for rendering.
/// </summary>
public sealed class GaitAnimator
{
    /// <summary>One foot of the stepping state machine.</summary>
    public struct Foot
    {
        public bool Placed;
        public bool Swinging;
        public float X, Y;                   // current world position
        public float FromX, FromY, ToX, ToY; // swing endpoints
        public float T;                      // swing progress 0..1
    }

    private readonly GaitConfig _cfg;
    private readonly Foot[] _feet;
    // Fallback stance direction while idle; seeds to north like the player's
    // default facing.
    private (float Dx, float Dy) _lastDir = (0f, -1f);
    // Moving state of the previous Update; its rising edge pre-phases the
    // feet into a mid-stride arrangement so a fresh walk starts stepping
    // immediately instead of stranding a boot far behind (see Update).
    private bool _wasMoving;

    /// <summary>Current stance/facing direction (unit): the travel direction
    /// while moving, the last travel direction while idle.</summary>
    public (float Dx, float Dy) Dir => _lastDir;

    /// <summary>Whether the stride cycle is currently stepping (body in motion).</summary>
    public bool Moving { get; private set; }

    public GaitAnimator(GaitConfig cfg)
    {
        _cfg = cfg;
        _feet = new Foot[cfg.FootOffsets.Length];
    }

    public int FootCount => _feet.Length;

    public ref Foot GetFoot(int index) => ref _feet[index];

    /// <summary>Phase group of a foot: feet in the same group swing together
    /// (diagonal pairs on a quadruped), groups alternate (biped = one group per foot).</summary>
    private int GroupOf(int index) => _cfg.Pattern switch
    {
        GaitPattern.Biped => index,
        GaitPattern.QuadrupedWalk => index / 2,
        _ => index,
    };

    /// <summary>Advance the whole gait one frame. <paramref name="velX"/>/Y are
    /// the entity's real net velocity (world px/s); the stance direction comes
    /// from them while moving and from the last travel direction while idle.
    /// <paramref name="stanceShift"/> offsets every stance laterally in world
    /// px (positive = the traveler's right): the player passes the turn-lean
    /// shift so feet plant toward the bank instead of under the old
    /// centerline. <paramref name="trail"/> offsets every stance BACKWARD
    /// along the travel direction in world px, so the boots settle slightly
    /// behind (under) the body — a subtle run-bike lean forward.</summary>
    public void Update(float originX, float originY, float velX, float velY, float dt,
        float stanceShift = 0f, float trail = 0f)
    {
        float speed = MathF.Sqrt(velX * velX + velY * velY);
        bool moving = speed > 1f;
        Moving = moving;
        (float dx, float dy) dir;
        if (moving)
        {
            dir = (velX / speed, velY / speed);
            _lastDir = dir;
        }
        else
        {
            dir = _lastDir;
        }
        // Left-perpendicular of the travel direction.
        float px = -dir.dy, py = dir.dx;
        // halfStride = speed × SwingDur × 0.5 makes the alternation gate
        // lag-free at constant velocity; the floor keeps low speeds stepping.
        float halfStride = MathF.Max(_cfg.MinStride, speed * _cfg.SwingDur * 0.5f);

        // Walk-start edge: idle replant leaves every foot parked on its
        // stance, so without this the first alternation gates one foot
        // through the other's whole swing — it strands halfStride +
        // speed×SwingDur behind (a full boot-throw of visible lag on every
        // stop-and-go). Pre-phase the planted feet into the mid-stride
        // arrangement instead: group 0 sits halfStride behind (due to lift
        // this frame), group 1 halfStride ahead (plants through). Applied in
        // the foot loop below so a rig's very first Update (feet still
        // unplaced) phases its fresh placements too.
        bool startEdge = moving && !_wasMoving;

        for (int i = 0; i < _feet.Length; i++)
        {
            ref Foot f = ref _feet[i];
            var (lat, lon) = _cfg.FootOffsets[i];
            // trail subtracts along the travel direction: stances settle a
            // touch behind the body's centerline while moving.
            float stanceX = originX + px * (lat + stanceShift) + dir.dx * (lon - trail * (moving ? 1f : 0f));
            float stanceY = originY + py * (lat + stanceShift) + dir.dy * (lon - trail * (moving ? 1f : 0f));
            if (!f.Placed)
            {
                f.X = stanceX; f.Y = stanceY;
                f.Placed = true;
            }
            if (startEdge && !f.Swinging)
            {
                // Covers freshly placed feet too: a rig born mid-walk would
                // otherwise park on its stance and strand a foot on the
                // first alternation.
                float phase = GroupOf(i) % 2 == 0 ? 1f : -1f;
                f.X = stanceX - dir.dx * phase * (halfStride + 0.5f);
                f.Y = stanceY - dir.dy * phase * (halfStride + 0.5f);
            }
            if (f.Swinging)
            {
                // Keep the landing point glued to the CURRENT stance: the
                // body keeps moving during the swing, so a landing computed
                // once at lift time goes stale whenever a frame spike
                // stretches the swing — the foot would plant short of its
                // stride and read as boots trailing the body (worst at low
                // frame rates, e.g. zoomed out). Refreshing the target every
                // frame makes the swing land halfStride ahead of wherever
                // the body actually is when the foot comes down. Idle
                // replant swings keep their stance target untouched.
                if (moving)
                {
                    f.ToX = stanceX + dir.dx * (halfStride + speed * (1f - f.T) * _cfg.SwingDur);
                    f.ToY = stanceY + dir.dy * (halfStride + speed * (1f - f.T) * _cfg.SwingDur);
                }
                UpdateSwing(ref f, dt);
                continue;
            }
            // Symmetric gait: lift once the foot has trailed halfStride behind
            // the stance along the travel direction — the same distance it will
            // land ahead of the stance — so feet swing through the stance
            // without a permanent behind-phase.
            float behind = (stanceX - f.X) * dir.dx + (stanceY - f.Y) * dir.dy;
            // Hard cap: never let a planted foot trail more than one full
            // swing's travel past due. The alternation gate below waits for
            // the other group's swing; without the cap a frame spike or a
            // direction change strands the foot for the whole wait and the
            // boot visibly drags behind the body.
            bool stranded = behind > halfStride + speed * _cfg.SwingDur;
            if (moving && behind > halfStride && (stranded || !GroupSwinging(GroupOf(i))))
            {
                // Land halfStride ahead of the stance plus the distance the
                // body covers during the swing, so the foot plants halfStride
                // ahead of the then-current stance at any constant velocity.
                f.FromX = f.X; f.FromY = f.Y;
                f.ToX = stanceX + dir.dx * (halfStride + speed * _cfg.SwingDur);
                f.ToY = stanceY + dir.dy * (halfStride + speed * _cfg.SwingDur);
                f.T = 0f;
                f.Swinging = true;
                // Phase mates (diagonal partners) lift together so the pair
                // stays rigid; a biped foot has no mates.
                SyncGroupMates(i, stanceX, stanceY, dir.dx, dir.dy, speed, dt);
            }
            else if (!moving)
            {
                // Idle replant: pull the foot back onto the stance when it has
                // drifted off it, contouring the terrain.
                float sdx = f.X - stanceX, sdy = f.Y - stanceY;
                if (sdx * sdx + sdy * sdy > _cfg.TriggerDist * _cfg.TriggerDist)
                {
                    f.FromX = f.X; f.FromY = f.Y;
                    f.ToX = stanceX; f.ToY = stanceY;
                    f.T = 0f;
                    f.Swinging = true;
                }
            }
        }
        _wasMoving = moving;
    }

    /// <summary>True when any foot of a different phase group is mid-swing —
    /// that group must plant before this foot's group may lift.</summary>
    private bool GroupSwinging(int group)
    {
        for (int i = 0; i < _feet.Length; i++)
        {
            if (GroupOf(i) != group && _feet[i].Swinging)
                return true;
        }
        return false;
    }

    /// <summary>Start the same swing on this foot's phase mates so pairs move
    /// together; each mate lands at its own stance plus the velocity lead.
    /// <paramref name="stanceX"/>/Y are the triggering foot's stance; mates
    /// derive their own stance from the difference of their foot offsets.</summary>
    private void SyncGroupMates(int index, float stanceX, float stanceY,
        float dirX, float dirY, float speed, float dt)
    {
        int group = GroupOf(index);
        var (lat, lon) = _cfg.FootOffsets[index];
        float lead = MathF.Max(_cfg.MinStride, speed * _cfg.SwingDur * 0.5f)
            + speed * _cfg.SwingDur;
        float px = -dirY, py = dirX;
        for (int i = 0; i < _feet.Length; i++)
        {
            if (i == index || GroupOf(i) != group || _feet[i].Swinging)
                continue;
            var (mLat, mLong) = _cfg.FootOffsets[i];
            // The mate's stance sits at the triggering stance plus the offset
            // difference, so both feet keep one shared stance origin.
            float mStanceX = stanceX + px * (mLat - lat) + dirX * (mLong - lon);
            float mStanceY = stanceY + py * (mLat - lat) + dirY * (mLong - lon);
            ref Foot f = ref _feet[i];
            // A mate not placed yet (rig's very first Update) parks at its
            // stance and swings from there, so the pair lifts together from
            // frame one instead of the mate planting alone.
            if (!f.Placed)
            {
                f.X = mStanceX; f.Y = mStanceY;
                f.Placed = true;
            }
            f.FromX = f.X; f.FromY = f.Y;
            f.ToX = mStanceX + dirX * lead;
            f.ToY = mStanceY + dirY * lead;
            // The sync runs mid-loop: a mate AFTER the trigger in foot order
            // still gets its branch's dt advance this frame (the trigger's
            // fresh T=0 swing does not), so seed it one step back; a mate
            // BEFORE the trigger has already run its branch, so seed 0. This
            // keeps the pair exactly in phase — without it the mate lands a
            // frame early or late depending on foot order.
            f.T = i > index ? -dt / _cfg.SwingDur : 0f;
            f.Swinging = true;
        }
    }

    /// <summary>Advance one foot that is mid-swing until it plants. No-op when
    /// the foot is planted — otherwise a fresh foot would "land" at (0,0).</summary>
    private void UpdateSwing(ref Foot f, float dt)
    {
        if (!f.Swinging) return;
        f.T += dt / _cfg.SwingDur;
        if (f.T >= 1f)
        {
            f.T = 1f;
            f.Swinging = false;
            f.X = f.ToX; f.Y = f.ToY;
        }
        else
        {
            f.X = f.FromX + (f.ToX - f.FromX) * f.T;
            f.Y = f.FromY + (f.ToY - f.FromY) * f.T;
        }
    }

    /// <summary>Pin every foot to its stance point under the body so feet
    /// dangle there (swimming / suspended gait), canceling any swing, and
    /// leaving the machine a sane position to resume from. The direction is
    /// the entity's current stance direction. <paramref name="lead"/> shifts
    /// every stance forward along that direction (world px) — the leap's
    /// landing reach: feet pinned ahead of the body plant first.</summary>
    public void PinToStance(float originX, float originY, float dirX, float dirY, float lead = 0f)
    {
        float px = -dirY, py = dirX;
        for (int i = 0; i < _feet.Length; i++)
        {
            var (lat, lon) = _cfg.FootOffsets[i];
            _feet[i].X = originX + px * lat + dirX * (lon + lead);
            _feet[i].Y = originY + py * lat + dirY * (lon + lead);
            _feet[i].Placed = true;
            _feet[i].Swinging = false;
        }
    }

    /// <summary>Kick/plant/launch tilt profile for a swinging foot, in the
    /// range [-1, +1]: −1 at lift (toe-off drag — the boot's top tips back,
    /// trailing edge down), easing through level (0) mid-swing, to +1 at
    /// plant (heel-strike — top tips forward, heel meets the ground first).
    /// Renderers shear the foot box by <c>tangent × height × curve</c> along
    /// the travel direction. Planted feet do not tilt (the terrain sets
    /// their angle); T from idle replant swings also tilts harmlessly.
    /// </summary>
    /// <param name="t">Swing progress in [0,1] (clamped). The seeded-out-of-
    /// range T on freshly synced diagonal mates is clamped here too.</param>
    public static float SwingTilt(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        // Smoothstep through the middle so the boot holds near each pose
        // instead of sweeping linearly: drag longer at liftoff, present the
        // heel earlier before plant.
        float s = t * t * (3f - 2f * t);
        return s * 2f - 1f;
    }
}
