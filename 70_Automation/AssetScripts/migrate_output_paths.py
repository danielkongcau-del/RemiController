# D1-b：迁移主工程内纯输出常量的路径值（仅改字符串字面量值，不改任何语法结构）
# 规则：只处理两类行——① const string (Out|Output|destination|ReportPath|Project) = "E:/ZZZ/local-only/..."
#       ② FullPlayerSmoke 的 -remielleAuditOutput 回退默认值行
# 纯输入常量（Prep/Acq/Capture）与混合根（Folder/Root）不动（后者为 D1-c 拆分项）。
import os
import re

ROOT = os.path.join(r"E:\ZZZ\ZCode", r"10_Unity\Remielle_Main\Assets")
MAP = [
    ("E:/ZZZ/local-only/RemielleZcode/takeover-smoke/animcollection-runtime-verification.json",
     "E:/ZZZ/ZCode/90_Builds/ControllerIntegration/animcollection-runtime-verification.json"),
    ("E:/ZZZ/local-only/RemielleControllerImplementation/", "E:/ZZZ/ZCode/90_Builds/ControllerImplementation/"),
    ("E:/ZZZ/local-only/RemielleControllerDependencies/", "E:/ZZZ/ZCode/90_Builds/ControllerDependencies/"),
    ("E:/ZZZ/local-only/RemielleModelReadiness/", "E:/ZZZ/ZCode/90_Builds/ModelReadiness/"),
    ("E:/ZZZ/local-only/RemielleRuntimeRepair/", "E:/ZZZ/ZCode/90_Builds/RuntimeRepair/"),
    ("E:/ZZZ/local-only/RemielleRenderingReview/", "E:/ZZZ/ZCode/90_Builds/RenderingReview/"),
    ("E:/ZZZ/local-only/RemielleDataAcquisition/", "E:/ZZZ/ZCode/90_Builds/DataAcquisition/"),
    ("E:/ZZZ/local-only/RemielleHoyoToon/Assets/", "E:/ZZZ/ZCode/10_Unity/Remielle_Main/Assets/"),
]
OUT_CONST = re.compile(r"string\s+(Out|Output|destination|ReportPath|Project)\s*=")
OUT_INLINE = re.compile(r'(BuildTo|WriteAllText|WriteAllBytes)\s*\(\s*"E:/ZZZ/local-only/')
DIRS = set()
changed = 0
for dirpath, _dirs, files in os.walk(ROOT):
    for fn in files:
        if not fn.endswith(".cs"):
            continue
        p = os.path.join(dirpath, fn)
        lines = open(p, encoding="utf-8", errors="replace").read().splitlines()
        touched = False
        for i, line in enumerate(lines):
            if "E:/ZZZ/local-only/" not in line:
                continue
            if "ui-live-binding" in line:
                continue  # 混合根家族（读旧绑定+写验证），归 D1-c 拆分，不在本轮值级迁移
            if not (OUT_CONST.search(line) or OUT_INLINE.search(line) or "-remielleAuditOutput" in line):
                continue
            new = line
            for old, dst in MAP:
                if old in new:
                    # 记录输出目录（写到文件的场景取其目录）
                    val = new.split('"')
                    for seg in val:
                        if dst in seg:
                            DIRS.add(os.path.dirname(os.path.normpath("E:\\" + seg.split("E:/", 1)[1].replace("/", "\\"))))
                    new = new.replace(old, dst)
            if new != line:
                lines[i] = new
                touched = True
                changed += 1
                print(f"MIGRATED {os.path.relpath(p, ROOT)}:{i+1}")
        if touched:
            open(p, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")

print(f"共迁移 {changed} 行；预建输出目录 {len(DIRS)} 个")
os.makedirs(r"E:\ZZZ\ZCode\90_Builds\.dirs", exist_ok=True)
for d in sorted(DIRS):
    if "90_Builds" in d:
        os.makedirs(d, exist_ok=True)
print("DIRS_DONE")
