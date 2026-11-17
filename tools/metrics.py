#!/usr/bin/env python3
"""Вимірювач метрик успадкованого коду (методика М1-М3 методички СР-2).

Для кожного методу рахує: логічні рядки (LOC), цикломатичну складність (CC),
максимальну глибину вкладеності, кількість параметрів. Для кожного класу —
fan-out (скільки інших типів проєкту клас згадує). Додатково шукає клони
довжиною 6+ рядків за точним збігом після нормалізації пробілів.

Запуск:  python tools/metrics.py src docs/metrics-before.csv
"""
import csv
import re
import sys
from pathlib import Path

KEYWORDS = ("if", "for", "foreach", "while", "case", "catch")
MODIFIERS = (
    "public", "private", "protected", "internal", "static", "virtual",
    "override", "sealed", "async", "partial", "extern", "unsafe",
)


def strip_comments(text: str) -> str:
    """Прибирає // і /* */, зберігаючи рядки та вміст рядкових літералів."""
    out = []
    i, n = 0, len(text)
    in_string = False
    while i < n:
        ch = text[i]
        if in_string:
            out.append(ch)
            if ch == "\\" and i + 1 < n:
                out.append(text[i + 1])
                i += 2
                continue
            if ch == '"':
                in_string = False
            i += 1
            continue
        if ch == '"':
            in_string = True
            out.append(ch)
            i += 1
            continue
        if ch == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if ch == "/" and i + 1 < n and text[i + 1] == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                if text[i] == "\n":
                    out.append("\n")
                i += 1
            i += 2
            continue
        out.append(ch)
        i += 1
    return "".join(out)


def match_brace(text: str, start: int) -> int:
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return i
    raise ValueError("unbalanced braces")


def is_method_decl(stripped: str) -> bool:
    if "(" not in stripped or " class " in stripped or " enum " in stripped:
        return False
    if "=" in stripped.split("(", 1)[0]:  # ініціалізатор поля, не метод
        return False
    first = stripped.split(maxsplit=1)[0]
    if first not in MODIFIERS:
        return False
    return not stripped.startswith(("return ", "if ", "for ", "foreach ",
                                    "while ", "switch ", "catch ", "new "))


def logical_lines(body: str) -> int:
    count = 0
    for raw in body.splitlines():
        line = raw.strip()
        if not line or line in ("{", "}", "};"):
            continue
        if line.startswith("//"):
            continue
        count += 1
    return count


def cyclomatic(body: str) -> int:
    cc = 1
    for kw in KEYWORDS:
        cc += len(re.findall(r"\b" + kw + r"\b", body))
    cc += body.count("&&") + body.count("||")
    cc += body.count("??")
    cc += len(re.findall(r"\?\.", body))
    cc += len(re.findall(r"\?(?![?.])", body))  # тернарний оператор
    return cc


def max_nesting(body: str) -> int:
    depth = 0
    best = 0
    for ch in body:
        if ch == "{":
            depth += 1
            best = max(best, depth)
        elif ch == "}":
            depth -= 1
    return best  # тіло методу — рівень 1


def param_count(params: str) -> int:
    params = params.strip()
    if not params:
        return 0
    depth = 0
    count = 1
    for ch in params:
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        elif ch == "," and depth == 0:
            count += 1
    return count


def method_bodies(block: str):
    """Повертає (ім'я, параметри, тіло) для кожного методу в блоці класу."""
    result = []
    lines = block.splitlines(keepends=True)
    starts, pos = [], 0
    for line in lines:
        starts.append(pos)
        pos += len(line)
    i = 0
    while i < len(lines):
        stripped = lines[i].strip()
        if is_method_decl(stripped):
            j, head = i, ""
            while j < len(lines):
                head += lines[j]
                if "{" in head:
                    break
                j += 1
            if "{" in head:
                brace = starts[i] + head.index("{")
                end = match_brace(block, brace)
                name = re.search(r"(\w+)\s*\(", head)
                params = re.search(r"\(([^)]*)\)", head, re.S)
                result.append((name.group(1) if name else "?",
                               params.group(1) if params else "",
                               block[brace:end + 1]))
                i = j + 1
                continue
        i += 1
    return result


