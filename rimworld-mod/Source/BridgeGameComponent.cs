using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// Main-thread side of the bridge. RimWorld creates one per game automatically.
    /// M0: send hello on connect and a tick counter once per second; log anything received.
    /// M1: send a pawn snapshot about ten times a second, paused or not.
    /// M2: move the colonist the client plays (<see cref="PlayerPawn"/>) where the client says, report every
    /// bullet fired (<see cref="ShotReport"/>) and fire the FPS player's (<see cref="PlayerShots"/>).
    /// M3: send the map's terrain (<see cref="TerrainReport"/>).
    /// M4: send what the played colonist can loot and apply the client's looting (<see cref="LootReport"/>).
    /// M5: send roofs, wall and floor art, and everything built (<see cref="WorldReport"/>).
    /// M8: the played colonist is struck only by bullets the client says met the player (<see cref="DodgeableShots"/>).
    /// M9: RimWorld's camera follows the FPS player and feeds the client's minimap; Tab switches between
    /// the two games' windows (<see cref="RimView"/>).
    /// M10: people are drawn as RimWorld draws them (<see cref="PawnArt"/>) and what grows carries
    /// RimWorld's own pictures (<see cref="FloraReport"/>).
    /// M11: doors open as the played colonist walks up to them (<see cref="DoorReport"/>).
    /// M12: RimWorld is where the game lives: "Play in first person" on a drafted colonist hands them
    /// to the client (<see cref="ControlGizmo"/>); E in the client opens doors and flicks switches
    /// (<see cref="Activate"/>); people flash when hurt (<see cref="Hurt"/>).
    /// </summary>
    public class BridgeGameComponent : GameComponent
    {
        private const int TickReportInterval = 60; // 60 ticks = 1 second at 1x speed
        private const float SnapshotSeconds = 0.1f; // real time, so pausing and speed don't change it

        // Static so loading another save replaces the old connection instead of leaking it.
        private static BridgeClient client;
        private bool helloSent;
        private float lastSnapshot = -1f;

        public BridgeGameComponent(Game game) { }

        /// <summary>RimWorld is quitting (M19): its window put back from being the minimap and
        /// the connection closed before Unity shuts down, which crashed with it still open.</summary>
        internal static void Shutdown()
        {
            RimView.Drop();
            client?.Dispose();
            client = null;
        }

        /// <summary>The client is connected.</summary>
        internal static bool Linked => client != null && client.Connected;

        public override void FinalizeInit()
        {
            if (BridgeMod.Settings.AutoConnect) Connect();
        }

        public void Connect()
        {
            client?.Dispose();
            client = new BridgeClient(BridgeMod.Settings.Host, BridgeMod.Settings.Port);
            helloSent = false;
            TerrainReport.Reset();
            WorldReport.Reset();
            LootReport.Reset();
            LookReport.Reset();
            FloraReport.Reset();
            PawnArt.Reset();
            DoorReport.Reset();
            EffectsReport.Reset();
            WeatherReport.Reset();
            FleckReport.Reset();
            HeldReport.Reset();
            ShotReport.Send = line =>
            {
                if (client != null && client.Connected) client.Send(line);
            };
        }

        public override void GameComponentTick()
        {
            if (client == null || !client.Connected || !helloSent) return;
            int tick = Find.TickManager.TicksGame;
            if (tick % TickReportInterval == 0)
                client.Send(Json.Msg("tick").Num("tick", tick).ToString());
        }

        public override void GameComponentUpdate()
        {
            // Keep status logging alive while the game is paused (no ticks run then).
            if (client == null) return;
            DrainStatus();
            // Everything but the tick goes from here, not from ticks, so it keeps flowing while
            // RimWorld is paused (it pauses itself on loading a save): the client still sees the pawns,
            // the map and who is drafted, and still moves its colonist.
            if (!client.Connected)
            {
                helloSent = false;
                TerrainReport.Reset();
                WorldReport.Reset();
                LootReport.Reset();
                LookReport.Reset();
                FloraReport.Reset();
                PawnArt.Reset();
                DoorReport.Reset();
                EffectsReport.Reset();
                WeatherReport.Reset();
                FleckReport.Reset();
                RimView.Drop();
                return;
            }
            if (!helloSent)
            {
                KeepRunningInBackground();
                client.Send(Json.Msg("hello").Str("side", "rimworld").Num("protocol", 0)
                    .Str("game_version", VersionControl.CurrentVersionStringWithRev)
                    .Str("window", RimView.OwnWindow().ToString()).ToString());
                helloSent = true;
            }
            while (client.Inbound.TryDequeue(out var line))
                Handle(line);
            foreach (string line in TerrainReport.Poll())
                client.Send(line);
            foreach (string line in WorldReport.Poll())
                client.Send(line);
            string loot = LootReport.Poll();
            if (loot != null) client.Send(loot);
            string looks = LookReport.Poll();
            if (looks != null) SendAfterArt(looks);
            string flora = FloraReport.Poll();
            if (flora != null) SendAfterArt(flora);
            string doors = DoorReport.Poll();
            if (doors != null) client.Send(doors);
            foreach (string line in EffectsReport.Poll())
                client.Send(line);
            string weather = WeatherReport.PollWeather();
            if (weather != null) client.Send(weather);
            string filth = WeatherReport.PollFilth();
            if (filth != null) SendAfterArt(filth);
            string flecks = FleckReport.Take();
            if (flecks != null) SendAfterArt(flecks);
            RimView.Update(client.Send);
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - lastSnapshot < SnapshotSeconds) return;
            lastSnapshot = now;
            string snapshot = PawnSnapshot.Build();
            if (snapshot != null) SendAfterArt(snapshot);
        }

        /// <summary>Sends <paramref name="line"/>, after any art drawn for it (M13).</summary>
        private void SendAfterArt(string line)
        {
            string textures = WorldReport.NewTextures();
            if (textures != null) client.Send(textures);
            client.Send(line);
        }

        /// <summary>
        /// RimWorld pauses when its window loses focus unless "Run in background" is on, and
        /// playing in the client always takes focus away. Turned on once while the bridge is connected.
        /// </summary>
        private static void KeepRunningInBackground()
        {
            if (Prefs.RunInBackground) return;
            Prefs.RunInBackground = true;
            Log.Message("[RimWorldFPS] turned on Options > Run in background so RimWorld keeps running while the client has focus");
        }

        private void Handle(string line)
        {
            JsonLine message = JsonLine.Parse(line);
            switch (message?.Type)
            {
                case "player_move":
                    string pawn = message.Str("pawn");
                    float? x = message.Num("x"), z = message.Num("z"), yaw = message.Num("yaw");
                    if (pawn != null && x.HasValue && z.HasValue && yaw.HasValue)
                        PlayerPawn.Move(pawn, x.Value, z.Value, yaw.Value);
                    break;
                case "player_shot":
                    float[] from = message.Nums("from"), to = message.Nums("to");
                    if (message.Str("pawn") != null && from?.Length == 3 && to?.Length == 3)
                        PlayerShots.Fired(message.Str("pawn"), new Vector3(from[0], 0f, from[2]), new Vector3(to[0], 0f, to[2]),
                            message.Str("target"), message.Str("part"), message.Num("damage") ?? 0f);
                    break;
                case "spectate":
                    float? sx = message.Num("x"), sz = message.Num("z");
                    if (sx.HasValue && sz.HasValue) RimView.Spectate(sx.Value, sz.Value);
                    break;
                case "hello":
                    RimView.Window(message.Str("window"));
                    Log.Message($"[RimWorldFPS] <- {line}");
                    break;
                case "player_melee":
                    if (message.Str("pawn") != null && message.Str("target") != null)
                        PlayerMelee.Strike(message.Str("pawn"), message.Str("target"));
                    break;
                case "player_throw":
                    float[] at = message.Nums("to");
                    if (message.Str("pawn") != null && at?.Length == 2) Throws.Throw(message.Str("pawn"), at[0], at[1]);
                    break;
                case "use":
                    if (message.Str("pawn") != null) Activate.Use(message.Str("pawn"));
                    break;
                case "rimworld_shown":
                    RimView.Shown(message.Str("back"));
                    break;
                case "menu":
                    RimView.Menu(message.Str("open") == "true");
                    break;
                case "player_struck":
                    if (message.Str("pawn") != null && message.Str("shot") != null)
                        DodgeableShots.Struck(message.Str("pawn"), message.Str("shot"), message.Str("part"));
                    break;
                case "loot_take":
                    if (message.Str("pawn") != null && message.Str("item") != null)
                        LootReport.Take(message.Str("pawn"), message.Str("item"));
                    break;
                case "loot_drop":
                    if (message.Str("pawn") != null && message.Str("item") != null)
                        LootReport.Drop(message.Str("pawn"), message.Str("item"));
                    break;
                default:
                    Log.Message($"[RimWorldFPS] <- {line}");
                    break;
            }
        }

        private void DrainStatus()
        {
            while (client.StatusMessages.TryDequeue(out var status))
                Log.Message($"[RimWorldFPS] bridge {status}");
        }
    }
}
