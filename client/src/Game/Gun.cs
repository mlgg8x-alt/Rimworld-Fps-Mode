using System.Numerics;
using RimFps.Net;
using RimFps.World;

namespace RimFps.Game;

/// <summary>
/// The colonist's weapon in the player's hands, firing as RimWorld fires it: a trigger pull
/// fires the gun's whole burst, its shots RimWorld's time apart; then the gun cycles for
/// RimWorld's cooldown plus the colonist's aiming time before the next burst (shown as a
/// reload). Bullets are hitscan, as in Doom, and go no further than the gun's RimWorld range;
/// a hit is reported with the body part struck, and RimWorld deals the damage. Aiming down the
/// sights tightens the spread and zooms by the gun's range; sprinting holds fire.
/// Melee weapons and bare hands swing at whoever is in front, and any weapon bashes with V
/// (RimWorld uses the gun's own melee: its stock or barrel); grenades are thrown.
/// </summary>
public sealed class Gun
{
    public enum Mode { None, Gun, Melee, Throw }

    private const float MeleeReach = 1.7f, MeleeCone = 0.55f, MeleeSeconds = 0.9f, SwingTime = 0.32f;
    /// <summary>Where on a standing person's picture each part starts, highest first: their
    /// face ends a little above half way, measured on RimWorld's pictures (upstream M18).</summary>
    private static readonly (float from, string part)[] PictureParts = { (0.56f, "head"), (0.36f, "upper"), (0.18f, "lower") };

    public Mode Kind;
    public int Burst = 1, Clip = 1;
    /// <summary>Seconds to the next shot of a burst, and left of the cycle after one.</summary>
    public float Between, Cycle, CycleLength = 1;
    /// <summary>Shots of the current burst still to fire.</summary>
    public int Pending;
    /// <summary>Animation: the kick of the last shot (1 just fired), the swing or throw in
    /// progress (0 to 1), seconds since anything fired.</summary>
    public float Kick, Swing = -1, Throw = -1;
    public float Wait;
    public string WeaponDef = "";
    /// <summary>Shots fired this frame, for flashes, tracers and sounds: from and to, in RimWorld
    /// coordinates, and whether a pawn was struck.</summary>
    public readonly List<(Vector3 from, Vector3 to, bool hit)> Fired = new();
    public bool Swung, Thrown;
    public string? LastTarget;
    /// <summary>When a shot or blow last struck someone, and who and where, for the HUD's hit
    /// and kill markers.</summary>
    public float LastHitAt = -10f;
    public string? LastHitPart;
    public float Spread;
    public float Clock;
    /// <summary>How much the sights zoom for this gun: more the further it shoots.</summary>
    public float Zoom = 1.3f;

    private readonly Random random = new();

    public void Update(float dt, bool trigger, bool triggerDown, bool reload, bool grenadeKey, bool bash, bool aiming, Link link, Player player, Figures figures,
        Grid grid, Action<string> send, bool frozen)
    {
        Clock += dt;
        Fired.Clear();
        Swung = Thrown = false;
        PawnState? pawn = link.Player;
        GunTiming? gun = link.Snap?.Gun;
        float rate = link.Rate;
        Mode kind = pawn == null ? Mode.None
            : gun?.Throw != null ? Mode.Throw
            : gun != null ? Mode.Gun
            : Mode.Melee;
        string def = pawn?.Weapon ?? "";
        if (kind != Kind || def != WeaponDef)
        {
            Kind = kind;
            WeaponDef = def;
            Pending = 0;
            Cycle = 0;
            Clip = Burst = Math.Max(1, gun?.Burst ?? 1);
        }
        Kick = Math.Max(0, Kick - dt * 6f);
        if (Swing >= 0) Swing = Swing + dt / SwingTime > 1 ? -1 : Swing + dt / SwingTime;
        if (Throw >= 0) Throw = Throw + dt / 0.45f > 1 ? -1 : Throw + dt / 0.45f;
        Wait = Math.Max(0, Wait - dt);
        aimingNow = aiming && kind == Mode.Gun;
        float rest = aimingNow ? 0.12f : 1.1f;
        Spread = Math.Max(rest, Spread - dt * 5f);
        Zoom = gun?.Range is float range ? range >= 40f ? 3f : range >= 28f ? 1.6f : 1.3f : 1.3f;
        if (pawn == null || frozen || link.Paused) return;
        // A bash with whatever is in hand.
        if (bash && Wait <= 0 && kind != Mode.Throw)
        {
            Melee(pawn, player, figures, send, rate);
            return;
        }
        switch (kind)
        {
            case Mode.Gun:
                // Sprinting holds fire; pulling the trigger is what stops the sprint.
                Shoot(dt, trigger && !player.Sprinting, reload, gun!, rate, pawn, player, figures, grid, send);
                break;
            case Mode.Melee:
                if (triggerDown && Wait <= 0) Melee(pawn, player, figures, send, rate);
                break;
            case Mode.Throw:
                if ((triggerDown || grenadeKey) && Wait <= 0)
                {
                    Vector2 at = AimOnGround(player, gun!.Throw ?? 10f);
                    send(Out.PlayerThrow(pawn.Id, at.X, at.Y));
                    Wait = (gun.Warmup + gun.Cooldown) / rate;
                    Throw = 0;
                    Thrown = true;
                }
                break;
        }
    }

