# 工作集校验：excluded-items 登记 + 对已删 legacy 的引用扫描
# 用法: python audit_workingset.py
import json
import os

COPY = r"E:\ZZZ\ZCode\20_ReverseEngineering\AssetVault"
OUT = r"E:\ZZZ\ZCode\20_ReverseEngineering\Analysis\WorkingSet"

manifest = json.load(open(os.path.join(COPY, "metadata", "manifest.json"), encoding="utf-8"))
excluded_cfg = json.load(open(os.path.join(COPY, "config", "excluded_sources.json"), encoding="utf-8"))
excl_class = {e["name"]: e["classification"] for e in excluded_cfg.get("exclusions", [])}

legacy = []
for s in manifest["sources"]:
    sid = s["id"]
    if sid.startswith("legacy-") or sid in excl_class:
        legacy.append({
            "id": sid,
            "files": s.get("files"),
            "bytes": s.get("logicalBytes"),
            "manifestNote": s.get("note", ""),
            "excludedSourcesClassification": excl_class.get(sid),
            "removedFromCopy": True,
            "retainedInOriginal": True,
        })

# 依赖断链扫描：副本保留文件中引用已删 legacy 目录名的（排除登记类文件本身）
HITS = []
REGISTRY_DOCS = {"manifest.json", "CATALOG.md", "coverage.json"}
for dirpath, _dirs, files in os.walk(COPY):
    for fn in files:
        if not fn.lower().endswith((".json", ".jsonl", ".ndjson", ".md", ".txt", ".csv")):
            continue
        p = os.path.join(dirpath, fn)
        is_registry = fn in REGISTRY_DOCS and os.path.basename(dirpath) == "metadata"
        try:
            txt = open(p, encoding="utf-8", errors="replace").read()
        except OSError:
            continue
        if "legacy-remielle-" in txt and not is_registry:
            HITS.append(os.path.relpath(p, COPY).replace("\\", "/"))

result = {
    "schema": "zcode.workingset-audit.v1",
    "generatedUtc": __import__("datetime").datetime.now(__import__("datetime").timezone.utc).isoformat(),
    "excludedItems": {
        "note": "原 manifest 保留为溯源数据不动；本表为清理后工作集的机器可读删除登记（依据=manifest note 与 excluded_sources 分类）",
        "legacyDirs": legacy,
        "mechanicalJunk": {"pycacheDirs": 6, "junkFiles": 78, "detail": "见 AssetVault-Cleanup-Report.md"},
        "retainedPendingReview": ["effect-p0c1-legacy（未点名后继，保守保留）", "controllers/work（注册在案的解析索引）"],
    },
    "dependencyScan": {
        "scope": "副本内文本类文件对已删 legacy-remielle-* 目录名的引用",
        "exemptions": ["metadata/{manifest.json,CATALOG.md,coverage.json}（来源注册/目录文档，引用 22 个来源 id 属预期）"],
        "referenceHits": HITS,
        "verdict": "PASS（无未豁免引用）" if not HITS else f"REVIEW（{len(HITS)} 处引用待人工判定）",
    },
}
os.makedirs(OUT, exist_ok=True)
json.dump(result, open(os.path.join(OUT, "workingset-audit.json"), "w", encoding="utf-8", newline="\n"), ensure_ascii=False, indent=2)
print(f"legacy 登记: {len(legacy)} 项 | 依赖引用命中: {len(HITS)} | 结论: {result['dependencyScan']['verdict']}")
for h in HITS[:10]:
    print("  HIT:", h)
