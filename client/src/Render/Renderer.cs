using System.Numerics;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Render;

/// <summary>How the world is lit and fogged this frame.</summary>
public struct Lighting
{
    public float Sky;
    public Vector3 SkyTint, LampTint, FogColor;
    public float Ambient, FogStart, FogEnd;
}

/// <summary>
/// Draws the frame. In RimWorld style (the default) the 3D view goes straight to the window at
/// full resolution, multisampled, softly lit and fogged. In retro style it is drawn small into
/// a render texture, banded, pushed through a 256 colour palette with ordered dithering, and
/// blown up by a whole number of pixels with no smoothing. The HUD goes on top at full
/// resolution either way.
/// </summary>
public sealed class Renderer : IDisposable
{
    private readonly Settings settings;
    private Shader world, sprite, palette, held;
    private RenderTexture2D scene, post;
    private Texture2D lutRamps, lutRamps64, sky, softSky, stars;
    private readonly Dictionary<int, Material> materials = new();
    private Atlas? atlas;
    private int windowW, windowH;

    /// <summary>The size of what the 3D view is drawn into: the window, or the retro texture.</summary>
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Scale { get; private set; } = 1;

    public Shader WorldShader => world;
    public Shader SpriteShader => sprite;
    public Shader HeldShader => held;
    private readonly Dictionary<int, Material> heldMaterials = new();

    /// <summary>The material for held things (weapons, hands) on atlas page <paramref name="page"/>.</summary>
    public Material HeldMaterial(int page)
    {
        if (heldMaterials.TryGetValue(page, out Material m)) return m;
        m = Raylib.LoadMaterialDefault();
        m.Shader = held;
        Raylib.SetMaterialTexture(ref m, MaterialMapIndex.Albedo, atlas!.Texture(page));
        heldMaterials[page] = m;
        return m;
    }

    /// <summary>How held things are lit this frame.</summary>
    public void HeldTint(Vector3 tint) => Set(held, "tint", tint);

    public Renderer(Settings settings)
    {
        this.settings = settings;
        world = Raylib.LoadShaderFromMemory(Shaders.ForPlatform(Shaders.WorldVertex), Shaders.ForPlatform(Shaders.WorldFragment));
        sprite = Raylib.LoadShaderFromMemory(Shaders.ForPlatform(Shaders.WorldVertex), Shaders.ForPlatform(Shaders.WorldFragment));
        palette = Raylib.LoadShaderFromMemory(Shaders.ForPlatform(Shaders.PlainVertex), Shaders.ForPlatform(Shaders.PaletteFragment));
        held = Raylib.LoadShaderFromMemory(Shaders.ForPlatform(Shaders.WorldVertex), Shaders.ForPlatform(Shaders.HeldFragment));
        Set(world, "encoded", 1f);
        Set(sprite, "encoded", 0f);
        Set(held, "encoded", 2f);
        lutRamps = Lut(256);
        lutRamps64 = Lut(64);
        sky = Upload(PixelArt.Sky(1024, 160), 1024, 160, TextureWrap.Repeat, false);
        softSky = Upload(PixelArt.SoftSky(2048, 320), 2048, 320, TextureWrap.Repeat, true);
        stars = Upload(PixelArt.Stars(1024, 320), 1024, 320, TextureWrap.Repeat, false);
    }

    private static Texture2D Lut(int colours) =>
        Upload(Palette.Lookup(Palette.Make(colours / 16)), 1024, 32, TextureWrap.Clamp, false);

    private static unsafe Texture2D Upload(Color[] pixels, int w, int h, TextureWrap wrap, bool smooth)
    {
        Image image = Raylib.GenImageColor(w, h, Color.Black);
        Texture2D texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        fixed (Color* data = pixels) Raylib.UpdateTexture(texture, data);
        Raylib.SetTextureFilter(texture, smooth ? TextureFilter.Bilinear : TextureFilter.Point);
        Raylib.SetTextureWrap(texture, wrap);
        return texture;
    }

    private static void Set(Shader shader, string name, float value) =>
        Raylib.SetShaderValue(shader, Raylib.GetShaderLocation(shader, name), value, ShaderUniformDataType.Float);

    private static void Set(Shader shader, string name, Vector3 value) =>
        Raylib.SetShaderValue(shader, Raylib.GetShaderLocation(shader, name), value, ShaderUniformDataType.Vec3);

