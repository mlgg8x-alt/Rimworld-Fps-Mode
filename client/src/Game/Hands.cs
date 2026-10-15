using System.Numerics;
using RimFps.Net;
using RimFps.UI;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Game;

/// <summary>
/// What the player holds, drawn flat on the screen in front of everything: the colonist's
/// weapon as its own RimWorld picture (<see cref="WeaponModel"/>), held by round hands in their
/// skin colour, the way RimWorld draws hands. It sits low on the right or comes in under the
/// crosshair when aiming, lowers to run, bobs, sways behind the mouse, kicks with each shot,
/// dips while RimWorld's cooldown cycles the gun, swings melee weapons down and across, bashes
/// with guns, and throws grenades. Where it sits is the player's to change in the HUD's edit
/// mode (<see cref="WeaponPose"/>).
/// </summary>
public sealed class Hands : IDisposable
{
    private readonly Dictionary<(int, float), WeaponModel?> models = new();
    private float time, swayX, swayY, swayRoll;
    private Vector2 lastLook;
    private float sprint, aim, flash;

    /// <summary>How far aimed down the sights, 0 to 1, for the camera's zoom.</summary>
    public float Aim => aim;

    /// <summary>Set while the HUD is edited: the weapon held still, at the hip (0) or aimed (1).</summary>
    public float? Hold;

    /// <summary>The weapon drawn last frame, for the editor.</summary>
    public WeaponModel? Shown { get; private set; }
    public string ShownName { get; private set; } = "";
    public Placed ShownAt { get; private set; }
    public Vector2 ShownGrip { get; private set; }
    public Vector2? ShownFore { get; private set; }
    public Vector2? ShownMuzzle { get; private set; }

    /// <summary>A weapon's picture placed on the screen: its grip at <see cref="At"/>, turned
    /// <see cref="Turn"/> degrees, <see cref="Scale"/> screen pixels to a picture pixel,
    /// mirrored or not.</summary>
    public readonly record struct Placed(WeaponModel Model, Vector2 At, float Turn, float Scale, bool Mirror, Vector2 Grip)
    {
        private Vector2 Local(Vector2 p) => new(((Mirror ? 1 - p.X : p.X) - (Mirror ? 1 - Grip.X : Grip.X)) * Model.W * Scale, (p.Y - Grip.Y) * Model.H * Scale);

        /// <summary>A point of the picture (0 to 1) on the screen.</summary>
        public Vector2 ToScreen(Vector2 p)
        {
            Vector2 l = Local(p);
            float r = Turn * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
            return At + new Vector2(l.X * c - l.Y * s, l.X * s + l.Y * c);
        }

        /// <summary>A point of the screen on the picture (0 to 1).</summary>
        public Vector2 ToPicture(Vector2 screen)
        {
            Vector2 d = screen - At;
            float r = -Turn * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
            var l = new Vector2(d.X * c - d.Y * s, d.X * s + d.Y * c);
            float gx = Mirror ? 1 - Grip.X : Grip.X;
            float x = l.X / (Model.W * Scale) + gx, y = l.Y / (Model.H * Scale) + Grip.Y;
            return new Vector2(Mirror ? 1 - x : x, y);
        }

        /// <summary>The picture's corners on the screen.</summary>
        public Vector2[] Corners() => new[] { ToScreen(new(0, 0)), ToScreen(new(1, 0)), ToScreen(new(1, 1)), ToScreen(new(0, 1)) };
    }

    private WeaponModel? Model(Pictures pictures, Held held)
    {
        var key = (held.Tex, held.Turn);
        if (models.TryGetValue(key, out WeaponModel? m)) return m;
        m = WeaponModel.Build(pictures, held.Tex, new Vector2(held.W, held.H), held.Melee, held.Turn);
        // Not loaded yet: ask again next frame.
        if (m != null) models[key] = m;
        return m;
    }

