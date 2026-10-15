using System.Numerics;
using RimFps.Net;
using RimFps.Render;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Game;

/// <summary>
/// What the player holds, in 3D in front of them: the colonist's weapon, built from its RimWorld
/// picture (<see cref="WeaponModel"/>), held by two round hands in their skin colour, the way
/// RimWorld draws hands. It sits at the hip or comes up to the eye when aiming, lowers to run,
/// bobs, sways behind the mouse, kicks with each shot, dips while RimWorld's cooldown cycles the
/// gun, swings melee weapons in an arc, bashes with guns, and throws grenades. Drawn with its own
/// camera and always in front of the world, so it never sinks into a wall.
/// </summary>
public sealed class Hands : IDisposable
{
    private readonly Dictionary<int, WeaponModel?> models = new();
    private float time, swayX, swayY, swayRoll;
    private Vector2 lastLook;
    private float sprint, aim, flash;
    private Pic? handPic;

    /// <summary>How far aimed down the sights, 0 to 1, for the camera's zoom.</summary>
    public float Aim => aim;

    private WeaponModel? Model(Pictures pictures, Held held)
    {
        if (models.TryGetValue(held.Tex, out WeaponModel? m)) return m;
        m = WeaponModel.Build(pictures, held.Tex, new Vector2(held.W, held.H), held.Melee);
        // Not loaded yet: ask again next frame.
        if (m != null) models[held.Tex] = m;
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
        swayX += (-lastLook.X * 0.00035f * k - swayX) * Math.Min(1f, dt * 10f);
        swayY += (lastLook.Y * 0.00035f * k - swayY) * Math.Min(1f, dt * 10f);
        swayRoll += (-lastLook.X * 0.03f * k - swayRoll) * Math.Min(1f, dt * 8f);
        swayX = Math.Clamp(swayX, -0.03f, 0.03f);
        swayY = Math.Clamp(swayY, -0.03f, 0.03f);
        swayRoll = Math.Clamp(swayRoll, -6f, 6f);
        if (gun.Fired.Count > 0) flash = 0.05f;
        flash = Math.Max(0, flash - dt);
    }

    private static Matrix4x4 Turn(float pitch, float yaw, float roll) =>
        Matrix4x4.CreateRotationZ(roll * MathF.PI / 180f) * Matrix4x4.CreateRotationX(pitch * MathF.PI / 180f) * Matrix4x4.CreateRotationY(yaw * MathF.PI / 180f);

    /// <summary>Model space (x along the weapon, y up, z across) to view space (x right, y up,
    /// -z ahead): the weapon points ahead.</summary>
    private static readonly Matrix4x4 Ahead = new(0, 0, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1);

