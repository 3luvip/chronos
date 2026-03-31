use super::bin_format::*;
use memmap2::Mmap;
use std::collections::HashMap;
use std::fs::File;

/// Registry load-once, zero-copy.
/// Toàn bộ dữ liệu nằm trong mmap — không allocate heap cho từng PartRecord.
pub struct EquipRegistry {
    _mmap:   Mmap,                          // giữ file mở
    index:   HashMap<u32, *const PartRecord>, // sprite_id → con trỏ vào mmap
}

// Safe vì mmap sống suốt lifetime của Registry và không bao giờ mutate
unsafe impl Send for EquipRegistry {}
unsafe impl Sync for EquipRegistry {}

impl EquipRegistry {
    pub fn load(path: &str) -> Result<Self, Box<dyn std::error::Error>> {
        let file  = File::open(path)?;
        let mmap  = unsafe { Mmap::map(&file)? };
        let bytes = mmap.as_ptr();

        // Validate magic + version
        let magic = u32::from_le_bytes(mmap[0..4].try_into()?);
        if magic != EQP_MAGIC { return Err("invalid magic".into()); }
        let ver = u16::from_le_bytes(mmap[4..6].try_into()?);
        if ver  != FORMAT_VER  { return Err("version mismatch".into()); }

        let count = u32::from_le_bytes(mmap[8..12].try_into()?) as usize;

        // Index table bắt đầu tại offset 12 (header = 4+2+2+4 = 12 bytes)
        let index_base = 12_usize;
        let data_base  = index_base + count * 8;

        let mut index = HashMap::with_capacity(count);
        for i in 0..count {
            let off = index_base + i * 8;
            let id  = u32::from_le_bytes(mmap[off..off+4].try_into()?);
            let pos = u32::from_le_bytes(mmap[off+4..off+8].try_into()?) as usize;
            let ptr = unsafe {
                (bytes.add(data_base + pos)) as *const PartRecord
            };
            index.insert(id, ptr);
        }

        Ok(Self { _mmap: mmap, index })
    }

    /// O(1) lookup — trả về tham chiếu vào vùng nhớ mmap, zero-copy
    pub fn get(&self, sprite_id: u32) -> Option<&PartRecord> {
        self.index.get(&sprite_id).map(|&ptr| unsafe { &*ptr })
    }

    /// Lấy tất cả frame offsets cho một anim (zero-copy slice)
    pub fn get_offsets(&self, sprite_id: u32, anim_id: u8)
        -> Option<&[FrameOffset]>
    {
        let rec = self.get(sprite_id)?;
        let base = rec as *const PartRecord as *const u8;
        let mut cursor = std::mem::size_of::<PartRecord>();

        for _ in 0..rec.anim_block_count {
            let hdr = unsafe {
                &*(base.add(cursor) as *const AnimBlockHeader)
            };
            cursor += std::mem::size_of::<AnimBlockHeader>();
            let frames = unsafe {
                std::slice::from_raw_parts(
                    base.add(cursor) as *const FrameOffset,
                    hdr.frame_count as usize,
                )
            };
            if hdr.anim_id == anim_id {
                return Some(frames);
            }
            cursor += hdr.frame_count as usize * 2;
        }
        None
    }

    pub fn name_of<'a>(
        &self,
        sprite_id: u32,
        file_bytes: &'a [u8],
    ) -> Option<&'a str> {
        let rec = self.get(sprite_id)?;
        let start = rec.name_offset as usize;

        let slice = file_bytes.get(start..)?;
        let end = slice.iter().position(|&b| b == 0)?;

        std::str::from_utf8(&slice[..end]).ok()
    }
}