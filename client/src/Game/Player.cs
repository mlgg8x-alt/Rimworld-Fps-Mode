using System.Numerics;
using RimFps.Net;
using RimFps.World;

namespace RimFps.Game;

/// <summary>What the player asks for this frame, from the keyboard and mouse.</summary>
public struct Controls
{
    public float Forward, Right, Up;
    public float LookX, LookY;
    public bool Walk, Crouch, Fly, Sprint, Aim, Jump;
}

/// <summary>
/// The first-person player: the colonist RimWorld hands over (its <c>player_pawn</c>), or,
/// with nobody handed over, a free-flying camera that RimWorld's own camera follows.
///
/// Movement is a modern shooter's (run, sprint, crouch, aim, a small hop) but never faster than
/// the colonist runs in RimWorld: sprinting is its <c>MoveSpeed</c>, injuries and all, sped up
/// with the game, slowed by each cell's path cost as RimWorld's pawns are, and held back where
/// they could not go. Every
/// tenth of a second the position goes to RimWorld, which walks the colonist there; if
/// RimWorld holds it back (a wall, a door it can not open), the player is put back on it.
/// </summary>
public sealed class Player
{
    private const float MoveSeconds = 0.1f, Snap = 3f, SentMemory = 1.5f, MaxTicksPerCell = 450f;
    private const float FlySpeed = 10f, FlyFast = 3f;

    public float X, Z;
    /// <summary>Degrees clockwise from north, and up from level.</summary>
    public float Yaw, Pitch;
    public float EyeHeight = Dims.Eye;
    public float FlyY = 6f;
    public Vector2 Velocity;
    /// <summary>The colonist played, by RimWorld's id; null while flying free.</summary>
    public string? Playing;
    public bool Crouching, Sprinting;
    /// <summary>How high off the ground a hop has the player, and how fast it rises.</summary>
    public float JumpY, JumpV;
    /// <summary>Where recoil has pushed the view, degrees up and right; it settles back.</summary>
    public float RecoilPitch, RecoilYaw;
    /// <summary>0 to 1: how fast the player moves of their top speed, for the weapon's bob.</summary>
    public float Moving;
    public float BobPhase;
    public float Speed { get; private set; }

    private float sinceMove, sinceSpectate;
    private readonly List<(float age, Vector2 at)> sent = new();

    public bool Flying => Playing == null;
    public float EyeY => Flying ? FlyY : EyeHeight + JumpY;
    /// <summary>The way the view looks with recoil on top of the player's own aim.</summary>
    public float ViewPitch => Math.Clamp(Pitch + RecoilPitch, -89f, 89f);
    public float ViewYaw => Yaw + RecoilYaw;
    public Vector3 Eye => WorldMesh.P(X, EyeY, Z);

    /// <summary>Which way the view looks, in the world.</summary>
    public Vector3 Forward
    {
        get
        {
            float y = ViewYaw * MathF.PI / 180f, p = ViewPitch * MathF.PI / 180f;
            return new Vector3(MathF.Sin(y) * MathF.Cos(p), MathF.Sin(p), -MathF.Cos(y) * MathF.Cos(p));
        }
    }

    /// <summary>The view's right along the ground, in the world.</summary>
    public Vector3 Right
    {
        get
        {
            float y = ViewYaw * MathF.PI / 180f;
            return new Vector3(MathF.Cos(y), 0, MathF.Sin(y));
        }
    }

