#!/usr/bin/env python3
"""星穹铁道 Stub 特征码比对工具（纯只读，无第三方依赖）。

用途：每次游戏版本更新后，用旧特征码扫新 GameAssembly.dll，得到

  1) 命中数比对 —— 仍然唯一命中 = 特征码直接可用，不用改；
  2) 相似性比对 —— 0 命中 / 多命中时，用短锚点扫出候选集，按「固定字节命中率」
     打分排序，挑出最可能的同一函数，据此重新推导特征码。

特征码表与 src/StubStarRail/Il2CppBridge.cpp 里的常量一一对应；改动那边时
记得同步这里（否则比对基线就漂了）。

用法：
    python3 tools/starrail-signatures/check_signatures.py \
        --binary <新版 GameAssembly.dll> [--baseline <旧版 GameAssembly.dll>]
    python3 tools/starrail-signatures/check_signatures.py \
        --verify-source src/StubStarRail/Il2CppBridge.cpp

输出：每个目标在新版上的命中数、命中 RVA；0 命中时给出相似度最高的候选。
--verify-source 只比对 C++ 源码与本工具的特征码是否一致，不扫描 DLL。
"""

from __future__ import annotations

import argparse
import re
import struct
from dataclasses import dataclass


# --------------------------------------------------------------------------
# 特征码表：与 src/StubStarRail/Il2CppBridge.cpp 保持一致
# --------------------------------------------------------------------------
@dataclass(frozen=True)
class Signature:
    name: str
    pattern: str
    # 旧版基线 RVA，仅用于打印对照，不参与判定
    baseline_rva: int
    # 多命中目标：候选后面这个窗口内必须出现 twin_pattern
    twin_pattern: str | None = None
    twin_window: int = 0
    twin_baseline_rva: int = 0
    # 多命中是预期行为：真身由运行时探测确定（路径查找），或两个同构入口都要挂
    # （TextMeshProUGUI 的 SetVerticesDirty / SetMaterialDirty）。这类目标只要求
    # 命中数落在 [min_hits, max_hits] 区间内，不按「唯一命中」判定。
    expect_multiple: bool = False
    min_hits: int = 1
    max_hits: int = 0  # 0 表示不设上限


