# RPF9 format notes (GTA VI)

Format notes for RPF9 archives, derived from the archive files alone. The table of contents
(TOC) is encrypted and RageForge does not decrypt it; resources are located by scanning the data
area that follows the TOC (see [Carving](#carving)).

Each statement below is marked as **observed** (seen on every archive examined, 12 archives of
one game version) or **hypothesis** (consistent with the observations, not proven). Header
parsing and the carver are covered by the maintainer's synthetic test suite, which is not part of
the public repository; no test uses bytes taken from game files.

## Archive magic

Observed: the first four bytes are `39 46 50 52` ("9FPR"); the logical name is "RPF9".
Lineage: RPF7 = GTA V, RPF8 = RDR2 / GTA V Enhanced, RPF9 = GTA VI.

## 64-byte header

| Offset | Size | Field        | Notes |
|--------|------|--------------|-------|
| 0x00   | 4    | magic        | `39 46 50 52` |
| 0x04   | 4    | toc_end      | size in bytes of the encrypted TOC that follows the header (16-aligned) |
| 0x08   | 4    | toc_field_b  | hypothesis: size of the entries section |
| 0x0C   | 4    | names_size   | hypothesis: decompressed size of the name table |
| 0x10   | 8    | toc_hash     | 64-bit value, read big-endian; purpose not established (hypothesis: a check value for the TOC) |
| 0x18   | 4    | const        | observed: always 293376 (0x47A00) |
| 0x1C   | 4    | const        | observed: always 336 (0x150) |
| 0x20   | 2    | unk_a        | small integer, varies |
| 0x22   | 2    | unk_b        | observed: always 1 |
| 0x24   | 4    | count        | hypothesis: number of TOC entries |
| 0x28   | 4    | key_id       | varies between archives; meaning not established |
| 0x2C   | 4    | flags        | observed: `00 09 0F xx`, byte 0 = 1 or 2 |
| 0x30   | 4    | const        | observed: always 16 |
| 0x34   | 12   | reserved     | observed: zero |

RageForge uses only `magic` (to recognise the format) and `toc_end` (to find where the data area
starts). The other fields are displayed for information.

## TOC

Observed:

- The TOC occupies `toc_end` bytes right after the header.
- Its first 32 bytes are identical in every archive examined, whatever the content, entry count
  or `key_id`.
- The bytes after them look random (flat byte histogram, about 8 bits of entropy per byte),
  standard zlib, deflate and gzip decoding fails, and no 16-byte block repeats inside the body.
- The TOC ends with zero bytes in the clear, up to the 16-byte boundary.
- XOR-ing the TOC bodies of two archives that share a `key_id` gives random-looking output.
- `key_id` is not the joaat, joaat-lower, CRC32 or FNV-1a hash of the archive name (base name,
  stem or full path).

Hypotheses, not proven:

- The body is encrypted rather than compressed.
- The constant 32 bytes are either a fixed encrypted preamble produced with a fixed IV, or a
  plaintext signature.
- The cipher is a block cipher in a chaining mode such as CBC rather than a stream cipher. The
  observations above are consistent with this but do not identify the algorithm, the mode or the
  key handling.
- `key_id` selects a key or a key group.

RageForge contains no key, no cipher and no code that reads the TOC for anything other than its
size. `Rpf9Archive` can load the raw TOC bytes for display; it does not transform them.

## Carving

`src/RageForge.Core/Carve/Rpf9Carver.cs`:

1. `DataStart` reads the header and returns `align16(64 + toc_end)`, or nothing when the magic
   is wrong, the file is shorter than the header or the TOC runs past the end of the file. In
   that case nothing is scanned.
2. `ScanArchive` / `ScanParallel` / `ScanRange` walk the data area in 16-byte steps, read-only,
   in 256 MB segments processed in parallel.
3. At each step it checks for three signatures: a zlib header (`0x78` + valid check bits), an
   AWC audio container, or a Bink 2 video header.
4. `InflateAt` decompresses a candidate zlib stream with a standard inflater and keeps it only if
   the stream ends correctly (checksum included). Truncated or random data is discarded.
5. `GroupStreams` joins consecutive 64 KiB zlib chunks into one resource.
6. `GameIndexer` names each resource from strings found in its own decompressed data (`name.#xx`)
   or generates a name from its kind and offset, and caches the result as an independent index.

The scan never reads the TOC area. The synthetic tests cover: data starting after the TOC,
streams inside the TOC being ignored, invalid magic, truncated header, TOC past the end of file,
a stream cut by the end of file, and a data area made entirely of random bytes.

## TTPF

`data/ttdb/*.ttpf` texture stores use a separate container, magic `RDS5` (`52 44 53 35`). They
are parsed by `src/RageForge.Core/Ttpf` (store layout, AMD Gfx10/RDNA2 detiling, BC1–BC7 and
Xbox BCPack decoding) and decoded in memory for display.
