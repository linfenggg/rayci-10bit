from __future__ import annotations

from pathlib import Path

import capstone
import pefile


RAYCI_PATH = Path(r"D:\work\ultron\rayci-10bit\dist\RayCi64Lite-FGCameraBridge\RayCi.exe")
TARGET_IMPORTS = {
    "FGGetNodeList",
    "FGInitModule",
    "?Connect@CFGCamera@@UEAAKPEAUUINT32HL@@PEAX@Z",
    "?GetDeviceName@CFGCamera@@UEAAKPEADK0@Z",
}


def get_text_section(pe: pefile.PE):
    for section in pe.sections:
        if section.Name.rstrip(b"\x00") == b".text":
            return section
    raise RuntimeError("missing .text section")


def get_target_iat(pe: pefile.PE):
    targets: dict[str, int] = {}
    for entry in pe.DIRECTORY_ENTRY_IMPORT:
        dll = entry.dll.decode(errors="ignore")
        if dll.lower() != "fgcamera.dll":
            continue
        for imp in entry.imports:
            if not imp.name:
                continue
            name = imp.name.decode(errors="ignore")
            if name in TARGET_IMPORTS:
                targets[name] = imp.address
    missing = TARGET_IMPORTS.difference(targets)
    if missing:
        raise RuntimeError(f"missing imports: {sorted(missing)}")
    return targets


def disassemble_text(pe: pefile.PE):
    text = get_text_section(pe)
    text_data = text.get_data()
    base = pe.OPTIONAL_HEADER.ImageBase + text.VirtualAddress
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    md.detail = True
    instructions = list(md.disasm(text_data, base))
    return instructions


def find_iat_references(instructions, iat_va: int):
    matches = []
    for index, insn in enumerate(instructions):
        if not insn.operands:
            continue
        for op in insn.operands:
            if op.type != capstone.x86.X86_OP_MEM:
                continue
            mem = op.mem
            if mem.base != capstone.x86.X86_REG_RIP:
                continue
            target = insn.address + insn.size + mem.disp
            if target == iat_va:
                matches.append(index)
                break
    return matches


def find_direct_call_targets(instructions, target_addresses: set[int]):
    matches = []
    for index, insn in enumerate(instructions):
        if insn.mnemonic != "call" or not insn.operands:
            continue
        op = insn.operands[0]
        if op.type != capstone.x86.X86_OP_IMM:
            continue
        if op.imm in target_addresses:
            matches.append(index)
    return matches


def print_context(instructions, indexes, label: str):
    print(f"\n=== {label} ===")
    for idx in indexes:
        start = max(0, idx - 18)
        end = min(len(instructions), idx + 28)
        print(f"\n-- context around {instructions[idx].address:#x} --")
        for j in range(start, end):
            insn = instructions[j]
            marker = ">>" if j == idx else "  "
            print(f"{marker} {insn.address:#x}: {insn.mnemonic:8} {insn.op_str}")


def main():
    pe = pefile.PE(str(RAYCI_PATH), fast_load=False)
    targets = get_target_iat(pe)
    instructions = disassemble_text(pe)
    print(f"ImageBase={pe.OPTIONAL_HEADER.ImageBase:#x}")
    for name, iat_va in targets.items():
        iat_refs = find_iat_references(instructions, iat_va)
        thunk_addresses = {instructions[idx].address for idx in iat_refs}
        callers = find_direct_call_targets(instructions, thunk_addresses)
        print(
            f"{name}: IAT={iat_va:#x}, iat_refs={len(iat_refs)}, "
            f"thunks={[hex(addr) for addr in sorted(thunk_addresses)]}, callers={len(callers)}"
        )
        print_context(instructions, iat_refs, f"{name} thunk")
        print_context(instructions, callers, f"{name} callers")


if __name__ == "__main__":
    main()
