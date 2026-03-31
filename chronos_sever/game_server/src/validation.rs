use crate::player::{Player, Vec2};

const MAX_DIST_PER_TICK: f32 = 0.5;  // tiles per 20ms tick at max speed
const MAX_SPEED_TILES_PER_S: f32 = 8.0;

#[derive(Debug)]
pub enum ValidationError {
    TeleportDetected { expected_max: f32, actual: f32 },
    AttackOnCooldown { remaining_ms: u64 },
    InvalidTargetRange,
    DeadPlayerAction,
}

pub fn validate_move(
    player: &Player,
    new_pos: Vec2,
    now_ms: u64,
    last_tick_ms: u64,
) -> Result<Vec2, ValidationError> {
    if player.stats.hp <= 0 {
        return Err(ValidationError::DeadPlayerAction);
    }

    let elapsed_s   = (now_ms - last_tick_ms) as f32 / 1000.0;
    // Cho phép thêm 20% tolerance cho network jitter
    let max_dist    = player.stats.move_speed * elapsed_s * 1.2;
    let actual_dist = player.position.dist(new_pos);

    if actual_dist > max_dist {
        return Err(ValidationError::TeleportDetected {
            expected_max: max_dist,
            actual: actual_dist,
        });
    }
    Ok(new_pos)
}

pub fn validate_attack(
    attacker: &Player,
    target: &Player,
    now_ms: u64,
    max_range: f32,
) -> Result<(), ValidationError> {
    if attacker.stats.hp <= 0 { return Err(ValidationError::DeadPlayerAction); }

    let since_last = now_ms - attacker.stats.last_attack_ms;
    if since_last < attacker.stats.attack_cooldown_ms {
        return Err(ValidationError::AttackOnCooldown {
            remaining_ms: attacker.stats.attack_cooldown_ms - since_last,
        });
    }

    if attacker.position.dist(target.position) > max_range {
        return Err(ValidationError::InvalidTargetRange);
    }

    Ok(())
}