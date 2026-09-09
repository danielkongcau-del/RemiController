# 生成资产副本的 SHA-256 清单（CSV）+ 组级摘要与 Merkle 根
# 用法: python generate_manifests.py   （从任意位置运行，路径写死为 ZCode 工作区）
import hashlib
import json
import os
import sys
from datetime import datetime, timezone

BASE = r"E:\ZZZ\ZCode"
OUT = os.path.join(BASE, r"20_ReverseEngineering\Manifests")
ONLY = sys.argv[1] if len(sys.argv) > 1 else None

# 组名 -> 物理根（相对 ZCode）；AssetVault 按顶层子目录拆分以保证单文件可渲染
GROUPS = {
    "AssetVault-assets": r"20_ReverseEngineering\AssetVault\assets",
    "AssetVault-recovered": r"20_ReverseEngineering\AssetVault\recovered",
    "AssetVault-metadata": r"20_ReverseEngineering\AssetVault\metadata",
    "AssetVault-tools": r"20_ReverseEngineering\AssetVault\tools",
    "AssetVault-skinning-verification": r"20_ReverseEngineering\AssetVault\skinning-verification",
    "AssetVault-config": r"20_ReverseEngineering\AssetVault\config",
    "RuntimeRepair": r"20_ReverseEngineering\RuntimeRepair",
    "DataAcquisition": r"20_ReverseEngineering\DataAcquisition",
    "RenderingReview": r"20_ReverseEngineering\RenderingReview",
    "Evidence": r"20_ReverseEngineering\Evidence",
    "Remielle_Main": r"10_Unity\Remielle_Main",
}

CHUNK = 1 << 20
SKIP_DIRS = {".git", "Library", "Temp", "obj", "Logs", "UserSettings"}  # 可再生产物不入清单


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            b = f.read(CHUNK)
            if not b:
                break
            h.update(b)
    return h.hexdigest()


def main():
    os.makedirs(OUT, exist_ok=True)
    summary_path = os.path.join(OUT, "summary.json")
    groups_run = {k: v for k, v in GROUPS.items() if ONLY is None or k == ONLY}
    summary = {
        "schema": "zcode.asset-manifest-summary.v1",
        "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "csvFormat": "sha256,size,path  |  path 相对各组根目录、按字节序排序 | UTF-8 无 BOM LF",
        "merkleAlgorithm": "对排序后每行构造 'sha256 <path>\\n'，按文件顺序拼接后整体 SHA-256 = merkleRoot",
        "groups": [],
    }
    if ONLY is not None and os.path.exists(summary_path):
        old = json.load(open(summary_path, encoding="utf-8"))
        summary["groups"] = [g for g in old.get("groups", []) if g.get("group") not in groups_run]
    for name, rel in groups_run.items():
        root = os.path.join(BASE, rel)
        rows = []
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for fn in filenames:
                p = os.path.join(dirpath, fn)
                rp = os.path.relpath(p, root).replace("\\", "/")
                rows.append((rp, os.path.getsize(p), sha256_file(p)))
        rows.sort(key=lambda r: r[0].encode("utf-8"))
        csv_name = f"{name}.sha256.csv"
        csv_path = os.path.join(OUT, csv_name)
        with open(csv_path, "w", encoding="utf-8", newline="\n") as f:
            f.write("sha256,size,path\n")
            for rp, size, dig in rows:
                f.write(f"{dig},{size},{rp}\n")
        cat = "".join(f"{dig} {rp}\n" for rp, _, dig in rows)
        summary["groups"].append({
            "group": name,
            "root": rel.replace("\\", "/"),
            "fileCount": len(rows),
            "totalBytes": sum(r[1] for r in rows),
            "manifestFile": csv_name,
            "manifestSha256": sha256_file(csv_path),
            "merkleRoot": hashlib.sha256(cat.encode("utf-8")).hexdigest(),
        })
        print(f"{name}: {len(rows)} files, manifest={csv_name}", flush=True)
    with open(os.path.join(OUT, "summary.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(summary, f, ensure_ascii=False, indent=2)
    print("SUMMARY_DONE", flush=True)


if __name__ == "__main__":
    main()
