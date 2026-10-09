using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M21: RimWorld's flecks (puffs of dust and smoke, sparks, splashes, footprints, blood
    /// spurts) for MW2 to draw: each made on the map in view near the MW2 player is sent once,
    /// as it starts, in a <c>flecks</c> batch each frame; MW2 moves, turns, grows and fades it
    /// from there as RimWorld would.
    /// </summary>
    [HarmonyPatch(typeof(FleckManager), nameof(FleckManager.CreateFleck))]
    public static class FleckReport
    {
        /// <summary>Cells from the MW2 player within which flecks are sent.</summary>
        private const float Reach = 40f;
        /// <summary>Most flecks sent a frame; more are dropped (a big fire makes plenty).</summary>
        private const int MaxPerFrame = 80;
        /// <summary>Cells above the ground a fleck that RimWorld draws over things floats.</summary>
        private const float Overhead = 0.4f;

        private static readonly StringBuilder batch = new StringBuilder();
        private static int count;
        private static int frame = -1;
        private static Vector3? centre;

        public static void Reset()
        {
            batch.Length = 0;
            count = 0;
        }

        /// <summary>The <c>flecks</c> made since the last call, or null.</summary>
        public static string Take()
        {
            if (count == 0) return null;
            string line = Json.Msg("flecks").Raw("flecks", "[" + batch + "]").ToString();
            Reset();
            return line;
        }

        public static void Postfix(FleckManager __instance, FleckCreationData fleckData)
        {
            try
            {
                if (!BridgeGameComponent.Linked || count >= MaxPerFrame) return;
                FleckDef def = fleckData.def;
                if (def == null || def.realTime || def.altitudeLayer >= AltitudeLayer.Weather) return;
                Map map = __instance.parent;
                if (map == null || map != PawnSnapshot.MapInView()) return;
                if (frame != Time.frameCount)
                {
                    frame = Time.frameCount;
                    centre = RimView.Target(map);
                }
                Vector3 at = fleckData.spawnPosition;
                if (!centre.HasValue || (at - centre.Value).MagnitudeHorizontalSquared() > Reach * Reach) return;
                GraphicData data = def.graphicData ?? (def.randomGraphics.NullOrEmpty() ? null : def.randomGraphics[0]);
                string shader = data?.shaderType?.defName ?? "";
                if (data == null || shader.Contains("Distort") || shader.Contains("Glow")) return;
                int tex = TextureExport.Of(data.Graphic?.MatSingle, cutout: true);
                if (tex < 0) return;
                Vector2 drawSize = data.drawSize;
                float scale = fleckData.exactScale?.x ?? fleckData.scale;
                Vector3 velocity = Quaternion.AngleAxis(fleckData.velocityAngle, Vector3.up) * Vector3.forward * fleckData.velocitySpeed;
                if (fleckData.velocity.HasValue) velocity += fleckData.velocity.Value;
                Color color = fleckData.instanceColor ?? Color.white;
                bool flat = def.altitudeLayer <= AltitudeLayer.MoteLow;
                if (count > 0) batch.Append(',');
                batch.Append("{\"tex\":").Append(tex)
                    .Append(",\"x\":").Append(F(at.x)).Append(",\"z\":").Append(F(at.z));
                if (!flat) batch.Append(",\"y\":").Append(F(Overhead));
                batch.Append(",\"size\":").Append(F(Mathf.Abs(scale) * drawSize.x));
                if (def.growthRate != 0f) batch.Append(",\"grow\":").Append(F(def.growthRate * drawSize.x));
                float rot = def.rotateTowardsMoveDirection && velocity != Vector3.zero
                    ? velocity.AngleFlat() + def.rotateTowardsMoveDirectionExtraAngle
                    : fleckData.rotation;
                if (rot != 0f) batch.Append(",\"rot\":").Append(F(rot));
                if (fleckData.rotationRate != 0f) batch.Append(",\"spin\":").Append(F(fleckData.rotationRate));
                if (velocity.x != 0f) batch.Append(",\"vx\":").Append(F(velocity.x));
                if (velocity.z != 0f) batch.Append(",\"vz\":").Append(F(velocity.z));
                if (def.fadeInTime > 0f) batch.Append(",\"fade_in\":").Append(F(def.fadeInTime));
                batch.Append(",\"solid\":").Append(F(fleckData.solidTimeOverride ?? def.solidTime));
                if (def.fadeOutTime > 0f) batch.Append(",\"fade_out\":").Append(F(def.fadeOutTime));
                if (color != Color.white)
                    batch.Append(",\"color\":[").Append(F(color.r)).Append(',').Append(F(color.g)).Append(',')
                        .Append(F(color.b)).Append(',').Append(F(color.a)).Append(']');
                if (flat) batch.Append(",\"flat\":true");
                batch.Append('}');
                count++;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[RimWorldMW2] fleck " + fleckData.def?.defName + ": " + e.Message, 0x3f1e);
            }
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
