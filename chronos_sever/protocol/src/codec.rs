use std::io::{Error, ErrorKind, Result};

#[derive(Default)]
pub struct PacketWriter {
    buf: Vec<u8>,
}

impl PacketWriter {
    pub fn write_u8(&mut self, v: u8) {
        self.buf.push(v);
    }

    pub fn write_bool(&mut self, v: bool) {
        self.buf.push(if v { 1 } else { 0 });
    }

    pub fn write_i32(&mut self, v: i32) {
        self.buf.extend_from_slice(&v.to_be_bytes());
    }

    pub fn write_i64(&mut self, v: i64) {
        self.buf.extend_from_slice(&v.to_be_bytes());
    }

    pub fn write_u64(&mut self, v: u64) {
        self.buf.extend_from_slice(&v.to_be_bytes());
    }

    pub fn write_utf(&mut self, s: &str) -> Result<()> {
        let b = s.as_bytes();
        if b.len() > u16::MAX as usize {
            return Err(Error::new(ErrorKind::InvalidData, "string too long"));
        }
        self.buf.extend_from_slice(&(b.len() as u16).to_be_bytes());
        self.buf.extend_from_slice(b);
        Ok(())
    }

    pub fn into_inner(self) -> Vec<u8> {
        self.buf
    }
}

pub struct PacketReader {
    buf: Vec<u8>,
    pos: usize,
}

impl PacketReader {
    pub fn new(buf: Vec<u8>) -> Self {
        Self { buf, pos: 0 }
    }

    pub fn read_u8(&mut self) -> Result<u8> {
        let data = self.read_exact(1)?;
        Ok(data[0])
    }

    pub fn read_bool(&mut self) -> Result<bool> {
        Ok(self.read_u8()? != 0)
    }

    pub fn read_i32(&mut self) -> Result<i32> {
        let data = self.read_exact(4)?;
        Ok(i32::from_be_bytes([data[0], data[1], data[2], data[3]]))
    }

    pub fn read_i64(&mut self) -> Result<i64> {
        let data = self.read_exact(8)?;
        Ok(i64::from_be_bytes([
            data[0], data[1], data[2], data[3],
            data[4], data[5], data[6], data[7],
        ]))
    }

    pub fn read_u64(&mut self) -> Result<u64> {
        let data = self.read_exact(8)?;
        Ok(u64::from_be_bytes([
            data[0], data[1], data[2], data[3],
            data[4], data[5], data[6], data[7],
        ]))
    }

    pub fn read_utf(&mut self) -> Result<String> {
        let len_bytes = self.read_exact(2)?;
        let len = u16::from_be_bytes([len_bytes[0], len_bytes[1]]) as usize;
        let data = self.read_exact(len)?;
        Ok(String::from_utf8_lossy(data).into_owned())
    }

    fn read_exact(&mut self, n: usize) -> Result<&[u8]> {
        if self.pos + n > self.buf.len() {
            return Err(Error::new(ErrorKind::UnexpectedEof, "invalid payload length"));
        }
        let s = &self.buf[self.pos..self.pos + n];
        self.pos += n;
        Ok(s)
    }
}