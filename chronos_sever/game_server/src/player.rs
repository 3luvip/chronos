use serde::{Deserialize, Serialize};

/// Mỗi player là một entity với các component tách biệt
#[derive(Debug, Clone)]
pub struct Player {
    pub id:        u32,
    pub position:  Vec2,
    pub velocity:  Vec2,
    pub anim:      AnimState,
    pub equipment: Equipment,
    pub stats:     Stats,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum AnimState { Idle, Run, Attack, Jump, Die }

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Equipment {
    pub head:   Option<u32>,   // sprite_id
    pub body:   Option<u32>,
    pub legs:   Option<u32>,
    pub weapon: Option<u32>,
    pub aura:   Option<u32>,
}

#[derive(Debug, Clone)]
pub struct Stats {
    pub hp:          i32,
    pub max_hp:      i32,
    pub move_speed:  f32,  // tiles/s — used for server-side cheat detection
    pub attack_cooldown_ms: u64,
    pub last_attack_ms:     u64,
}

#[derive(Debug, Clone, Copy)]
pub struct Vec2 { pub x: f32, pub y: f32 }

impl Vec2 {
    pub fn dist(self, other: Vec2) -> f32 {
        let dx = self.x - other.x;
        let dy = self.y - other.y;
        (dx * dx + dy * dy).sqrt()
    }
}