    private static void Set(Shader shader, string name, Vector4 value) =>
        Raylib.SetShaderValue(shader, Raylib.GetShaderLocation(shader, name), value, ShaderUniformDataType.Vec4);

    public void UseAtlas(Atlas atlas) => this.atlas = atlas;

    /// <summary>The world's material for atlas page <paramref name="page"/>.</summary>
    public Material Material(int page)
    {
        if (materials.TryGetValue(page, out Material m)) return m;
        m = Raylib.LoadMaterialDefault();
        m.Shader = world;
        Raylib.SetMaterialTexture(ref m, MaterialMapIndex.Albedo, atlas!.Texture(page));
        materials[page] = m;
        return m;
    }

    /// <summary>Forgets the materials, for a new atlas. Only their map lists are freed: the
    /// shader and the atlas textures they point at are not theirs to unload.</summary>
    public unsafe void ForgetMaterials()
    {
        foreach (Material m in materials.Values) Raylib.MemFree(m.Maps);
        materials.Clear();
        foreach (Material m in heldMaterials.Values) Raylib.MemFree(m.Maps);
        heldMaterials.Clear();
    }

    /// <summary>Sets this frame's light and fog on both shaders.</summary>
    public void Light(Lighting light)
    {
        bool retro = settings.Retro;
        float levels = !retro ? 0f : settings.Palette == "ramps64" ? 8f : 16f;
        float diminish = retro && settings.Diminish ? 0.011f : 0f;
        foreach (Shader s in new[] { world, sprite, held })
        {
            Set(s, "skyLight", light.Sky);
            Set(s, "skyTint", light.SkyTint);
            Set(s, "lampTint", light.LampTint);
            Set(s, "ambient", light.Ambient);
            Set(s, "diminish", diminish);
            Set(s, "levels", levels);
            Set(s, "fogSteps", retro ? 8f : 0f);
            Set(s, "cutoff", retro ? 0.5f : 0.42f);
            Set(s, "fogColor", light.FogColor);
            Set(s, "fogStart", light.FogStart);
            Set(s, "fogEnd", light.FogEnd);
            Set(s, "flash", 0f);
            Set(s, "fullbright", 0f);
        }
    }

    public void Fullbright(bool on) => Set(sprite, "fullbright", on ? 1f : 0f);

    /// <summary>Starts the 3D view: into the retro texture, or straight to the window.</summary>
    public void BeginScene(int windowW, int windowH)
    {
        this.windowW = windowW;
        this.windowH = windowH;
        if (!settings.Retro)
        {
            Width = windowW;
            Height = windowH;
            Scale = 1;
            return;
        }
        Fit(windowW, windowH);
        Raylib.BeginTextureMode(scene);
    }

