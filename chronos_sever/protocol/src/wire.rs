
use byteorder::{LE, ReadBytesExt, WriteBytesExt};
use std::io::{self, Read, Write};
use half::f16;
/// Flags bitfield — chỉ gửi field nào thực sự thay đổi
pub mod DeltaFlags {
    pub const HAS_POS:   u8 = 0x01;
    pub const HAS_ANIM:  u8 = 0x02;
    pub const HAS_EQUIP: u8 = 0x04;
    pub const HAS_HP:    u8 = 0x08;
}

/// Min size: 5 B (flags + player_id). Max: ~45 B với tất cả fields.
/// So sánh JSON equivalent: ~120 B compressed / ~280 B raw.
pub struct PlayerDelta {
    pub player_id:   u32,
    pub flags:       u8,
    pub pos:         Option<(f16, f16)>,   // half-float: 4B tổng
    pub anim:        Option<AnimPacked>,   // 1 byte: 3-bit state | 5-bit frame
    pub equip:       Option<EquipPacked>,  // 3 bytes: u8 part | u16 sprite_id
    pub hp_pct:      Option<u8>,           // 0–255 mapped từ 0–100%
}

/// Đóng gói anim_state (0-4) và frame_index (0-31) vào 1 byte
#[derive(Clone, Copy)]
pub struct AnimPacked(pub u8);
impl AnimPacked {
    pub fn new(state: u8, frame: u8) -> Self {
        Self((state & 0x07) | ((frame & 0x1F) << 3))
    }
    pub fn state(self) -> u8 { self.0 & 0x07 }
    pub fn frame(self) -> u8 { (self.0 >> 3) & 0x1F }
}

#[derive(Clone, Copy)]
pub struct EquipPacked { pub part_type: u8, pub sprite_id: u16 }

impl PlayerDelta {
    pub fn write_to<W: Write>(&self, w: &mut W) -> io::Result<()> {
        w.write_u32::<LE>(self.player_id)?;
        w.write_u8(self.flags)?;

        if self.flags & DeltaFlags::HAS_POS != 0 {
            let (x, y) = self.pos.unwrap();
            w.write_u16::<LE>(x.to_bits())?;
            w.write_u16::<LE>(y.to_bits())?;
        }
        if self.flags & DeltaFlags::HAS_ANIM != 0 {
            w.write_u8(self.anim.unwrap().0)?;
        }
        if self.flags & DeltaFlags::HAS_EQUIP != 0 {
            let eq = self.equip.unwrap();
            w.write_u8(eq.part_type)?;
            w.write_u16::<LE>(eq.sprite_id)?;
        }
        if self.flags & DeltaFlags::HAS_HP != 0 {
            w.write_u8(self.hp_pct.unwrap())?;
        }
        Ok(())
    }

    pub fn read_from<R: Read>(r: &mut R) -> io::Result<Self> {
        let player_id = r.read_u32::<LE>()?;
        let flags     = r.read_u8()?;

        let pos = if flags & DeltaFlags::HAS_POS != 0 {
            let xb = r.read_u16::<LE>()?;
            let yb = r.read_u16::<LE>()?;
            Some((f16::from_bits(xb), f16::from_bits(yb)))
        } else { None };

        let anim = if flags & DeltaFlags::HAS_ANIM != 0 {
            Some(AnimPacked(r.read_u8()?))
        } else { None };

        let equip = if flags & DeltaFlags::HAS_EQUIP != 0 {
            Some(EquipPacked {
                part_type: r.read_u8()?,
                sprite_id: r.read_u16::<LE>()?,
            })
        } else { None };

        let hp_pct = if flags & DeltaFlags::HAS_HP != 0 {
            Some(r.read_u8()?)
        } else { None };

        Ok(Self { player_id, flags, pos, anim, equip, hp_pct })
    }
}

// ── Batch broadcast: nhiều delta trong một frame ──────────────────────────

/// Gom tất cả delta của tick hiện tại vào một packet duy nhất.
/// Format: u16 count | delta_0 | delta_1 | ...
pub fn write_batch<W: Write>(
    w:      &mut W,
    deltas: &[PlayerDelta],
) -> io::Result<()> {
    w.write_u16::<LE>(deltas.len() as u16)?;
    for d in deltas { d.write_to(w)?; }
    Ok(())
}