    public void Draw(Link link, Player player, Gun gun, Pictures pictures, Atlas atlas, Builtins art, Renderer renderer, Settings settings, Vector3 light)
    {
        PawnState? pawn = link.Player;
        if (pawn == null) return;
        handPic ??= art.Hand;
        var camera = new Camera3D(Vector3.Zero, new Vector3(0, 0, -1), Vector3.UnitY, 54f, CameraProjection.Perspective);
        Raylib.BeginMode3D(camera);
        Rlgl.DisableBackfaceCulling();
        renderer.HeldTint(light);
        var (skin, _) = Colours(link, pawn.Id);
        float bob = settings.Bob ? player.Moving * (1f - aim * 0.85f) : 0f;
        var bobbing = new Vector3(MathF.Sin(player.BobPhase) * 0.011f * bob, -MathF.Abs(MathF.Cos(player.BobPhase)) * 0.012f * bob + MathF.Sin(time * 1.7f) * 0.002f, 0);
        var sway = new Vector3(swayX, swayY, 0);
        var hands = new List<(Vector3 at, float size)>();
        Vector3? flashAt = null;
        float flashSize = 0;

        WeaponModel? model = pawn.Held != null ? Model(pictures, pawn.Held) : null;
        if (model != null && gun.Kind != Gun.Mode.Throw)
        {
            Matrix4x4 pose = model.Melee ? MeleePose(model, gun) : GunPose(model, gun);
            pose *= Matrix4x4.CreateTranslation(bobbing + sway);
            model.Draw(renderer.HeldMaterial(model.Page), Matrix4x4.Transpose(pose));
            Vector3 grip = Vector3.Transform(new Vector3(model.Grip, 0), pose);
            hands.Add((grip + new Vector3(0, 0, 0.012f), 0.017f));
            if (!model.Melee)
            {
                Vector3 fore = model.TwoHanded
                    ? Vector3.Transform(new Vector3(model.Fore, 0), pose)
                    : grip + new Vector3(-0.018f, -0.012f, 0.004f);
                hands.Add((fore + new Vector3(0, -0.004f, 0.01f), 0.016f));
                if (flash > 0)
                {
                    flashAt = Vector3.Transform(new Vector3(model.Muzzle + new Vector2(0.03f, 0), 0), pose);
                    flashSize = 0.06f + 0.03f * (float)Random.Shared.NextDouble();
                }
            }
        }
        else if (gun.Kind == Gun.Mode.Throw)
        {
            // A grenade in the hand, thrown with a swing of the arm.
            float t = gun.Throw >= 0 ? gun.Throw : 0;
            bool gone = gun.Throw >= 0.45f || gun.Throw < 0 && gun.Wait > 0;
            var hand = new Vector3(0.14f - t * 0.1f, -0.13f + MathF.Sin(t * MathF.PI) * 0.12f, -0.28f - t * 0.15f) + bobbing + sway;
            if (gone && gun.Throw < 0) hand.Y -= 0.2f;
            if (!gone && pawn.Held != null && pictures.Get(pawn.Held.Tex) is Pic grenade)
            {
                Raylib.BeginShaderMode(renderer.HeldShader);
                Sprite(atlas, grenade, hand + new Vector3(0, 0.03f, -0.01f), 0.07f, Color.White);
                Rlgl.DrawRenderBatchActive();
                Raylib.EndShaderMode();
            }
            hands.Add((hand, 0.02f));
        }
        else
        {
            // Bare hands: two fists up, the right one punching.
            float t = gun.Swing >= 0 ? gun.Swing : 0;
            float punch = MathF.Sin(t * MathF.PI);
            hands.Add((new Vector3(0.12f - punch * 0.1f, -0.13f + punch * 0.06f, -0.27f - punch * 0.18f) + bobbing + sway, 0.024f));
            hands.Add((new Vector3(-0.12f, -0.14f, -0.27f) + bobbing * 0.7f + sway, 0.024f));
        }

        // The hands: round, as RimWorld's, in the colonist's skin, on top of what they hold
        // (as RimWorld draws them).
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();
        Raylib.BeginShaderMode(renderer.HeldShader);
        foreach (var (at, size) in hands) Sprite(atlas, handPic, at, size * 2f, skin);
        Rlgl.DrawRenderBatchActive();
        if (flashAt is Vector3 muzzle)
        {
            // The muzzle flash glows whatever the light.
            renderer.HeldTint(new Vector3(1.4f));
            Sprite(atlas, art.Flash[(int)(time * 30) % 2], muzzle, flashSize, Color.White);
            Rlgl.DrawRenderBatchActive();
            renderer.HeldTint(light);
        }
        Raylib.EndShaderMode();
        Rlgl.EnableDepthTest();
        Rlgl.EnableBackfaceCulling();
        Raylib.EndMode3D();
    }

    /// <summary>A gun at the hip, or up at the eye when aiming; lowered to run, dipped while
    /// it cycles, kicked by each shot, thrust out to bash.</summary>
    private Matrix4x4 GunPose(WeaponModel m, Gun gun)
    {
        float viewLength = Math.Clamp(m.Length * 0.27f, 0.14f, 0.38f);
        float scale = viewLength / m.Length;
        Matrix4x4 size = Matrix4x4.CreateScale(scale);
        var grip3 = new Vector3(m.Grip, 0);
        // At the hip: down and to the right, turned a touch inwards.
        Matrix4x4 hipTurn = Turn(-1f, 4f, 0f);
        Vector3 hipGrip = new(0.115f, -0.118f, -0.3f);
        Matrix4x4 hip = size * Ahead * hipTurn;
        hip *= Matrix4x4.CreateTranslation(hipGrip - Vector3.Transform(grip3, hip));
        // Aiming: brought in under the middle of the view, held out at arm's length so the
        // stock stays off the eye, turned a little so its side still shows (RimWorld's weapons
        // have no sights to look down).
        Matrix4x4 ads = size * Ahead * Turn(2f, 5f, 0f);
        ads *= Matrix4x4.CreateTranslation(new Vector3(0.045f, -0.085f, -0.3f) - Vector3.Transform(grip3, ads));
        Matrix4x4 pose = Blend(hip, ads, aim);
        float kick = gun.Kick * gun.Kick;
        float cycle = gun.Kind == Gun.Mode.Gun && gun.Cycle > 0
            ? MathF.Sin(Math.Clamp(1f - gun.Cycle / Math.Max(gun.CycleLength, 0.01f), 0, 1) * MathF.PI) : 0;
        float bash = gun.Swing >= 0 ? MathF.Sin(gun.Swing * MathF.PI) : 0;
        // Everything turns about the grip.
        Vector3 pivot = Vector3.Transform(grip3, pose);
        Matrix4x4 around = Matrix4x4.CreateTranslation(-pivot)
            * Turn(kick * (6f - aim * 3f) - cycle * 32f - sprint * 12f, sprint * 32f + bash * 18f, cycle * 22f - sprint * 18f + swayRoll + bash * -25f)
            * Matrix4x4.CreateTranslation(pivot + new Vector3(-sprint * 0.04f - bash * 0.06f, -cycle * 0.09f - sprint * 0.05f, kick * 0.035f - bash * 0.12f));
        return pose * around;
    }