    /// <summary>Ends the 3D view; in retro style puts it through the palette and onto the window.</summary>
    public void EndScene()
    {
        if (!settings.Retro) return;
        Raylib.EndTextureMode();
        Raylib.BeginTextureMode(post);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(palette);
        Set(palette, "usePalette", settings.Palette != "off" ? 1f : 0f);
        Set(palette, "dither", settings.Dither ? 1f : 0f);
        Set(palette, "spread", settings.Palette == "ramps64" ? 0.11f : 0.06f);
        Set(palette, "tint", Vector4.Zero);
        Raylib.SetShaderValueTexture(palette, Raylib.GetShaderLocation(palette, "lut"), settings.Palette == "ramps64" ? lutRamps64 : lutRamps);
        Raylib.DrawTextureRec(scene.Texture, new Rectangle(0, 0, Width, -Height), Vector2.Zero, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
        float w = Width * Scale, h = Height * Scale;
        float x = MathF.Floor((windowW - w) / 2f), y = MathF.Floor((windowH - h) / 2f);
        Raylib.DrawTexturePro(post.Texture, new Rectangle(0, 0, Width, -Height), new Rectangle(x, y, w, h), Vector2.Zero, 0, Color.White);
    }

    /// <summary>Sizes the retro view to the window: as near the chosen number of lines as a
    /// whole-pixel scale allows, the full width of the window.</summary>
    private void Fit(int windowW, int windowH)
    {
        windowW = Math.Max(windowW, 64);
        windowH = Math.Max(windowH, 64);
        int scale = Math.Max(1, (int)MathF.Round(windowH / (float)settings.RenderHeight));
        int w = Math.Max(64, windowW / scale), h = Math.Max(48, windowH / scale);
        if (w == Width && h == Height && scale == Scale && scene.Id != 0) return;
        if (scene.Id != 0) Raylib.UnloadRenderTexture(scene);
        if (post.Id != 0) Raylib.UnloadRenderTexture(post);
        Width = w;
        Height = h;
        Scale = scale;
        scene = Raylib.LoadRenderTexture(w, h);
        post = Raylib.LoadRenderTexture(w, h);
        Raylib.SetTextureFilter(scene.Texture, TextureFilter.Point);
        Raylib.SetTextureFilter(post.Texture, TextureFilter.Point);
    }

    /// <summary>
    /// The sky behind everything: a 360 degree picture that turns with the view and sits on
    /// the horizon, dimmed for night, when the stars come out, and greyed by rain and fog.
    /// </summary>
    public void DrawSky(float yaw, float pitch, float fov, float skyLight, float murk, Vector3 fog)
    {
        Raylib.ClearBackground(ToColor(fog));
        Texture2D picture = settings.Retro ? sky : softSky;
        float vfov = fov * MathF.PI / 180f;
        float focal = Height / 2f / MathF.Tan(vfov / 2f);
        float horizon = Height / 2f + MathF.Tan(pitch * MathF.PI / 180f) * focal;
        float hfov = 2f * MathF.Atan(MathF.Tan(vfov / 2f) * Width / Height) * 180f / MathF.PI;
        float texels = hfov / 360f * picture.Width;
        float texel = Width / texels;
        float tall = picture.Height * texel;
        float x0 = yaw / 360f * picture.Width - texels / 2f;
        Vector3 day = Vector3.Lerp(new Vector3(0.16f, 0.2f, 0.42f), Vector3.One, Math.Clamp(skyLight, 0f, 1f));
        day = Vector3.Lerp(day, new Vector3(0.62f, 0.64f, 0.68f) * Math.Max(skyLight, 0.25f), Math.Clamp(murk, 0f, 0.85f));
        Color tint = ToColor(day);
        Color top = settings.Retro
            ? new((byte)(78 * day.X), (byte)(112 * day.Y), (byte)(168 * day.Z), (byte)255)
            : new((byte)(92 * day.X), (byte)(132 * day.Y), (byte)(190 * day.Z), (byte)255);
        float picTop = horizon + 14 * texel - tall;
        if (picTop > 0) Raylib.DrawRectangle(0, 0, Width, (int)MathF.Ceiling(picTop) + 1, top);
        Raylib.DrawTexturePro(picture, new Rectangle(x0, 0, texels, picture.Height), new Rectangle(0, picTop, Width, tall), Vector2.Zero, 0, tint);
        if (skyLight < 0.6f && murk < 0.5f)
        {
            byte a = (byte)(255 * Math.Clamp((0.6f - skyLight) / 0.4f, 0f, 1f));
            float starTexels = hfov / 360f * stars.Width;
            float starTall = stars.Height * (Width / starTexels);
            Raylib.DrawTexturePro(stars, new Rectangle(yaw / 360f * stars.Width - starTexels / 2f, 0, starTexels, stars.Height),
                new Rectangle(0, horizon - starTall, Width, starTall), Vector2.Zero, 0, new Color((byte)255, (byte)255, (byte)255, a));
        }
    }

    public static Color ToColor(Vector3 c) =>
        new((byte)Math.Clamp(c.X * 255, 0, 255), (byte)Math.Clamp(c.Y * 255, 0, 255), (byte)Math.Clamp(c.Z * 255, 0, 255), (byte)255);

    public void Dispose()
    {
        Raylib.UnloadShader(world);
        Raylib.UnloadShader(sprite);
        Raylib.UnloadShader(palette);
        Raylib.UnloadShader(held);
        if (scene.Id != 0) Raylib.UnloadRenderTexture(scene);
        if (post.Id != 0) Raylib.UnloadRenderTexture(post);
        Raylib.UnloadTexture(lutRamps);
        Raylib.UnloadTexture(lutRamps64);
        Raylib.UnloadTexture(sky);
        Raylib.UnloadTexture(softSky);
        Raylib.UnloadTexture(stars);
    }
}
