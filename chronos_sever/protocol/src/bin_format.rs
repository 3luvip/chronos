//! Binary file format cho equipment registry và animation data.
//!
//! .eqp  = equipment/part registry
//! .anim = animation offset tables
//!
//! Header + Index table + Data blobs + String pool
//! Tất cả little-endian, không padding trừ align của struct.

use std::io::{self, Read, Write, BufReader, BufWriter};

/// Magic bytes: "CHNR" (Chronos)
pub const EQP_MAGIC:  u32 = 0x43484E52;
pub const ANIM_MAGIC: u32 = 0x43484E41;
pub const FORMAT_VER: u16 = 1;

/// Mỗi entry trong index table: 8 bytes
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct IndexEntry {
    pub id:     u32,   // sprite_id hoặc anim_id
    pub offset: u32,   // byte offset từ đầu data section
}

/// Một mảnh ghép nhân vật: 28 bytes, repr(C) để mmap an toàn
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct PartRecord {
    pub sprite_id:    u32,
    pub part_type:    u8,    // 0=legs 1=body 2=weapon 3=head 4=aura
    pub layer:        u8,
    pub _pad:         [u8; 2],
    pub name_offset:  u32,   // offset vào string pool
    pub def_bonus:    i16,   // defense bonus
    pub hp_bonus:     i16,   // hp bonus
    pub anim_block_offset: u32,  // offset vào AnimBlock section
    pub anim_block_count:  u8,
    pub _pad2:        [u8; 3],
}

/// Header của một animation block: 6 bytes + N×FrameOffset
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct AnimBlockHeader {
    pub anim_id:     u8,    // 0=idle 1=run 2=attack 3=jump 4=die
    pub frame_count: u8,
    pub _pad:        [u8; 2],
    // Theo sau ngay: frame_count × FrameOffset (2 bytes mỗi frame)
}

/// Offset một frame: 2 bytes — i8 đủ vì offset max ±127 pixel
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct FrameOffset {
    pub dx: i8,
    pub dy: i8,
}