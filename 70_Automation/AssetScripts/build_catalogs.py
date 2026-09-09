# B1 v1：从 Vault 副本与主工程枚举全量资产目录（机器可查的 CSV + 摘要）
# 用法: python build_catalogs.py
import csv
import json
import os
from datetime import datetime, timezone

BASE = r"E:\ZZZ\ZCode"
V = os.path.join(BASE, r"20_ReverseEngineering\AssetVault")
OUT = os.path.join(BASE, r"20_ReverseEngineering\Analysis\catalogs")
EXTS = (".anim", ".acl", ".json", ".npz", ".yaml", ".raw", ".ress", ".resS", ".bytes", ".bin", ".glb", ".png", ".txt", ".md")

TIERS = {
    "highest-quality": os.path.join(V, r"recovered\animations\highest-quality"),
    "standalone-native": os.path.join(V, r"recovered\animations\standalone-native"),
    "scalar-full-source": os.path.join(V, r"recovered\animations\scalar-recovered\full-source"),
}


def stem(fn):
    for e in EXTS:
        if fn.lower().endswith(e) and len(fn) > len(e):
            return fn[: -len(e)]
    return fn


def write_csv(name, header, rows):
    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, name)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)
    return len(rows), p


def main():
    result = {"generatedUtc": datetime.now(timezone.utc).isoformat(), "catalogs": {}}

    # 1) 动画：分两张表——身份表（829 包身份，tier=恢复产品属性）与文件表（全部文件，含解码依赖）
    anim_rows = []          # 文件级
    identities = {}         # (source_id, cab, name) -> {tiers, files}
    for tier, root in TIERS.items():
        if not os.path.isdir(root):
            continue
        for dirpath, _dirnames, filenames in os.walk(root):
            rel = os.path.relpath(dirpath, root).replace("\\", "/")
            parts = rel.split("/") if rel != "." else []
            src_id = parts[0] if parts else ""
            cab = parts[1] if len(parts) > 1 else ""
            for fn in filenames:
                size = os.path.getsize(os.path.join(dirpath, fn))
                nm = stem(fn)
                anim_rows.append((tier, src_id, cab, fn, size))
                key = (src_id, cab, nm)
                rec = identities.setdefault(key, {"tiers": set(), "files": 0, "pkgFiles": 0})
                rec["files"] += 1
                rec["tiers"].add(tier)
                if tier in ("highest-quality", "standalone-native") and fn.lower().endswith((".npz", ".anim")):
                    rec["pkgFiles"] += 1
    n, _ = write_csv("animation-catalog.csv", ("tier", "source_id", "cab", "file", "size"), anim_rows)
    ident_rows = [
        (src, cab, nm, "|".join(sorted(rec["tiers"])), rec["files"])
        for (src, cab, nm), rec in sorted(identities.items())
    ]
    ni, _ = write_csv("animation-identities.csv", ("source_id", "cab", "name", "recovery_products", "files"), ident_rows)
    pkg_rows = [
        (src, cab, nm, "|".join(sorted(rec["tiers"])))
        for (src, cab, nm), rec in sorted(identities.items())
        if rec["pkgFiles"] > 0
    ]
    result["catalogs"]["animation"] = {
        "fileLevel": {"files": n,
                      "note": "文件级全枚举（含 scalar-full-source 解码依赖与报告文件；名称列可能是数字词干）"},
        "identityLevel": {
            "allIdentities": ni,
            "packageIdentities": len(pkg_rows),
            "packageNote": "恢复包身份（highest-quality .npz + standalone .npz/.anim），tier 为恢复产品属性不改身份；"
                           "对照 RECOVERY_STATUS 声称的 829 身份",
        },
    }
    write_csv("animation-names.csv", ("name", "tiers", "identity_count"),
              [(nm, "|".join(sorted({t for (s, c, n2), rec2 in identities.items() if n2 == nm for t in rec2["tiers"]})),
                len([1 for (s, c, n2) in identities if n2 == nm]))
               for nm in sorted({k[2] for k in identities})])

    # 2) 网格
    mesh_root = os.path.join(V, r"recovered\meshes\by-source")
    mesh_rows = []
    for dirpath, _d, filenames in os.walk(mesh_root):
        rel = os.path.relpath(dirpath, mesh_root).replace("\\", "/")
        parts = rel.split("/") if rel != "." else []
        src_id, cab = (parts[0] if parts else ""), (parts[1] if len(parts) > 1 else "")
        for fn in filenames:
            mesh_rows.append((src_id, cab, fn, os.path.getsize(os.path.join(dirpath, fn))))
    n, _ = write_csv("mesh-catalog.csv", ("source_id", "cab", "file", "size"), mesh_rows)
    result["catalogs"]["mesh"] = {"files": n, "note": "对照 README：178 个原始 Mesh 蒙皮 GLB（头发另有 UV1 派生版本）"}

    # 3) 材质与贴图
    mat_rows = []
    for sub in ("materials", "textures"):
        root = os.path.join(V, "recovered", sub, "by-source")
        if not os.path.isdir(root):
            continue
        for dirpath, _d, filenames in os.walk(root):
            rel = os.path.relpath(dirpath, root).replace("\\", "/")
            parts = rel.split("/") if rel != "." else []
            src_id, cab = (parts[0] if parts else ""), (parts[1] if len(parts) > 1 else "")
            for fn in filenames:
                mat_rows.append((sub, src_id, cab, fn, os.path.getsize(os.path.join(dirpath, fn))))
    n, _ = write_csv("material-texture-catalog.csv", ("kind", "source_id", "cab", "file", "size"), mat_rows)
    result["catalogs"]["material_texture"] = {"files": n, "note": "对照 README：95 材质包、410 纹理引用"}

    # 4) 控制器
    ctl_rows = []
    for sub in ("controllers", "controllers-v2-corrected"):
        root = os.path.join(V, "assets", "gameplay", sub)
        if not os.path.isdir(root):
            continue
        for dirpath, _d, filenames in os.walk(root):
            for fn in filenames:
                ctl_rows.append((sub, fn, os.path.getsize(os.path.join(dirpath, fn))))
    n, _ = write_csv("controller-catalog.csv", ("set", "file", "size"), ctl_rows)
    result["catalogs"]["controller"] = {"files": n}

    # 5) 特效与时间线：目录级清单
    inv_rows = []
    for sub in ("effects", "timeline"):
        root = os.path.join(V, "assets", sub)
        for entry in sorted(os.listdir(root)):
            p = os.path.join(root, entry)
            if os.path.isdir(p):
                cnt = sum(len(fs) for _, _, fs in os.walk(p))
                size = sum(os.path.getsize(os.path.join(dp, f)) for dp, _, fs in os.walk(p) for f in fs)
                inv_rows.append((sub, entry, cnt, size))
            else:
                inv_rows.append((sub, entry, 1, os.path.getsize(p)))
    n, _ = write_csv("effects-timeline-inventory.csv", ("domain", "entry", "files", "bytes"), inv_rows)
    result["catalogs"]["effects_timeline"] = {"entries": n}

    # 6) 主工程动作库索引
    idx_path = os.path.join(BASE, r"10_Unity\Remielle_Main\Assets\StreamingAssets\RemielleControllerMotions\index.json")
    if os.path.isfile(idx_path):
        try:
            idx = json.load(open(idx_path, encoding="utf-8"))
            result["catalogs"]["project_motion_bank"] = {
                "indexFile": "Assets/StreamingAssets/RemielleControllerMotions/index.json",
                "topLevelKeys": list(idx.keys())[:12] if isinstance(idx, dict) else f"list[{len(idx)}]",
            }
        except Exception as e:
            result["catalogs"]["project_motion_bank"] = {"error": str(e)}

    with open(os.path.join(OUT, "catalogs-summary.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
