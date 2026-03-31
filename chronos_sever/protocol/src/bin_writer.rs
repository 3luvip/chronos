use super::bin_format::*;
use byteorder::{LE, WriteBytesExt};
use std::collections::HashMap;
use std::io::{self, Seek, SeekFrom, Write};

pub struct EqpWriter<W: Write + Seek> {
    w:          W,
    string_pool: Vec<u8>,
    str_offsets: HashMap<String, u32>,
}

impl<W: Write + Seek> EqpWriter<W> {
    pub fn new(w: W) -> Self {
        Self { w, string_pool: Vec::new(), str_offsets: HashMap::new() }
    }

    /// intern string vào pool, trả về offset
    fn intern(&mut self, s: &str) -> u32 {
        if let Some(&off) = self.str_offsets.get(s) {
            return off;
        }
        let off = self.string_pool.len() as u32;
        self.string_pool.extend_from_slice(s.as_bytes());
        self.string_pool.push(0); // null terminator
        self.str_offsets.insert(s.to_owned(), off);
        off
    }

    pub fn write_file(&mut self, parts: &[EquipmentDef]) -> io::Result<()> {
        let count = parts.len() as u32;

        // ── Header ──
        self.w.write_u32::<LE>(EQP_MAGIC)?;
        self.w.write_u16::<LE>(FORMAT_VER)?;
        self.w.write_u16::<LE>(0)?;       // flags (reserved)
        self.w.write_u32::<LE>(count)?;

        // ── Index table placeholder (sẽ seek back để fill) ──
        let index_pos = self.w.stream_position()?;
        for _ in 0..count {
            self.w.write_u64::<LE>(0)?;   // 8 bytes placeholder
        }

        // ── Data blobs ──
        let data_base = self.w.stream_position()? as u32;
        let mut entries: Vec<IndexEntry> = Vec::with_capacity(parts.len());

        for def in parts {
            let blob_start = (self.w.stream_position()? as u32) - data_base;
            let name_off   = self.intern(&def.name);

            // Serialize FrameOffsets trước để tính offset
            let anim_data = build_anim_blobs(&def.animations);

            let rec = PartRecord {
                sprite_id:         def.sprite_id,
                part_type:         def.part_type,
                layer:             def.layer,
                _pad:              [0; 2],
                name_offset:       name_off,
                def_bonus:         def.def_bonus,
                hp_bonus:          def.hp_bonus,
                anim_block_offset: std::mem::size_of::<PartRecord>() as u32,
                anim_block_count:  def.animations.len() as u8,
                _pad2:             [0; 3],
            };

            // Safe: PartRecord є repr(C), no padding, known size
            let bytes = unsafe {
                std::slice::from_raw_parts(
                    &rec as *const PartRecord as *const u8,
                    std::mem::size_of::<PartRecord>(),
                )
            };
            self.w.write_all(bytes)?;
            self.w.write_all(&anim_data)?;

            entries.push(IndexEntry {
                id:     def.sprite_id,
                offset: blob_start,
            });
        }

        // ── String pool ──
        let string_pool_offset = self.w.stream_position()?;
        self.w.write_all(&self.string_pool)?;

        // ── Seek back: fill index table ──
        self.w.seek(SeekFrom::Start(index_pos))?;
        for entry in &entries {
            self.w.write_u32::<LE>(entry.id)?;
            self.w.write_u32::<LE>(entry.offset)?;
        }

        Ok(())
    }
}

fn build_anim_blobs(animations: &[AnimDef]) -> Vec<u8> {
    let mut out = Vec::new();
    for anim in animations {
        out.push(anim.anim_id);
        out.push(anim.frames.len() as u8);
        out.push(0); out.push(0); // pad
        for frame in &anim.frames {
            out.push(frame.dx as u8);   // i8 → u8 bitcast
            out.push(frame.dy as u8);
        }
    }
    out
}

// ── Input types (từ editor/tool) ──────────────────────────────────────────

pub struct EquipmentDef {
    pub sprite_id: u32, pub name: String,
    pub part_type: u8,  pub layer: u8,
    pub def_bonus: i16, pub hp_bonus: i16,
    pub animations: Vec<AnimDef>,
}
pub struct AnimDef { pub anim_id: u8, pub frames: Vec<FrameOff> }
pub struct FrameOff { pub dx: i8, pub dy: i8 }