#!/usr/bin/env bash
# 一次性：把 RuntimeRepair / DataAcquisition / RenderingReview / Evidence 组复制进工作区
set -u
LOG=$(cygpath -w /e/ZZZ/ZCode/.inclusion-robocopy.log)
: > /e/ZZZ/ZCode/.inclusion-robocopy.log

pairs=(
  "RemielleRuntimeRepair|RuntimeRepair"
  "RemielleDataAcquisition|DataAcquisition"
  "RemielleRenderingReview|RenderingReview"
)
for ev in RemielleModelReadiness CharacterShaderEvidence FrameBloomShaderEvidence FrameLightingEvidence FrameLightingPassEvidence FramePipelineShaderEvidence FrameSceneNativeInstanced FrameSceneRawVertexInputs SceneLitMaterialEvidence SceneLitShaderDump SceneLitShaderEvidence MaterialRawDump visual-acquisition; do
  pairs+=("$ev|Evidence/$ev")
done

for pair in "${pairs[@]}"; do
  src="${pair%%|*}"
  dst="${pair##*|}"
  sw=$(cygpath -w "/e/ZZZ/local-only/$src")
  dw=$(cygpath -w "/e/ZZZ/ZCode/20_ReverseEngineering/$dst")
  cmd //c "robocopy $sw $dw /E /XJ /MT:16 /R:2 /W:5 /NFL /NDL /NP /LOG+:$LOG" >/dev/null 2>&1
  echo "EXIT $src=$?"
done
echo ALL_DONE