    public void Update(float dt, Controls c, Link link, Grid grid, Action<string> send, Settings settings, bool frozen)
    {
        // Looking is always free, even with RimWorld paused; slower while aiming.
        if (!frozen)
        {
            float sens = settings.Sensitivity * (c.Aim ? settings.AdsSensitivity * AimSlow : 1f);
            Yaw = ((Yaw + c.LookX * sens) % 360f + 360f) % 360f;
            Pitch = Math.Clamp(Pitch - c.LookY * sens * (settings.InvertY ? -1 : 1), -85f, 85f);
        }
        // Recoil settles back.
        float settle = 1f - MathF.Exp(-dt * 9f);
        RecoilPitch -= RecoilPitch * settle;
        RecoilYaw -= RecoilYaw * settle;
        PawnState? pawn = link.Player;
        if (pawn == null || !grid.Ready)
        {
            if (Playing != null) FlyY = Math.Max(EyeHeight + 3f, 4f);
            Playing = null;
            sent.Clear();
            Fly(dt, c, frozen || link.Paused, grid);
            sinceSpectate += dt;
            if (sinceSpectate >= MoveSeconds && link.Connected)
            {
                sinceSpectate = 0;
                send(Out.Spectate(X, Z));
            }
            return;
        }
        bool starting = Playing != pawn.Id;
        foreach (int i in Enumerable.Range(0, sent.Count)) sent[i] = (sent[i].age + dt, sent[i].at);
        sent.RemoveAll(s => s.age > SentMemory);
        if (starting || HeldBack(pawn))
        {
            sent.Clear();
            X = pawn.X;
            Z = pawn.Z;
            Velocity = Vector2.Zero;
            if (starting)
            {
                Playing = pawn.Id;
                Yaw = pawn.Rot;
                Pitch = 0;
                sinceMove = 0;
                Console.WriteLine($"[fps] playing {pawn.Name ?? pawn.Id}");
            }
            return;
        }
        Walk(dt, c, link, grid, frozen || link.Paused);
        sinceMove += dt;
        if (sinceMove >= MoveSeconds)
        {
            sinceMove = 0;
            sent.Add((0, new Vector2(X, Z)));
            send(Out.PlayerMove(pawn.Id, X, Z, Yaw));
        }
    }

    /// <summary>Whether RimWorld has held the colonist back from where the player went: far from
    /// the player and from everywhere the player was lately. RimWorld's snapshots trail the
    /// player, more so at speed, so trailing behind is no reason to put them back.</summary>
    private bool HeldBack(PawnState pawn)
    {
        bool Far(Vector2 at) => MathF.Sqrt((at.X - pawn.X) * (at.X - pawn.X) + (at.Y - pawn.Z) * (at.Y - pawn.Z)) > Snap;
        return Far(new Vector2(X, Z)) && sent.All(s => Far(s.at));
    }

    /// <summary>How much slower the mouse is while aiming, set by the zoom.</summary>
    public float AimSlow = 1f;

    /// <summary>A shot's kick: the view jumps up (and a little sideways), then settles.</summary>
    public void Kick(float up, float side)
    {
        RecoilPitch = Math.Min(RecoilPitch + up, 12f);
        RecoilYaw = Math.Clamp(RecoilYaw + side, -6f, 6f);
    }

    private void Walk(float dt, Controls c, Link link, Grid grid, bool held)
    {
        Crouching = c.Crouch;
        Sprinting = c.Sprint && c.Forward > 0 && !c.Aim && !Crouching && !held;
        // A small hop: RimWorld has no jumping, so it changes nothing there, only what
        // bullets meet here.
        if (c.Jump && JumpY <= 0f && !Crouching && !held) JumpV = 3.3f;
        if (JumpY > 0f || JumpV > 0f)
        {
            JumpV -= 11f * dt;
            JumpY = Math.Max(0f, JumpY + JumpV * dt);
            if (JumpY <= 0f) JumpV = 0f;
        }
        float wantEye = Crouching ? Dims.CrouchEye : Dims.Eye;
        EyeHeight += (wantEye - EyeHeight) * Math.Min(1f, dt * 12f);
        Vector2 wish = Vector2.Zero;
        if (!held)
        {
            float y = Yaw * MathF.PI / 180f;
            var forward = new Vector2(MathF.Sin(y), MathF.Cos(y));
            var right = new Vector2(MathF.Cos(y), -MathF.Sin(y));
            wish = forward * c.Forward + right * c.Right;
            if (wish.LengthSquared() > 1f) wish = Vector2.Normalize(wish);
        }
        Speed = TopSpeed(link, grid);
        // Sprinting is the colonist's full speed; running, aiming and crouching slower.
        float speed = Speed * (Sprinting ? 1f : 0.72f) * (c.Aim ? 0.6f : 1f) * (Crouching ? 0.5f : 1f) * (c.Walk && !c.Sprint ? 0.5f : 1f);
        Vector2 target = wish * speed;
        // Quick to start and to stop, with a little slide: a 90s shooter's feet.
        float grip = 1f - MathF.Exp(-dt * (wish == Vector2.Zero ? 14f : 10f));
        Velocity += (target - Velocity) * grip;
        if (held) Velocity = Vector2.Zero;
        Move(Velocity * dt, grid);
        Moving = Speed > 0 ? Math.Clamp(Velocity.Length() / Speed, 0, 1) : 0;
        BobPhase += Velocity.Length() * dt * 2.2f;
    }