    /// <summary>A melee weapon held up over the right shoulder; a swing sweeps it down and across.</summary>
    private Matrix4x4 MeleePose(WeaponModel m, Gun gun)
    {
        float viewLength = Math.Clamp(m.Length * 0.24f, 0.11f, 0.32f);
        Matrix4x4 size = Matrix4x4.CreateScale(viewLength / m.Length);
        var grip3 = new Vector3(m.Grip, 0);
        float t = gun.Swing >= 0 ? gun.Swing : 0;
        float eased = t * t * (3 - 2 * t);
        float back = t < 0.25f ? t / 0.25f : 1f;
        // Raised: blade up and a little forward; struck: down and across to the left.
        float pitch = 62f - eased * 95f + (1 - back) * 8f - sprint * 20f;
        float yaw = 18f + eased * 55f + sprint * 20f;
        float roll = -12f - eased * 40f + swayRoll;
        Vector3 hand = Vector3.Lerp(new Vector3(0.15f, -0.115f, -0.3f), new Vector3(-0.05f, -0.15f, -0.34f), eased) + new Vector3(0, -sprint * 0.04f, 0);
        Matrix4x4 pose = size * Ahead * Turn(pitch, yaw, roll);
        pose *= Matrix4x4.CreateTranslation(hand - Vector3.Transform(grip3, pose));
        return pose;
    }

    private static Matrix4x4 Blend(Matrix4x4 a, Matrix4x4 b, float t)
    {
        if (t <= 0.001f) return a;
        if (t >= 0.999f) return b;
        Matrix4x4.Decompose(a, out Vector3 sa, out Quaternion ra, out Vector3 ta);
        Matrix4x4.Decompose(b, out Vector3 sb, out Quaternion rb, out Vector3 tb);
        float e = t * t * (3 - 2 * t);
        return Matrix4x4.CreateScale(Vector3.Lerp(sa, sb, e)) * Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(ra, rb, e)) * Matrix4x4.CreateTranslation(Vector3.Lerp(ta, tb, e));
    }

    /// <summary>A picture facing the eye at a view-space point, through the held shader.</summary>
    private static void Sprite(Atlas atlas, Pic pic, Vector3 at, float size, Color color)
    {
        float h = size / 2f;
        Rlgl.SetTexture(atlas.Texture(pic.R.Page).Id);
        Rlgl.Begin((int)DrawMode.Quads);
        Rlgl.Color4ub(color.R, color.G, color.B, 255);
        Corner(pic, 0, 1, at + new Vector3(-h, -h, 0));
        Corner(pic, 1, 1, at + new Vector3(h, -h, 0));
        Corner(pic, 1, 0, at + new Vector3(h, h, 0));
        Corner(pic, 0, 0, at + new Vector3(-h, h, 0));
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    private static void Corner(Pic pic, float u, float v, Vector3 p)
    {
        Vector2 t = pic.R.At(pic.Cut[0] + pic.CutW * u, pic.Cut[1] + pic.CutH * v);
        Rlgl.TexCoord2f(t.X, t.Y);
        Rlgl.Vertex3f(p.X, p.Y, p.Z);
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

    /// <summary>A new atlas: every model again.</summary>
    public void Forget()
    {
        foreach (WeaponModel? m in models.Values) m?.Dispose();
        models.Clear();
        handPic = null;
    }

    public void Dispose() => Forget();
}