    private void Shoot(float dt, bool trigger, bool reload, GunTiming gun, float rate, PawnState pawn, Player player, Figures figures,
        Grid grid, Action<string> send)
    {
        Burst = Math.Max(1, gun.Burst);
        CycleLength = Math.Max(0.1f, (gun.Cooldown + gun.Warmup) / rate);
        if (Cycle > 0)
        {
            Cycle -= dt;
            if (Cycle <= 0) Clip = Burst;
            return;
        }
        if (reload && Pending == 0 && Clip < Burst)
        {
            Cycle = CycleLength;
            return;
        }
        if (Pending == 0 && trigger && Clip > 0)
        {
            Pending = Clip;
            Between = 0;
        }
        if (Pending <= 0) return;
        Between -= dt;
        while (Pending > 0 && Between <= 0)
        {
            FireOne(gun, pawn, player, figures, grid, send);
            Pending--;
            Clip--;
            Between += Math.Max(0.03f, gun.Between / rate);
        }
        if (Clip <= 0)
        {
            Pending = 0;
            Cycle = CycleLength;
        }
    }

    private bool aimingNow;

    /// <summary>How hard each class of gun kicks, degrees up per shot at the hip.</summary>
    private static float KickOf(string def) => Audio.Sfx.ShotFor(def) switch
    {
        "light" => 1.0f,
        "heavy" => 1.9f,
        "shotgun" => 2.8f,
        "charge" => 0.6f,
        _ => 0.75f,
    };

    private void FireOne(GunTiming gun, PawnState pawn, Player player, Figures figures, Grid grid, Action<string> send)
    {
        // Spread: tight down the sights, loose from the hip, wider moving, a little more each
        // shot of a burst, less crouched.
        float spread = Spread + player.Moving * (aimingNow ? 0.35f : 1.4f) + (player.JumpY > 0 ? 2.5f : 0f);
        if (player.Crouching) spread *= 0.7f;
        Spread = Math.Min(Spread + (aimingNow ? 0.18f : 0.5f), 3f);
        float kick = KickOf(pawn.Weapon ?? "") * (aimingNow ? 0.55f : 1f) * (player.Crouching ? 0.8f : 1f);
        player.Kick(kick, ((float)random.NextDouble() - 0.5f) * kick * 0.5f);
        Vector3 dir = Jitter(player.Forward, spread);
        Vector3 eye = player.Eye;
        var from = new Vector3(eye.X, eye.Y, -eye.Z);
        var way = new Vector3(dir.X, dir.Y, -dir.Z);
        float range = gun.Range is float r && r > 1 ? r : 32f;
        var (hit, to, target) = Trace(from, way, range, grid, figures);
        string? part = null;
        if (target is Target t)
        {
            float up = (to.Y - t.Bottom) / Math.Max(1e-3f, t.Top - t.Bottom);
            part = t.Parts ? PictureParts.FirstOrDefault(p => up > p.from).part ?? "legs" : "body";
            LastTarget = t.Id;
            LastHitAt = Clock;
            LastHitPart = part;
        }
        var muzzle = from + way * 0.4f;
        send(Out.PlayerShot(pawn.Id, pawn.Weapon ?? "gun",
            new[] { from.X, from.Y / Dims.ShotHeightScale, from.Z }, new[] { to.X, to.Y / Dims.ShotHeightScale, to.Z },
            target?.Id, part));
        Fired.Add((muzzle, to, target != null));
        Kick = 1;
    }

