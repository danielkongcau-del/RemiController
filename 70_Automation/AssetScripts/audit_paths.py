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
MIXED_HINT = re.compile(r"const\s+string\s+(Folder|Root)\b")

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
            if MIXED_HINT.search(line) or "ui-live-binding" in line:
                cls = "MIXED 待拆分（读根兼写根，D1-c）"
            elif WRITE_HINT.search(line):
                cls = "OUTPUT 疑似"
            elif READ_HINT.search(line):
                cls = "INPUT 疑似"
            else:
                cls = "UNCLASSIFIED"
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

# 用法级检查（2026-09-09 起）：值指向 local-only 的常量若出现在写调用上下文 → 真实写回，DANGER
wc = re.compile(r"WriteAllText|WriteAllBytes|WriteAllLines|CreateDirectory|File\.Copy|File\.Delete|File\.Move|Directory\.Delete|Directory\.Move|new StreamWriter|File\.Open\(")
const_decls = {}
for dirpath, _d, files in os.walk(ROOT):
    for fn in files:
        if not fn.endswith(".cs"):
            continue
        p = os.path.join(dirpath, fn)
        for line in open(p, encoding="utf-8", errors="replace"):
            m = re.search(r'const\s+string\s+(\w+)\s*=\s*"([^"]*local-only[^"]*)"', line)
            if m:
                const_decls[(os.path.relpath(p, ROOT).replace("\\", "/"), m.group(1))] = m.group(2)
danger = []
EXEMPT = {  # 已核实误报：常量仅作为写入内容的一部分被哈希（读输入），写入目标已迁移
    ("ControllerIntegration/Editor/SourceVisibilityBuild.cs", "Contract"): "Sha(Root+Contract) 为读输入哈希，写入目标为已迁移的 QualifiedIntegrationAudit.Output",
}
for (relp, name), val in const_decls.items():
    if (relp.replace("\\", "/"), name) in EXEMPT:
        continue
    for i, line in enumerate(open(os.path.join(ROOT, relp), encoding="utf-8", errors="replace")):
        # (?<![.\w]) 排除跨类限定名（Xxx.Root 形式的读引用）；本文件裸常量才计入
        if wc.search(line) and re.search(r"(?<![.\w])" + name + r"\b", line):
            danger.append((relp, i + 1, name, val[:80]))
with open(os.path.join(OUT, "path-audit.csv"), "a", encoding="utf-8", newline="\n") as f:
    w = csv.writer(f)
    for relp, ln, name, val in danger:
        w.writerow((relp, ln, "DANGER 真实写回", "冻结原件", val, "用法级检查"))
print(f"用法级检查: local-only 常量 {len(const_decls)} 个 | 真实写上下文残留 {len(danger)} 处（目标 0）")
