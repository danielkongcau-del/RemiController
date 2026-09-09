# D1-c：混合根拆分——读根（Folder/Root，指向冻结原件）保留，写调用点改走新 WriteRoot 常量（90_Builds）
# 处理三类形态：
#  1) const string (Folder|Root) = "E:/ZZZ/local-only/..."  → 追加 WriteRoot 常量（映射后路径），写调用点替换
#  2) const string Out = Root + "..."（派生输出根）          → RHS 整体替换为映射后字面量
#  3) const string Out = "E:/ZZZ/local-only/...ui-live-binding..."（读写同根）→ 同 1 处理（Out 为读根，WriteRoot 为写根）
import os
import re

ROOT = os.path.join(r"E:\ZZZ\ZCode", r"10_Unity\Remielle_Main\Assets")
MAP = [
    ("E:/ZZZ/local-only/RemielleControllerImplementation/", "E:/ZZZ/ZCode/90_Builds/ControllerImplementation/"),
    ("E:/ZZZ/local-only/RemielleControllerDependencies/", "E:/ZZZ/ZCode/90_Builds/ControllerDependencies/"),
    ("E:/ZZZ/local-only/RemielleModelReadiness/", "E:/ZZZ/ZCode/90_Builds/ModelReadiness/"),
    ("E:/ZZZ/local-only/RemielleRuntimeRepair/", "E:/ZZZ/ZCode/90_Builds/RuntimeRepair/"),
    ("E:/ZZZ/local-only/RemielleRenderingReview/", "E:/ZZZ/ZCode/90_Builds/RenderingReview/"),
    ("E:/ZZZ/local-only/RemielleDataAcquisition/", "E:/ZZZ/ZCode/90_Builds/DataAcquisition/"),
]
WRITE_CALL = re.compile(
    r"WriteAllText|WriteAllBytes|WriteAllLines|CreateDirectory|File\.Copy|File\.Delete|File\.Move|"
    r"Directory\.Delete|Directory\.Move|new StreamWriter|File\.Open\(")
MIXED_DECL = re.compile(r'const\s+string\s+(Folder|Root|Out)\s*=\s*"E:/ZZZ/local-only/([^"]*)"')
DERIVED_OUT = re.compile(r'const\s+string\s+(Out|WOut)\s*=\s*Root\s*\+\s*"([^"]*)"')

def mapped(local_value):
    v = local_value.rstrip("/") + "/"
    for old, dst in MAP:
        if v.startswith(old):
            return dst + v[len(old):]
    return None

changed_files = 0
write_sites = 0
for dirpath, _dirs, files in os.walk(ROOT):
    for fn in files:
        if not fn.endswith(".cs"):
            continue
        p = os.path.join(dirpath, fn)
        src = open(p, encoding="utf-8", errors="replace").read()
        lines = src.splitlines()
        touched = False
        # 形态 2：派生 Out=Root+"..."
        for i, line in enumerate(lines):
            m = DERIVED_OUT.search(line)
            if m:
                # Root 常量是本文件的混合根；其映射基 = Root 的声明值
                rm = re.search(r'const\s+string\s+Root\s*=\s*"E:/ZZZ/local-only/([^"]*)"', src)
                if rm:
                    base = mapped("E:/ZZZ/local-only/" + rm.group(1))
                    if base:
                        new = f'const string {m.group(1)} = "{base}{m.group(2).lstrip("/")}"'
                        lines[i] = lines[i].replace(m.group(0), new)
                        touched = True
                        write_sites += 1
                        print(f"DERIVED {os.path.relpath(p, ROOT)}:{i+1}")
        # 形态 1/3：混合根声明 → 插入 WriteRoot；写调用点替换标识符
        decls = {}
        for i, line in enumerate(lines):
            m = MIXED_DECL.search(line)
            if not m:
                continue
            name, sub = m.group(1), m.group(2)
            if name == "Out" and "ui-live-binding" not in sub:
                continue  # 已迁移的纯输出 Out 不动
            base = mapped("E:/ZZZ/local-only/" + sub)
            if not base:
                continue
            decls.setdefault(name, base)
        for name, base in decls.items():
            wname = "WriteRoot" if name != "WriteRoot" else "WriteRoot2"
            decl_line = None
            for i, line in enumerate(lines):
                if re.search(r"const\s+string\s+" + name + r"\s*=", line):
                    decl_line = i
                    break
            if decl_line is None:
                continue
            indent = re.match(r"\s*", lines[decl_line]).group(0)
            lines.insert(decl_line + 1, f'{indent}const string {wname} = "{base}"; // D1-c 写根（读根保留 A 类）')
            touched = True
            for i in range(len(lines)):
                if i == decl_line + 1 or WRITE_CALL.search(lines[i]) is None:
                    continue
                if re.search(r"\b" + name + r"\b", lines[i]):
                    lines[i] = re.sub(r"\b" + name + r"\b", wname, lines[i])
                    write_sites += 1
        if touched:
            open(p, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
            changed_files += 1

print(f"改动文件 {changed_files} 个；写调用点迁移 {write_sites} 处")