    /// <summary>The first thing a bullet from <paramref name="from"/> along <paramref name="way"/>
    /// meets within <paramref name="range"/> cells: a wall or the ground (hit false), or a pawn.</summary>
    public static (bool hit, Vector3 to, Target? target) Trace(Vector3 from, Vector3 way, float range, Grid grid, Figures figures)
    {
        way = Vector3.Normalize(way);
        float wall = range;
        const float step = 0.04f;
        int sx = (int)MathF.Floor(from.X), sz = (int)MathF.Floor(from.Z);
        for (float d = step; d <= range; d += step)
        {
            Vector3 p = from + way * d;
            int cx = (int)MathF.Floor(p.X), cz = (int)MathF.Floor(p.Z);
            if (cx == sx && cz == sz && p.Y > 0) continue;
            if (!grid.Inside(cx, cz) || grid.Stops(cx, cz, p.Y, cover: false))
            {
                wall = d;
                break;
            }
        }
        Target? best = null;
        float bestD = wall;
        foreach (Target t in figures.Targets)
        {
            // The ray against an upright cylinder round the pawn.
            float ox = from.X - t.X, oz = from.Z - t.Z;
            float a = way.X * way.X + way.Z * way.Z, b = 2 * (ox * way.X + oz * way.Z), c = ox * ox + oz * oz - t.Half * t.Half;
            if (a < 1e-6f) continue;
            float disc = b * b - 4 * a * c;
            if (disc < 0) continue;
            float sq = MathF.Sqrt(disc);
            float t0 = (-b - sq) / (2 * a), t1 = (-b + sq) / (2 * a);
            foreach (float d in new[] { t0, (t0 + t1) / 2, t1 })
            {
                if (d <= 0.05f || d >= bestD) continue;
                float y = from.Y + way.Y * d;
                if (y < t.Bottom || y > t.Top) continue;
                best = t;
                bestD = d;
                break;
            }
        }
        return (best != null || wall < range, from + way * bestD, best);
    }

    private Vector3 Jitter(Vector3 dir, float degrees)
    {
        float r = degrees * MathF.PI / 180f * MathF.Sqrt((float)random.NextDouble());
        float a = (float)random.NextDouble() * MathF.Tau;
        Vector3 side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY) + new Vector3(1e-5f, 0, 0));
        Vector3 up = Vector3.Cross(side, dir);
        return Vector3.Normalize(dir + (side * MathF.Cos(a) + up * MathF.Sin(a)) * MathF.Tan(r));
    }

    /// <summary>A swing at whoever stands nearest in front, within reach: RimWorld makes the
    /// attack (if it counts them as in reach) and rolls everything about it.</summary>
    private void Melee(PawnState pawn, Player player, Figures figures, Action<string> send, float rate)
    {
        Swing = 0;
        Swung = true;
        Wait = MeleeSeconds / Math.Max(1, rate);
        float yaw = player.Yaw * MathF.PI / 180f;
        var facing = new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
        Target? best = null;
        float bestScore = float.MaxValue;
        foreach (Target t in figures.Targets)
        {
            var to = new Vector2(t.X - player.X, t.Z - player.Z);
            float d = to.Length();
            if (d > MeleeReach || d < 1e-3f) continue;
            float ahead = Vector2.Dot(to / d, facing);
            if (ahead < MeleeCone) continue;
            float score = d * (1.5f - ahead * 0.5f);
            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        if (best is Target hit)
        {
            send(Out.PlayerMelee(pawn.Id, hit.Id));
            LastTarget = hit.Id;
            LastHitAt = Clock;
            LastHitPart = "melee";
        }
    }

    /// <summary>Where the player aims on the ground, no further than <paramref name="reach"/>
    /// cells, and that far when looking level or up.</summary>
    public static Vector2 AimOnGround(Player player, float reach)
    {
        float pitch = player.Pitch * MathF.PI / 180f, yaw = player.Yaw * MathF.PI / 180f;
        float down = -MathF.Sin(pitch);
        float far = down > 0.01f ? player.EyeY / down * MathF.Cos(pitch) : reach;
        float d = Math.Min(far, reach);
        return new Vector2(player.X + MathF.Sin(yaw) * d, player.Z + MathF.Cos(yaw) * d);
    }
}
