use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;
use tokio::time::{interval, Duration};
use crate::player::Player;
use crate::broadcast::BroadcastHandle;

const TICK_MS: u64 = 20;   // 50 Hz

pub async fn run_world_tick(
    players: Arc<RwLock<HashMap<u32, Player>>>,
    broadcast: BroadcastHandle,
) {
    let mut ticker = interval(Duration::from_millis(TICK_MS));

    loop {
        ticker.tick().await;

        let snapshot = {
            let ps = players.read().await;
            // Ghi lại delta: chỉ những player có state thay đổi
            ps.values()
              .filter(|p| p.dirty)   // dirty flag set khi có input
              .map(|p| PlayerDelta::from(p))
              .collect::<Vec<_>>()
        };

        if !snapshot.is_empty() {
            // Delta-compressed: chỉ gửi những gì thay đổi
            broadcast.send_all(&snapshot).await;

            // Clear dirty flags sau khi broadcast
            let mut ps = players.write().await;
            for delta in &snapshot {
                if let Some(p) = ps.get_mut(&delta.player_id) {
                    p.dirty = false;
                }
            }
        }
    }
}

#[derive(serde::Serialize)]
pub struct PlayerDelta {
    pub player_id:    u32,
    pub position:     Option<(f32, f32)>,   // None nếu không đổi
    pub anim_state:   Option<u8>,
    pub equip_change: Option<EquipDelta>,
}

#[derive(serde::Serialize)]
pub struct EquipDelta {
    pub part_type: u8,   // 0=head,1=body,2=legs,3=weapon,4=aura
    pub sprite_id: u32,
}