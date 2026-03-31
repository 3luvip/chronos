use std::collections::HashMap;
use std::sync::Arc;

use tokio::sync::{RwLock, broadcast};
use tokio::time::{interval, Duration};

const TICK_MS: u64 = 20; // 50Hz

// =======================
// Player
// =======================
#[derive(Clone)]
pub struct Player {
    pub id: u32,
    pub position: (f32, f32),
    pub velocity: (f32, f32),
    pub anim: u8,
    pub equipment: Option<EquipDelta>,
    pub stats: u32,

    pub dirty: bool, // 👈 thêm để track thay đổi
}

// =======================
// Delta structs
// =======================
#[derive(Clone, serde::Serialize)]
pub struct PlayerDelta {
    pub player_id: u32,
    pub position: Option<(f32, f32)>,
    pub anim_state: Option<u8>,
    pub equip_change: Option<EquipDelta>,
}

#[derive(Clone, serde::Serialize)]
pub struct EquipDelta {
    pub part_type: u8,
    pub sprite_id: u32,
}

// Convert Player -> PlayerDelta
impl From<&Player> for PlayerDelta {
    fn from(p: &Player) -> Self {
        Self {
            player_id: p.id,
            position: Some(p.position),
            anim_state: Some(p.anim),
            equip_change: p.equipment.clone(),
        }
    }
}

// =======================
// Broadcast handle
// =======================
#[derive(Clone)]
pub struct BroadcastHandle {
    sender: broadcast::Sender<Vec<PlayerDelta>>,
}

impl BroadcastHandle {
    pub fn new(buffer: usize) -> Self {
        let (sender, _) = broadcast::channel(buffer);
        Self { sender }
    }

    pub fn subscribe(&self) -> broadcast::Receiver<Vec<PlayerDelta>> {
        self.sender.subscribe()
    }

    pub async fn send_all(&self, data: &[PlayerDelta]) {
        let _ = self.sender.send(data.to_vec()); // clone 1 lần
    }
}

// =======================
// World Tick
// =======================
pub async fn run_world_tick(
    players: Arc<RwLock<HashMap<u32, Player>>>,
    broadcast: BroadcastHandle,
) {
    let mut ticker = interval(Duration::from_millis(TICK_MS));

    loop {
        ticker.tick().await;

        // 🔥 FIX: dùng 1 write lock luôn (tránh race)
        let snapshot = {
            let mut ps = players.write().await;

            let mut snapshot = Vec::with_capacity(128);

            for p in ps.values_mut() {
                if p.dirty {
                    snapshot.push(PlayerDelta::from(&*p));
                    p.dirty = false; // clear ngay tại đây
                }
            }

            snapshot
        };

        if !snapshot.is_empty() {
            broadcast.send_all(&snapshot).await;
        }
    }
}