// DSH session log reader for the DSH Launcher.
// Reads a session log (plain .jsonl or concatenated-frame .jsonl.zstd) and
// prints ONE JSON line with the session header info and a human-readable
// title (the latest session/title event, falling back to the first user
// message). Decompression uses node:zlib's built-in zstd (Node >= 22.15).
// Usage: node session-reader.mjs <session-file>
'use strict';
import { zstdDecompressSync } from 'node:zlib';
import fs from 'node:fs';

const ZSTD_MAGIC = 0xFD2FB528; // bytes 28 B5 2F FD, little-endian
const FRAME_BUDGET = 200;
const SETTLE_FRAMES = 10; // keep decoding a few frames after the first title

function scanZstdFrames(buf) {
  const frames = [];
  let off = 0;
  while (off + 5 <= buf.length) {
    if (buf.readUInt32LE(off) !== ZSTD_MAGIC) break;
    const start = off;
    off += 4;
    const fhd = buf[off++];
    const fcs = fhd >> 6;
    const singleSegment = (fhd >> 5) & 1;
    const checksum = (fhd >> 2) & 1;
    const did = fhd & 3;
    if (!singleSegment) off += 1;
    if (did !== 0) off += 1 << (did - 1);
    if (fcs === 1) off += singleSegment ? 1 : 2;
    else if (fcs === 2) off += 4;
    else if (fcs === 3) off += 8;
    let last = false;
    while (!last && off + 3 <= buf.length) {
      const block = buf.readUInt32LE(off) & 0xFFFFFF;
      last = block & 1;
      const type = (block >> 1) & 3;
      const size = block >> 3;
      off += 3;
      if (type === 3) off += 1;
      else if (type === 0 || type === 2) off += size;
      else break;
    }
    if (checksum) off += 4;
    frames.push([start, off]);
  }
  return frames;
}

function extract(lines, state) {
  for (const raw of lines) {
    if (!raw.trim()) continue;
    let event;
    try { event = JSON.parse(raw); } catch { continue; }

    if (event.type === 'session' && !state.header) {
      state.header = event;
    } else if (event.type === 'user/message' && !state.firstUser) {
      const content = event.data && event.data.content;
      if (Array.isArray(content)) {
        for (const c of content) {
          if (c && typeof c.text === 'string' && c.text.trim()) {
            state.firstUser = c.text.replace(/\s+/g, ' ').trim();
            break;
          }
        }
      }
    } else if (event.type === 'session/title') {
      const t = event.data && event.data.title;
      if (typeof t === 'string' && t.trim()) state.titles.push(t.trim());
    }
  }
}

function main() {
  const file = process.argv[2];
  if (!file) {
    console.error('usage: node session-reader.mjs <session-file>');
    process.exit(1);
  }

  const buf = fs.readFileSync(file);
  const state = { header: null, firstUser: null, titles: [] };

  if (file.endsWith('.zstd')) {
    const frames = scanZstdFrames(buf);
    const n = Math.min(frames.length, FRAME_BUDGET);
    let titlesSeen = 0;
    for (let i = 0; i < n; i++) {
      let text;
      try {
        text = zstdDecompressSync(buf.subarray(frames[i][0], frames[i][1])).toString('utf8');
      } catch { continue; }
      extract(text.split('\n'), state);
      if (state.titles.length > titlesSeen) {
        titlesSeen = state.titles.length;
      }
      if (state.header && state.firstUser && titlesSeen > 0 && i >= SETTLE_FRAMES) break;
    }
  } else {
    extract(buf.toString('utf8').split('\n'), state);
  }

  const header = state.header || {};
  const title = state.titles.length > 0
    ? state.titles[state.titles.length - 1]
    : (state.firstUser || '');
  console.log(JSON.stringify({
    id: header.id || null,
    createdAt: typeof header.createdAt === 'number' ? header.createdAt : null,
    cwd: typeof header.cwd === 'string' ? header.cwd : null,
    agentPreset: typeof header.agentPreset === 'string' ? header.agentPreset : null,
    title: title.slice(0, 200)
  }));
}

main();
