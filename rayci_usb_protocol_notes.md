# RayCi USB Protocol Notes

Date: 2026-06-10

## Current Authoritative Sources

Use these four captures first:

- `D:\work\ultron\rayci-ccd\插入usb.txt`
- `D:\work\ultron\rayci-ccd\插入usb1.txt`
- `D:\work\ultron\rayci-ccd\打开软件.txt`
- `D:\work\ultron\rayci-ccd\打开软件1.txt`

Older files such as `restart-live-tile-2.txt` are still useful as historical references, but they are not the sole source of truth anymore.

## Important Corrections

- Native camera identity is `USB\VID_4448&PID_5670`.
- `uEye UI-154xLE Series` is a compatibility-layer identity in this workspace, not the authoritative native USB identity.
- Treat `MER-130-30UM-L` and `RH1015005021` as suspect raw-page fields.
- Do not use those strings as authoritative outward simulator identity.
- Do not treat `40 b2` as a single fixed `d9 d9 f5` command anymore.
- Current captures show at least two prepare-stream payload variants:
  - `9a 9a f5`
  - `5a 5a f5`

## Insert-Phase Structure

Stage 1 descriptor:

```text
12 01 00 02 ff ff ff 40 48 44 70 56 c0 04 00 00 00 01
```

Stage 1 config length:

```text
09 02 ab 00 ...
```

Observed loader behavior:

- about `846` `40 a0` writes
- stable `0xe600` `01 -> 00` pattern
- consistent with loader/download/re-enumeration behavior

Stage 2 descriptor:

```text
12 01 00 02 00 00 00 40 48 44 70 56 40 1b 01 02 00 01
```

Stage 2 config length:

```text
09 02 20 00 ...
```

## Runtime Page Contract

Before live mode, the software repeatedly reads:

```text
c0 e1 00 00 00 00 04 00
c0 e1 00 00 04 00 40 00
c0 e1 00 00 44 00 40 00
```

and polls:

```text
c0 b4 00 00 00 00 01 00 -> 70
```

This `0x70` state is stable before live mode.

## Live Mode Contract

Observed sequence in the current capture set:

1. `40 b2 03 00 00 00 03 00`
2. payload variant:
   - `9a 9a f5` in `打开软件.txt`
   - `5a 5a f5` in `打开软件1.txt`
3. `c0 b4 -> 0x74`
4. full `c0 e1` sweep up to `0x0b04`
5. `c0 ca 00 00 00 00 04 00 -> 00 00 00 1a`
6. `40 bc 01 00 00 00 01 00 -> 01`
7. bulk stream starts on `10.2`

Stop path:

```text
40 bc 00 00 00 00 01 00 -> 00
10.2 RESET
```

## Working Stream Capture (`1201el-u2-1022-0034.txt`)

This file is currently the most useful evidence of a camera session that really reached image delivery.

Observed runtime behavior:

- continuous image payload on `25.2 IN`
- payload size `524288` bytes per image packet
- intermittent `25.2 IN` metadata packet of `31` bytes
- exposure write on `40 ce 00 00 58 04 04 00`
- immediate status dip `c0 b4 -> 0x54`
- return to stable live status `c0 b4 -> 0x74`
- stop command `40 bc 00 00 00 00 01 00 -> 00`
- after stop, repeated page reads:
  - `c0 e1 00 00 00 00 04 00`
  - `c0 e1 00 00 04 00 40 00`
  - `c0 e1 00 00 44 00 40 00`

Important implication for the simulator:

- it is not enough to only answer page reads and register writes
- the live path must also sustain repeated image-buffer delivery with the same wide-sample cadence expected by the host
- the 10-bit variant is carried in a 16-bit-style container (`10bpp (Y16)`), so compatibility code must not collapse `10bpp` buffers into `8bpp`

## Parameter Mapping

Exposure write:

```text
40 ce 00 00 58 04 04 00
```

- payload is little-endian exposure time in microseconds

Gain write:

```text
40 ce 00 00 04 07 04 00
```

- payload is a 32-bit gain code

Immediate post-write status:

```text
c0 b4 -> 0x54
```

Then the state returns to `0x74`.

## What To Trust vs What Not To Trust

Trust first:

- dual-stage enumeration
- loader-style `40 a0` insert flow
- runtime page offsets and lengths
- `0x70 / 0x74 / 0x54` state transitions
- `c0 ca -> 00 00 00 1a`
- `40 bc` start/stop stream behavior

Treat as suspect until independently revalidated:

- `MER-130-30UM-L`
- `RH1015005021`
- any derived decimal serial alias
- any single fixed `40 b2` payload treated as globally correct