SIGNATURES: tuple[Signature, ...] = (
    Signature(
        "RPGApplication.OnUpdate",
        "56 57 48 83 EC 48 0F 29 7C 24 30 0F 29 74 24 20 48 89 CE 80 3D ?? ?? ?? ?? 00 "
        "0F 85 ?? ?? ?? ?? 80 7E ?? 00 0F 84 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? "
        "80 B9 ?? 00 00 00 00 0F 84 ?? ?? ?? ??",
        0x1802FBF0,
    ),
    Signature(
        "Dither.Merge",
        "41 56 56 57 55 53 48 83 EC 50 0F 29 7C 24 40 0F 29 74 24 30 44 89 CD 44 89 C7 "
        "0F 28 F9 48 89 CE 80 3D ?? ?? ?? ?? 00 0F 85 ?? ?? ?? ?? "
        "80 3D ?? ?? ?? ?? 00 0F 85 ?? ?? ?? ??",
        0x19F1BE00,
    ),
    Signature(
        "Dither.SetDistance",
        "56 53 48 83 EC 38 0F 29 74 24 20 44 89 C3 0F 28 F1 48 89 CE 80 3D ?? ?? ?? ?? 00 "
        "75 ?? 80 7E 36 00 74 ?? 0F 57 C0 F3 0F 5F C6 F3 0F 10 0D ?? ?? ?? ?? "
        "F3 0F 5D C8 F3 0F 11 4E 30 F3 0F 59 4E 2C",
        0x19F1C0E0,
    ),
    Signature(
        "Dither.SetElevation",
        "56 48 83 EC 30 0F 29 74 24 20 0F 28 F1 48 89 CE 80 3D ?? ?? ?? ?? 00 "
        "75 ?? 80 7E 36 00 74 ?? 0F 57 C0 F3 0F 5F C6 F3 0F 10 0D ?? ?? ?? ?? "
        "F3 0F 5D C8 F3 0F 11 4E 2C F3 0F 59 4E 30",
        0x19F1BD70,
    ),
    Signature(
        "DOF.IsActiveImpl",
        "48 83 EC 28 80 79 18 00 74 ?? 48 8B 0D ?? ?? ?? ?? 80 B9 ?? 00 00 00 00 74 ?? "
        "48 8B 05 ?? ?? ?? ?? 48 8B 80 ?? ?? 00 00 48 85 C0 74 ?? 80 78 ?? 00 0F 95 C0 "
        "48 83 C4 28 C3 31 C0 48 83 C4 28 C3",
        0x1858CCF0,
    ),
    Signature(
        "Graphic.SetVerticesDirty",
        "56 48 83 EC 20 48 89 CE FF 15 ?? ?? ?? ?? 84 C0 74 ?? "
        "C6 86 99 00 00 00 01 EB ?? 48 89 F1 FF 15 ?? ?? ?? ?? 84 C0 74 ?? "
        "C6 46 58 01 48 89 F1 E8 ?? ?? ?? ?? 48 8B 46 68 48 85 C0 74 ?? "
        "4C 8B 40 18 48 8B 50 28 48 8B 48 40 48 83 C4 20 5E 49 FF E0 90",
        0x1B78C0C0,
        twin_pattern=(
            "56 48 83 EC 20 48 89 CE FF 15 ?? ?? ?? ?? 84 C0 74 09 "
            "C6 86 9A 00 00 00 01"
        ),
        twin_window=0x80,
        twin_baseline_rva=0x1B78C120,
    ),
    Signature(
        "TMP_Text.SetVerticesDirty",
        "56 57 53 48 83 EC 20 48 89 CF FF 15 ?? ?? ?? ?? 84 C0 0F 84 ?? ?? ?? ?? "
        "48 8B B7 ?? ?? ?? ?? 48 85 F6 0F 84 ?? ?? ?? ?? 48 83 7E 10 00",
        0x134E9970,
    ),
    Signature(
        "TextMeshProUGUI.Dirty",
        "56 57 48 83 EC 28 48 85 C9 74 ?? 48 89 CE 48 83 79 10 00 74 ?? "
        "48 89 F1 FF 15 ?? ?? ?? ?? 84 C0 74 ?? 48 8B 05 ?? ?? ?? ?? "
        "48 8B B8 ?? ?? ?? ??",
        0x134EACD0,
        expect_multiple=True,
        min_hits=2,
        max_hits=2,
    ),
    Signature(
        "GameObject.Find",
        "48 FF ?? ?? ?? ?? ?? 66 0F 1F 84 00 00 00 00 00 48 83 EC 28 C7 44 24 20",
        0x1DEDE300,
        expect_multiple=True,
    ),
    Signature(
        "GameObject.GetComponent",
        "48 8B 05 ?? ?? ?? ?? 48 FF E0 66 0F 1F 44 00 00 "
        "48 8B 05 ?? ?? ?? ?? 45 31 C0 48 FF E0 0F 1F 00",
        0x1DEDDE30,
        expect_multiple=True,
    ),
)

# C++ 源码（src/StubStarRail/Il2CppBridge.cpp）里的常量名 -> 本工具的签名名。
# 特征码两侧必须逐字节一致，--verify-source 用来防止改动时漏同步。
CPP_PATTERN_CONSTANTS: dict[str, str] = {
    "RPGApplication.OnUpdate": "kRpgApplicationOnUpdatePattern",
    "Dither.Merge": "kDitherMergePattern",
    "Dither.SetDistance": "kDitherSetDistancePattern",
    "Dither.SetElevation": "kDitherSetElevationPattern",
    "DOF.IsActiveImpl": "kDofIsActivePattern",
    "Graphic.SetVerticesDirty": "kGraphicSetVerticesDirtyPattern",
    "TMP_Text.SetVerticesDirty": "kTmpTextSetVerticesDirtyPattern",
    "TextMeshProUGUI.Dirty": "kTmpUguiDirtyPattern",
    "GameObject.Find": "kGameObjectFindPattern",
    "GameObject.GetComponent": "kGetComponentStringPattern",
}
CPP_TWIN_CONSTANT = "kGraphicSetLayoutDirtyTwinPattern"


