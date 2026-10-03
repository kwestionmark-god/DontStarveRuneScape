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

    /// <summary>Foot quad size as a fraction of the entity's render half-size.</summary>
    public float FootSizeFrac { get; init; } = 4f / 32f;

    /// <summary>Sprite key of the foot quad (boots for humanoids, paws for
    /// quadrupeds).</summary>
    public string FootTextureKey { get; init; } = "player/boot";
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
    /// from them while moving and from the last travel direction while idle.</summary>
    public void Update(float originX, float originY, float velX, float velY, float dt)
    {
        float speed = MathF.Sqrt(velX * velX + velY * velY);
        bool moving = speed > 1f;
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

        for (int i = 0; i < _feet.Length; i++)
        {
            ref Foot f = ref _feet[i];
            var (lat, lon) = _cfg.FootOffsets[i];
            float stanceX = originX + px * lat + dir.dx * lon;
            float stanceY = originY + py * lat + dir.dy * lon;
            if (!f.Placed)
            {
                f.X = stanceX; f.Y = stanceY;
                f.Placed = true;
                continue;
            }
            if (f.Swinging)
            {
                UpdateSwing(ref f, dt);
                continue;
            }
            // Symmetric gait: lift once the foot has trailed halfStride behind
            // the stance along the travel direction — the same distance it will
            // land ahead of the stance — so feet swing through the stance
            // without a permanent behind-phase.
            float behind = (stanceX - f.X) * dir.dx + (stanceY - f.Y) * dir.dy;
            if (moving && behind > halfStride && !GroupSwinging(GroupOf(i)))
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
                SyncGroupMates(i, stanceX, stanceY, dir.dx, dir.dy, speed);
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
        float dirX, float dirY, float speed)
    {
        int group = GroupOf(index);
        var (lat, lon) = _cfg.FootOffsets[index];
        float lead = MathF.Max(_cfg.MinStride, speed * _cfg.SwingDur * 0.5f)
            + speed * _cfg.SwingDur;
        float px = -dirY, py = dirX;
        for (int i = 0; i < _feet.Length; i++)
        {
            if (i == index || GroupOf(i) != group || _feet[i].Swinging || !_feet[i].Placed)
                continue;
            var (mLat, mLong) = _cfg.FootOffsets[i];
            // The mate's stance sits at the triggering stance plus the offset
            // difference, so both feet keep one shared stance origin.
            float mStanceX = stanceX + px * (mLat - lat) + dirX * (mLong - lon);
            float mStanceY = stanceY + py * (mLat - lat) + dirY * (mLong - lon);
            ref Foot f = ref _feet[i];
            f.FromX = f.X; f.FromY = f.Y;
            f.ToX = mStanceX + dirX * lead;
            f.ToY = mStanceY + dirY * lead;
            f.T = 0f;
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
    /// the entity's current stance direction.</summary>
    public void PinToStance(float originX, float originY, float dirX, float dirY)
    {
        float px = -dirY, py = dirX;
        for (int i = 0; i < _feet.Length; i++)
        {
            var (lat, lon) = _cfg.FootOffsets[i];
            _feet[i].X = originX + px * lat + dirX * lon;
            _feet[i].Y = originY + py * lat + dirY * lon;
            _feet[i].Placed = true;
            _feet[i].Swinging = false;
        }
    }
}