def find_clones(texts, min_len=6):
    """Клони — дві й більше послідовностей із min_len однакових рядків."""
    seq = []
    for path, text in texts.items():
        for no, raw in enumerate(text.splitlines(), 1):
            norm = re.sub(r"\s+", " ", raw.strip())
            if len(norm) < 3 or norm in ("{", "}"):
                continue
            if norm.startswith(("using ", "namespace ", "//")):
                continue
            seq.append((norm, path.name, no))
    groups, used = [], set()

    def run(a, b):
        length = 0
        while (a + length < len(seq) and b + length < len(seq)
               and seq[a + length][0] == seq[b + length][0]
               and seq[a + length][1] == seq[a][1]
               and seq[b + length][1] == seq[b][1]):
            length += 1
        return length

    for i in range(len(seq)):
        if i in used:
            continue
        for j in range(i + 1, len(seq)):
            if seq[i][0] != seq[j][0]:
                continue
            length = run(i, j)
            if length >= min_len:
                places = [f"{seq[i][1]}:{seq[i][2]}-{seq[i][2] + length - 1}",
                          f"{seq[j][1]}:{seq[j][2]}-{seq[j][2] + length - 1}"]
                used.update(range(i, i + length))
                used.update(range(j, j + length))
                for k in range(j + 1, len(seq)):
                    if seq[k][0] == seq[i][0] and run(i, k) >= length:
                        places.append(f"{seq[k][1]}:{seq[k][2]}-{seq[k][2] + length - 1}")
                        used.update(range(k, k + length))
                groups.append({"lines": length, "places": places})
                break
    unique = []
    for g in groups:
        if any(set(g["places"]) <= set(u["places"]) and g["lines"] <= u["lines"]
               for u in unique):
            continue
        unique.append(g)
    return unique


def main():
    src = Path(sys.argv[1])
    out = Path(sys.argv[2])
    files = sorted(src.glob("*.cs"))
    texts = {p: strip_comments(p.read_text(encoding="utf-8")) for p in files}

    type_names = set()
    for text in texts.values():
        type_names |= set(re.findall(r"\b(?:class|enum|record|struct)\s+(\w+)", text))

    rows = []
    for path, text in texts.items():
        for decl in re.finditer(r"\b(?:class|record)\s+(\w+)", text):
            class_name = decl.group(1)
            block = text[text.find("{", decl.end()):]
            block = block[:match_brace(block, 0) + 1]
            fanout = sum(1 for t in type_names if t != class_name
                         and re.search(r"\b" + t + r"\b", block))
            for name, params, body in method_bodies(block):
                rows.append({
                    "class": class_name,
                    "method": name,
                    "loc": logical_lines(body),
                    "cc": cyclomatic(body),
                    "nest": max_nesting(body),
                    "params": param_count(params),
                    "fanout": fanout,
                })

    measured = [r for r in rows if r["loc"] > 5]
    measured.sort(key=lambda r: (-r["cc"], -r["loc"], r["class"], r["method"]))

    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f, delimiter=";")
        writer.writerow(["Class", "Method", "LOC", "CC", "Nesting", "Params", "FanOut"])
        for r in measured:
            writer.writerow([r["class"], r["method"], r["loc"], r["cc"],
                             r["nest"], r["params"], r["fanout"]])

    physical = sum(len(t.splitlines()) for t in texts.values())
    logical = sum(logical_lines(t) for t in texts.values())
    clones = find_clones(texts)
    print(f"файлів: {len(files)}")
    print(f"типів: {len(type_names)} -> {', '.join(sorted(type_names))}")
    print(f"методів усього: {len(rows)}; у таблиці (LOC>5): {len(measured)}")
    print(f"рядків фізичних: {physical}; логічних: {logical}")
    print(f"клонів 6+ рядків: {len(clones)}")
    for g in clones:
        print(f"  клон {g['lines']} рядків: " + ", ".join(g["places"]))
    print(f"CC max: {max(r['cc'] for r in measured)}; "
          f"CC avg: {sum(r['cc'] for r in measured) / len(measured):.1f}")
    print(f"найдовший метод: {max(r['loc'] for r in measured)} рядків")
    print(f"макс. вкладеність: {max(r['nest'] for r in measured)}")


if __name__ == "__main__":
    main()