# --------------------------------------------------------------------------
# PE 解析 / 扫描
# --------------------------------------------------------------------------
@dataclass(frozen=True)
class Section:
    name: str
    va: int
    vsize: int
    raw: int
    rsize: int
    characteristics: int


class Image:
    def __init__(self, path: str) -> None:
        self.path = path
        with open(path, "rb") as handle:
            self.data = handle.read()
        header = self.data[:0x1000]
        pe = struct.unpack_from("<I", header, 0x3C)[0]
        count = struct.unpack_from("<H", header, pe + 6)[0]
        opt_size = struct.unpack_from("<H", header, pe + 20)[0]
        sections = []
        for i in range(count):
            off = pe + 24 + opt_size + i * 40
            name = header[off : off + 8].rstrip(b"\0").decode("ascii", "replace")
            vsize, va, rsize, raw = struct.unpack_from("<IIII", header, off + 8)
            characteristics = struct.unpack_from("<I", header, off + 36)[0]
            sections.append(Section(name, va, vsize, raw, rsize, characteristics))
        self.sections = sections

    def executable_ranges(self) -> list[tuple[int, int]]:
        """(start, end) 文件偏移区间，只含 IMAGE_SCN_MEM_EXECUTE 的节。"""
        out = []
        for s in self.sections:
            if s.characteristics & 0x20000000:
                out.append((s.raw, s.raw + s.rsize))
        return out

    def to_rva(self, file_offset: int) -> int | None:
        for s in self.sections:
            if s.raw <= file_offset < s.raw + max(s.rsize, s.vsize):
                return s.va + (file_offset - s.raw)
        return None


def compile_pattern(pattern: str) -> list[int | None]:
    return [None if t in ("?", "??") else int(t, 16) for t in pattern.split()]


def scan(image: Image, pattern: str) -> list[int]:
    compiled = compile_pattern(pattern)
    width = len(compiled)
    fixed = [(i, b) for i, b in enumerate(compiled) if b is not None]
    if not fixed:
        return []
    anchor_index, anchor_byte = fixed[0]
    data = image.data
    hits: list[int] = []
    for start, end in image.executable_ranges():
        pos = start
        while True:
            pos = data.find(bytes([anchor_byte]), pos, end)
            if pos < 0:
                break
            base = pos - anchor_index
            if base >= start and base + width <= end:
                if all(data[base + i] == b for i, b in fixed):
                    hits.append(base)
            pos += 1
    return hits


def similarity(image: Image, pattern: str, anchor: str) -> list[tuple[float, int, int]]:
    """(命中率, 命中固定字节数, 文件偏移)，按命中率降序。"""
    compiled = compile_pattern(pattern)
    fixed = [(i, b) for i, b in enumerate(compiled) if b is not None]
    total = len(fixed)
    results = []
    for base in scan(image, anchor):
        matched = sum(
            1 for i, b in fixed if base + i < len(image.data) and image.data[base + i] == b
        )
        results.append((matched / total, matched, base))
    results.sort(reverse=True)
    return results


def anchor_of(pattern: str, length: int = 8) -> str:
    """取前若干字节做短锚点（遇通配就截断）。"""
    tokens: list[str] = []
    for token in pattern.split():
        if token in ("?", "??"):
            break
        tokens.append(token)
        if len(tokens) >= length:
            break
    return " ".join(tokens)


def resolve(image: Image, sig: Signature) -> tuple[list[int], str]:
    """返回 (最终候选 RVA 列表, 说明)。"""
    hits = scan(image, sig.pattern)
    if not hits:
        return [], "0 命中"

    rvas = [image.to_rva(h) for h in hits]
    if sig.twin_pattern is None:
        return [r for r in rvas if r is not None], f"{len(hits)} 命中"

    twins = scan(image, sig.twin_pattern)
    accepted = []
    for hit in hits:
        for twin in twins:
            if hit < twin <= hit + sig.twin_window:
                accepted.append(image.to_rva(hit))
                break
    note = f"{len(hits)} 命中 → 相邻结构校验后 {len(accepted)} 个"
    return [r for r in accepted if r is not None], note


