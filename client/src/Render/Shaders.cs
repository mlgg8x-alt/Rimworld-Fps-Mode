namespace RimFps.Render;

/// <summary>
/// The GLSL behind the look. The 3D view is drawn small (a few hundred lines tall), lit in
/// a handful of hard steps that darken with distance (Doom's light diminishing), fogged in
/// steps, see-through only by a fixed 4 by 4 dither pattern (never blended), then pushed
/// through a 256 colour palette with ordered dithering, and blown up with no smoothing.
/// </summary>
public static class Shaders
{
    public const string WorldVertex = @"#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;
uniform mat4 mvp;
out vec2 fragUv;
out vec4 fragColor;
out float fragDepth;
void main()
{
    fragUv = vertexTexCoord;
    fragColor = vertexColor;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
    // Perspective w is the distance in front of the eye.
    fragDepth = gl_Position.w;
}
";

    /// <summary>Shared by the world (whose vertex colours carry lamp light, sky and shade:
    /// <c>encoded</c> 1) and the sprites (whose vertex colours are their light and tint
    /// already, alpha their fade: <c>encoded</c> 0).</summary>
    /// <summary>The fragment shader for held weapons and hands: drawn last, always in front.</summary>
    public static string HeldFragment => WorldFragment.Replace("#version 330", "#version 330\n#define HELD");

    public const string WorldFragment = @"#version 330
in vec2 fragUv;
in vec4 fragColor;
in float fragDepth;
uniform sampler2D texture0;
uniform float encoded;
uniform float fullbright;
uniform float skyLight;
uniform vec3 skyTint;
uniform vec3 lampTint;
uniform float ambient;
uniform float diminish;
uniform float levels;
uniform vec3 fogColor;
uniform float fogStart;
uniform float fogEnd;
uniform float flash;
uniform float fogSteps;
uniform float cutoff;
uniform vec3 tint;
out vec4 finalColor;

const float bayer[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);

void main()
{
    vec4 texel = texture(texture0, fragUv);
    // A held weapon's edges (vertex alpha 0) are walls of solid colour, never cut.
    bool edge = encoded > 1.5 && fragColor.a < 0.5;
    if (!edge && texel.a < cutoff) discard;
    // See-through only by dithering: a fading sprite drops more of its pixels.
    float fade = encoded > 0.5 ? 1.0 : fragColor.a;
    ivec2 cell = ivec2(gl_FragCoord.xy) & 3;
    float threshold = (bayer[cell.y * 4 + cell.x] + 0.5) / 16.0;
    if (fade < threshold) discard;

    vec3 light;
    if (fullbright > 0.5)
    {
        light = encoded > 0.5 ? vec3(1.0) : fragColor.rgb;
    }
    else
    {
        if (encoded > 1.5)
        {
            // A held weapon or hand: its own shade, lit as the player stands.
            light = fragColor.rgb * tint;
        }
        else if (encoded > 0.5)
        {
            vec3 lamp = fragColor.r * lampTint;
            vec3 sky = fragColor.g * skyLight * skyTint;
            light = max(max(lamp, sky), vec3(ambient)) * fragColor.b;
        }
        else
        {
            light = fragColor.rgb;
        }
        // Retro: further off is darker, as Doom's light diminishing...
        light *= 1.0 - clamp(fragDepth * diminish, 0.0, 0.62);
        // ...in hard steps, as its colour maps. RimWorld style: smooth.
        if (levels > 0.5)
        {
            float lum = max(light.r, max(light.g, light.b));
            float stepped = floor(lum * levels + 0.5) / levels;
            light = lum > 0.001 ? light * (stepped / lum) : vec3(0.0);
        }
    }
    vec3 colour = texel.rgb * light * 1.08;
    colour = mix(colour, vec3(1.0, 0.25, 0.15), flash);
    float fog = clamp((fragDepth - fogStart) / max(fogEnd - fogStart, 0.01), 0.0, 1.0);
    if (fogSteps > 0.5) fog = floor(fog * fogSteps + 0.5) / fogSteps;
    else fog = fog * fog * (3.0 - 2.0 * fog);
    finalColor = vec4(mix(colour, fogColor, fog), 1.0);
#ifdef HELD
    // Held things always in front of the world, keeping their own order.
    gl_FragDepth = gl_FragCoord.z * 0.0005;
#endif
}
";

    /// <summary>raylib's own 2D vertex shader, spelled out.</summary>
    public const string PlainVertex = @"#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;
uniform mat4 mvp;
out vec2 fragTexCoord;
out vec4 fragColor;
void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
";

    /// <summary>The whole 3D view through the palette, with ordered dithering first.</summary>
    public const string PaletteFragment = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0;
uniform sampler2D lut;
uniform float usePalette;
uniform float dither;
uniform float spread;
uniform vec4 tint;
out vec4 finalColor;

const float bayer[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);

void main()
{
    vec3 c = texture(texture0, fragTexCoord).rgb;
    c = mix(c, tint.rgb, tint.a);
    if (dither > 0.5)
    {
        ivec2 cell = ivec2(gl_FragCoord.xy) & 3;
        c += ((bayer[cell.y * 4 + cell.x] + 0.5) / 16.0 - 0.5) * spread;
    }
    c = clamp(c, 0.0, 1.0);
    if (usePalette > 0.5)
    {
        ivec3 i = ivec3(floor(c * 31.0 + 0.5));
        vec2 uv = vec2((float(i.b * 32 + i.r) + 0.5) / 1024.0, (float(i.g) + 0.5) / 32.0);
        c = texture(lut, uv).rgb;
    }
    finalColor = vec4(c, 1.0);
}
";
}