    /// <summary>How fast the colonist runs here, in cells a real second: RimWorld's MoveSpeed
    /// at the game's speed, slowed by the cell's path cost as RimWorld adds it to a pawn's
    /// ticks a cell (at most 450).</summary>
    private float TopSpeed(Link link, Grid grid)
    {
        float speed = Math.Max(0.1f, link.Snap?.PlayerSpeed ?? 4.6f);
        float rate = link.Rate;
        float ticksPerCell = 60f / speed;
        float cost = grid.Cost((int)MathF.Floor(X), (int)MathF.Floor(Z));
        float slowed = ticksPerCell / Math.Clamp(ticksPerCell + cost, ticksPerCell, MaxTicksPerCell);
        return speed * rate * slowed;
    }

    /// <summary>Moves by <paramref name="step"/>, sliding along walls, in small steps so
    /// even at 6x game speed nothing is skipped through.</summary>
    private void Move(Vector2 step, Grid grid)
    {
        int parts = Math.Max(1, (int)MathF.Ceiling(step.Length() / 0.15f));
        Vector2 each = step / parts;
        for (int i = 0; i < parts; i++)
        {
            if (Free(grid, X + each.X, Z)) X += each.X;
            if (Free(grid, X, Z + each.Y)) Z += each.Y;
        }
    }

    /// <summary>Whether a player standing at (x, z) touches no cell they can not stand in.</summary>
    public static bool Free(Grid grid, float x, float z)
    {
        float r = Dims.PlayerRadius;
        int x0 = (int)MathF.Floor(x - r), x1 = (int)MathF.Floor(x + r);
        int z0 = (int)MathF.Floor(z - r), z1 = (int)MathF.Floor(z + r);
        for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                if (!grid.Blocks(cx, cz)) continue;
                float nx = Math.Clamp(x, cx, cx + 1), nz = Math.Clamp(z, cz, cz + 1);
                float dx = x - nx, dz = z - nz;
                if (dx * dx + dz * dz < r * r) return false;
            }
        return true;
    }

    /// <summary>No colonist to play: fly free over the map (WASD, Space up, C down, Shift
    /// faster), through anything.</summary>
    private void Fly(float dt, Controls c, bool held, Grid grid)
    {
        if (held) return;
        Vector3 f = Forward, r = Right;
        Vector3 move = f * c.Forward + r * c.Right + Vector3.UnitY * c.Up;
        if (move.LengthSquared() > 1f) move = Vector3.Normalize(move);
        float speed = FlySpeed * (c.Sprint || c.Walk ? FlyFast : 1f);
        X += move.X * speed * dt;
        Z -= move.Z * speed * dt;
        FlyY = Math.Clamp(FlyY + move.Y * speed * dt, 0.3f, 60f);
        if (grid.Ready)
        {
            X = Math.Clamp(X, -10, grid.W + 10);
            Z = Math.Clamp(Z, -10, grid.H + 10);
        }
    }
}