    public void Update(float dt, Player player, Gun gun, bool aiming, Vector2 look)
    {
        time += dt;
        float want = aiming && gun.Kind == Gun.Mode.Gun && gun.Cycle <= 0 ? 1f : 0f;
        aim += (want - aim) * Math.Min(1f, dt * 14f);
        float run = player.Sprinting ? 1f : 0f;
        sprint += (run - sprint) * Math.Min(1f, dt * 9f);
        // Sway: the weapon lags behind the view, and settles.
        lastLook = Vector2.Lerp(lastLook, look, Math.Min(1f, dt * 12f));
        float k = 1f - aim * 0.8f;
        swayX += (-lastLook.X * 0.0012f * k - swayX) * Math.Min(1f, dt * 10f);
        swayY += (-lastLook.Y * 0.0012f * k - swayY) * Math.Min(1f, dt * 10f);
        swayRoll += (lastLook.X * 0.03f * k - swayRoll) * Math.Min(1f, dt * 8f);
        swayX = Math.Clamp(swayX, -0.05f, 0.05f);
        swayY = Math.Clamp(swayY, -0.05f, 0.05f);
        swayRoll = Math.Clamp(swayRoll, -6f, 6f);
        if (gun.Fired.Count > 0) flash = 0.05f;
        flash = Math.Max(0, flash - dt);
    }

    /// <summary>Where the hands and the muzzle are on a weapon's picture: as the player put
    /// them, else as its outline says.</summary>
    public static Vector2 GripOf(WeaponModel m, WeaponPose p) => p.Grip is { Length: 2 } g ? new(g[0], g[1]) : m.Grip;
    public static Vector2 ForeOf(WeaponModel m, WeaponPose p) => p.Fore is { Length: 2 } g ? new(g[0], g[1]) : m.Fore;
    public static Vector2 MuzzleOf(WeaponModel m, WeaponPose p) => p.Muzzle is { Length: 2 } g ? new(g[0], g[1]) : m.Muzzle;
    public static bool TwoHands(WeaponModel m, WeaponPose p) => p.HandCount == 2 || p.HandCount == 0 && m.TwoHanded && !m.Melee;

    /// <summary>Whether the picture is mirrored in this pose: when it points left, so it is
    /// never drawn upside down.</summary>
    public static bool Mirrored(WeaponPose p) => p.Flip == 1 || p.Flip == 0 && MathF.Cos(p.Point * MathF.PI / 180f) < 0;

    /// <summary>The weapon's length on a screen <paramref name="h"/> pixels tall.</summary>
    public static float LengthOnScreen(WeaponModel m, WeaponPose p, float h) =>
        p.Size * h * (m.Melee ? Math.Clamp(0.22f + m.Length * 0.2f, 0.3f, 0.6f) : Math.Clamp(0.22f + m.Length * 0.2f, 0.3f, 0.56f));

    /// <summary>The weapon placed by a pose, before it moves: at the hip, or aimed.</summary>
    public static Placed Place(WeaponModel m, WeaponPose p, int w, int h, float aimed)
    {
        bool mirror = Mirrored(p);
        float e = aimed * aimed * (3 - 2 * aimed);
        var at = Vector2.Lerp(new Vector2(p.GripX * w, p.GripY * h), new Vector2(p.AimGripX * w, p.AimGripY * h), e);
        float point = p.Point + Figures.AngleDiff(p.Point, p.AimPoint) * e;
        // The picture points Angle (mirrored: 180 - Angle); turned to point where the pose says.
        float turn = point - (mirror ? 180f - m.Angle : m.Angle);
        float scale = LengthOnScreen(m, p, h) / Math.Max(1f, m.LengthPixels);
        return new Placed(m, at, turn, scale, mirror, GripOf(m, p));
    }

