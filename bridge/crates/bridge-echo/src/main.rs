//! Stand-in for the IW4L side of the bridge, for testing the RimWorld mod on its own.
//!
//!     cargo run -p bridge-echo                  # listens on 127.0.0.1:47820
//!     cargo run -p bridge-echo -- 127.0.0.1:9000
//!
//! Then load a RimWorld save with the mod enabled: you should see a hello and a tick
//! counter every second, then a snapshot summary every second. The real IW4L plugin speaks
//! the same protocol.

use std::io::{BufRead, BufReader, Write};
use std::net::{TcpListener, TcpStream};

use rwmw2_protocol::{DEFAULT_ADDR, Message, PROTOCOL_VERSION, Side};

fn main() -> std::io::Result<()> {
    let addr = std::env::args()
        .nth(1)
        .or_else(|| std::env::var("RWMW2_BRIDGE_ADDR").ok())
        .unwrap_or_else(|| DEFAULT_ADDR.to_string());
    let listener = TcpListener::bind(&addr)?;
    println!("bridge-echo listening on {addr}; start RimWorld with the mod enabled");
    for stream in listener.incoming() {
        match stream {
            Ok(stream) => {
                if let Err(e) = serve(stream) {
                    eprintln!("connection ended: {e}");
                }
            }
            Err(e) => eprintln!("accept failed: {e}"),
        }
    }
    Ok(())
}

fn serve(stream: TcpStream) -> std::io::Result<()> {
    let peer = stream.peer_addr()?;
    println!("connected: {peer}");
    let mut writer = stream.try_clone()?;
    let mut last_snapshot: Option<std::time::Instant> = None;
    for line in BufReader::new(stream).lines() {
        let line = line?;
        match Message::from_line(&line) {
            Ok(Message::Hello { side, protocol, game_version, .. }) => {
                println!("hello from {side:?}, protocol {protocol}, game {}", game_version.unwrap_or_default());
                if protocol != PROTOCOL_VERSION {
                    eprintln!("warning: protocol mismatch, this side speaks {PROTOCOL_VERSION}");
                }
                let reply = Message::Hello { side: Side::Iw4l, protocol: PROTOCOL_VERSION, game_version: None, window: None };
                writer.write_all(reply.to_line().as_bytes())?;
            }
            Ok(Message::Tick { tick }) => println!("tick {tick}"),
            // About ten a second: summarised once a second.
            Ok(Message::Snapshot { tick, map, pawns, player_pawn, .. }) => {
                if last_snapshot.is_none_or(|at| at.elapsed().as_secs_f32() >= 1.0) {
                    last_snapshot = Some(std::time::Instant::now());
                    println!("{}", snapshot_summary(tick, map, &pawns, player_pawn.as_deref()));
                }
            }
            Ok(other) => println!("{other:?}"),
            Err(e) => eprintln!("unparsed line ({e}): {line}"),
        }
    }
    println!("disconnected: {peer}");
    Ok(())
}

fn snapshot_summary(tick: i64, map: Option<[i32; 2]>, pawns: &[rwmw2_protocol::PawnState], player: Option<&str>) -> String {
    let count = |faction: &str| pawns.iter().filter(|p| p.faction == faction).count();
    let map = map.map_or_else(|| "?".to_owned(), |[x, z]| format!("{x}x{z}"));
    format!(
        "snapshot tick {tick}: map {map}, {} pawns ({} player, {} hostile, {} neutral), MW2 plays {}",
        pawns.len(),
        count("player"),
        count("hostile"),
        count("neutral"),
        player.unwrap_or("nobody"),
    )
}
