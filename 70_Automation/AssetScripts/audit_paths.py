# D1 第一轮：主工程代码绝对路径审计（只读扫描，输出登记表）
# 用法: python audit_paths.py
import csv
import os
import re

ROOT = os.path.join(r"E:\ZZZ\ZCode", r"10_Unity\Remielle_Main\Assets")
OUT = os.path.join(r"E:\ZZZ\ZCode", r"00_ProjectHub\AuditExports")
PAT = re.compile(r"[A-Za-z]:[\\/]{1,2}ZZZ[^\"'\)\s,;]*")
WRITE_HINT = re.compile(r"Write|Create|Copy|Build|Export|Save|Log|Report|Delete|Move|Directory\.|Out\b|Output|destination|Dest\b", re.I)
READ_HINT = re.compile(r"Read|Load|Open|Exists|Import|GetFiles|Enumerate|Source|Input", re.I)

rows = []
for dirpath, _dirs, files in os.walk(ROOT):
    for fn in files:
        if not fn.endswith(".cs"):
            continue
        p = os.path.join(dirpath, fn)
        rel = os.path.relpath(p, ROOT).replace("\\", "/")
        try:
            lines = open(p, encoding="utf-8", errors="replace").read().splitlines()
        except OSError:
            continue
        for i, line in enumerate(lines, 1):
            if "ZZZ" not in line and "local-only" not in line:
                continue
            m = PAT.search(line)
            target = m.group(0) if m else "(相对/local-only 引用)"
            cls = "OUTPUT 疑似" if WRITE_HINT.search(line) else ("INPUT 疑似" if READ_HINT.search(line) else "UNCLASSIFIED")
            in_frozen = "local-only" in target or "local-only" in line
            rows.append((rel, i, cls, "冻结原件" if in_frozen else "其他", target[:160], line.strip()[:180]))

os.makedirs(OUT, exist_ok=True)
with open(os.path.join(OUT, "path-audit.csv"), "w", encoding="utf-8", newline="\n") as f:
    w = csv.writer(f)
    w.writerow(("file", "line", "class", "touches", "path", "context"))
    w.writerows(rows)

# 附带：StreamingAssets 内的已知非 .cs 绝对路径实例
extra = os.path.join(r"E:\ZZZ\ZCode", r"10_Unity\Remielle_Main\Assets\StreamingAssets")
hit_files = []
for dirpath, _d, files in os.walk(extra):
    for fn in files:
        if not fn.endswith((".json", ".txt")):
            continue
        p = os.path.join(dirpath, fn)
        try:
            txt = open(p, encoding="utf-8", errors="replace").read()
        except OSError:
            continue
        if "local-only" in txt or "E:\\\\ZZZ" in txt or "E:\\ZZZ" in txt:
            hit_files.append(os.path.relpath(p, os.path.join(r"E:\ZZZ\ZCode", "10_Unity", "Remielle_Main")).replace("\\", "/"))

print(f"cs 命中行数: {len(rows)} | 冻结原件相关: {sum(1 for r in rows if r[3]=='冻结原件')} | OUTPUT 疑似: {sum(1 for r in rows if r[2].startswith('OUTPUT'))}")
print("StreamingAssets 非代码绝对路径实例:", hit_files)