    public void Draw(Link link, Player player, Gun gun, Pictures pictures, Atlas atlas, Builtins art, Settings settings, Vector3 light, int w, int h)
    {
        Shown = null;
        PawnState? pawn = link.Player;
        if (pawn == null) return;
        var (skin, _) = Colours(link, pawn.Id);
        bool live = Hold == null;
        float bob = settings.Bob && live ? player.Moving * (1f - aim * 0.85f) : 0f;
        var shake = new Vector2(MathF.Sin(player.BobPhase) * 0.012f * bob, MathF.Abs(MathF.Cos(player.BobPhase)) * 0.014f * bob + MathF.Sin(time * 1.7f) * 0.002f) * h;
        if (live) shake += new Vector2(swayX, swayY) * h;
        var lit = new Color((byte)Math.Clamp(light.X * 255, 40, 255), (byte)Math.Clamp(light.Y * 255, 40, 255), (byte)Math.Clamp(light.Z * 255, 40, 255), (byte)255);
        var hands = new List<(Vector2 at, float size)>();
        float handSize = h * 0.03f;

        WeaponModel? model = pawn.Held != null ? Model(pictures, pawn.Held) : null;
        if (model != null && gun.Kind != Gun.Mode.Throw)
        {
            string name = pictures.Name(pawn.Held!.Tex);
            WeaponPose wp = settings.PoseFor(name, model.Melee);
            Placed placed = model.Melee ? MeleePose(model, gun, wp, w, h) : GunPose(model, gun, wp, w, h);
            placed = placed with { At = placed.At + shake };
            DrawWeapon(atlas, placed, lit, wp.Thickness * h * 0.006f);
            float hand = handSize * wp.HandSize;
            Vector2 grip = placed.ToScreen(GripOf(model, wp));
            Vector2? fore = TwoHands(model, wp) ? placed.ToScreen(ForeOf(model, wp)) : null;
            if (fore is Vector2 f) hands.Add((f, hand * 0.95f));
            else if (!model.Melee) hands.Add((grip + new Vector2(hand * 0.7f, hand * 0.55f), hand * 0.95f));
            hands.Add((grip, hand));
            Vector2 muzzle = placed.ToScreen(MuzzleOf(model, wp));
            Shown = model;
            ShownName = name;
            ShownAt = placed;
            ShownGrip = grip;
            ShownFore = fore;
            ShownMuzzle = model.Melee ? null : muzzle;
            DrawHands(hands, skin);
            if (!model.Melee && flash > 0 && live)
            {
                float r = placed.Turn * MathF.PI / 180f;
                var way = new Vector2(MathF.Cos(r), MathF.Sin(r)) * (placed.Mirror ? -1f : 1f);
                Pic glow = art.Flash[(int)(time * 30) % art.Flash.Length];
                float size = h * (0.1f + 0.04f * (float)Random.Shared.NextDouble());
                Vector2 at = muzzle + way * size * 0.3f;
                Ui.Picture(atlas, glow, new Rectangle(at.X, at.Y, size, size), Color.White, rotation: (float)Random.Shared.NextDouble() * 360f, origin: new Vector2(size / 2, size / 2));
            }
            return;
        }
        if (gun.Kind == Gun.Mode.Throw)
        {
            // A grenade in the hand, thrown with a swing of the arm.
            float t = gun.Throw >= 0 ? gun.Throw : 0;
            bool gone = gun.Throw >= 0.45f || gun.Throw < 0 && gun.Wait > 0;
            var hand = new Vector2(w * (0.74f - t * 0.2f), h * (0.88f - MathF.Sin(t * MathF.PI) * 0.25f)) + shake;
            if (gone && gun.Throw < 0) hand.Y += h * 0.3f;
            if (!gone && pawn.Held != null && pictures.Get(pawn.Held.Tex) is Pic grenade)
            {
                float size = h * 0.11f;
                Ui.Picture(atlas, grenade, new Rectangle(hand.X, hand.Y - size * 0.3f, size, size), lit, origin: new Vector2(size / 2, size / 2));
            }
            hands.Add((hand, handSize));
        }
        else
        {
            // Bare hands: two fists up, the right one punching.
            float t = gun.Swing >= 0 ? gun.Swing : 0;
            float punch = MathF.Sin(t * MathF.PI);
            hands.Add((new Vector2(w * (0.68f - punch * 0.14f), h * (0.86f - punch * 0.12f)) + shake, handSize * (1.3f - punch * 0.25f)));
            hands.Add((new Vector2(w * 0.32f, h * 0.88f) + shake * 0.7f, handSize * 1.3f));
        }
        DrawHands(hands, skin);
    }

    /// <summary>The picture, with a darker copy just below it for its edge, so it reads as a
    /// solid thing rather than a sticker.</summary>
    private static void DrawWeapon(Atlas atlas, Placed p, Color lit, float edge)
    {
        WeaponModel m = p.Model;
        var size = new Vector2(m.W * p.Scale, m.H * p.Scale);
        float gx = p.Mirror ? 1 - p.Grip.X : p.Grip.X;
        var origin = new Vector2(gx * size.X, p.Grip.Y * size.Y);
        if (edge > 0.2f)
        {
            var dark = new Color((byte)(lit.R * 0.28f), (byte)(lit.G * 0.28f), (byte)(lit.B * 0.28f), (byte)235);
            for (float d = edge; d > 0; d -= Math.Max(1f, edge / 3f))
                Ui.Picture(atlas, m.Pic, new Rectangle(p.At.X + d * 0.5f, p.At.Y + d, size.X, size.Y), dark, p.Mirror, p.Turn, origin);
        }
        Ui.Picture(atlas, m.Pic, new Rectangle(p.At.X, p.At.Y, size.X, size.Y), lit, p.Mirror, p.Turn, origin);
    }