def extract_cpp_patterns(source_path: str) -> dict[str, str | None]:
    """从 Il2CppBridge.cpp 提取特征码常量；相邻字符串字面量按 C++ 规则拼接。"""
    with open(source_path, encoding="utf-8") as handle:
        source = handle.read()

    def one(constant: str) -> str | None:
        match = re.search(
            r"constexpr const char\*\s+" + constant + r"\s*=\s*(.*?);", source, re.S
        )
        if not match:
            return None
        return "".join(re.findall(r'"([^"]*)"', match.group(1))).strip()

    patterns = {name: one(constant) for name, constant in CPP_PATTERN_CONSTANTS.items()}
    patterns["__twin__"] = one(CPP_TWIN_CONSTANT)
    return patterns


def verify_source(source_path: str) -> int:
    """校验 C++ 源码里的特征码与本工具是否逐字节一致。"""
    try:
        cpp = extract_cpp_patterns(source_path)
    except OSError as error:
        print(f"读取失败: {error}")
        return 1

    def norm(value: str | None) -> str:
        return " ".join((value or "").split())

    failures = 0
    for sig in SIGNATURES:
        if norm(cpp.get(sig.name)) != norm(sig.pattern):
            print(f"MISMATCH {sig.name}")
            print(f"  cpp : {norm(cpp.get(sig.name))}")
            print(f"  tool: {norm(sig.pattern)}")
            failures += 1
        else:
            print(f"OK {sig.name}")
        if sig.twin_pattern:
            if norm(cpp.get("__twin__")) != norm(sig.twin_pattern):
                print(f"MISMATCH {sig.name} (twin)")
                print(f"  cpp : {norm(cpp.get('__twin__'))}")
                print(f"  tool: {norm(sig.twin_pattern)}")
                failures += 1
            else:
                print(f"OK {sig.name} (twin)")

    print("-" * 96)
    print("C++ 与工具两侧特征码一致。" if failures == 0 else f"{failures} 处不一致。")
    return 0 if failures == 0 else 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", help="要检查的 GameAssembly.dll")
    parser.add_argument("--baseline", help="旧版 GameAssembly.dll（用于相似性比对）")
    parser.add_argument(
        "--verify-source",
        metavar="IL2CPP_BRIDGE_CPP",
        help="只校验 C++ 源码里的特征码与本工具是否一致，不扫描 DLL",
    )
    args = parser.parse_args()

    if args.verify_source:
        return verify_source(args.verify_source)

    if not args.binary:
        parser.error("需要 --binary，或使用 --verify-source")

    target = Image(args.binary)
    baseline = Image(args.baseline) if args.baseline else None

    print(f"目标二进制: {args.binary}")
    print(f"{'目标':28s} {'判定':10s} {'RVA':>12s}  说明")
    print("-" * 96)

    failures = 0
    for sig in SIGNATURES:
        rvas, note = resolve(target, sig)
        if sig.expect_multiple:
            count = len(rvas)
            within = count >= sig.min_hits and (sig.max_hits == 0 or count <= sig.max_hits)
            if within:
                verdict = "多命中(预期)"
            elif count == 0:
                verdict = "未命中"
            else:
                verdict = "命中数异常"
            shown = f"{count} 个"
            if not within:
                failures += 1
        elif len(rvas) == 1:
            verdict = "唯一命中"
            shown = hex(rvas[0])
        elif not rvas:
            verdict = "未命中"
            shown = "-"
            failures += 1
        else:
            verdict = "多命中"
            shown = ",".join(hex(r) for r in rvas[:4])
            failures += 1

        print(f"{sig.name:28s} {verdict:10s} {shown:>12s}  {note}")

        if verdict not in ("唯一命中", "多命中(预期)") and baseline is not None:
            old_hits = scan(baseline, sig.pattern)
            if old_hits:
                print(f"{'':28s} 旧版基线 RVA {hex(sig.baseline_rva)}，旧版命中 {len(old_hits)} 处")
            print(f"{'':28s} 相似性比对（按固定字节命中率降序）：")
            ranked = similarity(target, sig.pattern, anchor_of(sig.pattern))
            for ratio, matched, off in ranked[:5]:
                rva = target.to_rva(off)
                print(
                    f"{'':30s} sim={ratio:.3f} ({matched} 字节) RVA={hex(rva) if rva else '?'}"
                )

    print("-" * 96)
    print("全部目标唯一命中。" if failures == 0 else f"{failures} 个目标需要重新推导特征码。")
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