    /// <summary>Round hands, as RimWorld draws them: skin with a dark rim.</summary>
    private static void DrawHands(List<(Vector2 at, float size)> hands, Color skin)
    {
        var rim = new Color((byte)(skin.R * 0.45f), (byte)(skin.G * 0.4f), (byte)(skin.B * 0.38f), (byte)255);
        foreach (var (at, size) in hands)
        {
            Raylib.DrawCircleV(at, size + Math.Max(1.5f, size * 0.14f), rim);
            Raylib.DrawCircleV(at, size, skin);
            Raylib.DrawCircleV(at - new Vector2(size * 0.25f, size * 0.3f), size * 0.4f, Ui.Fade(Ui.Shade(skin, 1.12f), 0.6f));
        }
    }

    /// <summary>A gun at the hip or aimed, as the pose places it; lowered to run, dipped while
    /// it cycles, kicked by each shot, thrust in to bash.</summary>
    private Placed GunPose(WeaponModel m, Gun gun, WeaponPose wp, int w, int h)
    {
        if (Hold is float held) return Place(m, wp, w, h, held);
        Placed p = Place(m, wp, w, h, aim);
        float kick = gun.Kick * gun.Kick;
        float cycle = gun.Kind == Gun.Mode.Gun && gun.Cycle > 0
            ? MathF.Sin(Math.Clamp(1f - gun.Cycle / Math.Max(gun.CycleLength, 0.01f), 0, 1) * MathF.PI) : 0;
        float bash = gun.Swing >= 0 ? MathF.Sin(gun.Swing * MathF.PI) : 0;
        // Up, for this gun: the way its muzzle rises (pointing left, that is clockwise).
        float rise = p.Mirror ? 1f : -1f;
        float turn = p.Turn + rise * (kick * (7f - aim * 3f) - cycle * 24f - sprint * 34f + bash * 12f) + swayRoll;
        float r = p.Turn * MathF.PI / 180f;
        var back = -new Vector2(MathF.Cos(r), MathF.Sin(r)) * (p.Mirror ? -1f : 1f);
        Vector2 at = p.At + back * kick * h * 0.035f
            + new Vector2(sprint * w * 0.03f, (cycle * 0.08f + sprint * 0.08f) * h)
            + new Vector2(-bash * w * 0.1f, -bash * h * 0.06f);
        return p with { At = at, Turn = turn };
    }

    /// <summary>A melee weapon held up as the pose places it; a swing sweeps it down and across.</summary>
    private Placed MeleePose(WeaponModel m, Gun gun, WeaponPose wp, int w, int h)
    {
        Placed p = Place(m, wp, w, h, 0);
        if (Hold != null) return p;
        float t = gun.Swing >= 0 ? gun.Swing : 0;
        float eased = t * t * (3 - 2 * t);
        float windup = t > 0 && t < 0.25f ? MathF.Sin(t / 0.25f * MathF.PI) : 0f;
        // Anticlockwise from up-and-in, through the middle of the view, to down-and-across.
        float turn = p.Turn - eased * 125f + windup * 12f + sprint * 25f + swayRoll;
        Vector2 at = Vector2.Lerp(p.At, new Vector2(w * 0.46f, h * 0.9f), eased) + new Vector2(sprint * w * 0.03f, sprint * h * 0.06f);
        return p with { At = at, Turn = turn };
    }

    /// <summary>The colonist's skin, and the colour of what they wear on their arms, outermost.</summary>
    public static (Color skin, Color sleeve) Colours(Link link, string id)
    {
        if (!link.Looks.TryGetValue(id, out Look? look)) return (new Color(214, 170, 132, 255), new Color(96, 104, 80, 255));
        var skin = new Color(look.Skin[0], look.Skin[1], look.Skin[2], (byte)255);
        Wear? arms = look.Wear.LastOrDefault(wear => wear.Parts.Contains("Arms") || wear.Parts.Contains("Shoulders"))
            ?? look.Wear.LastOrDefault(wear => wear.Parts.Contains("Torso"));
        Color sleeve = arms != null ? new Color(arms.Color[0], arms.Color[1], arms.Color[2], (byte)255) : skin;
        return (skin, sleeve);
    }

    /// <summary>A new atlas: every weapon looked at again.</summary>
    public void Forget() => models.Clear();

    public void Dispose() => Forget();
}
