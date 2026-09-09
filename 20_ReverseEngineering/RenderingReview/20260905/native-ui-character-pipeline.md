# Remielle 原生 UI 角色渲染链审计

结论：通过。角色展示页与时装商店页使用同一套 24 次角色绘制顺序；三个序列化 UI Shader 资产、六个运行时生成/resolve 哈希、五路 G-buffer、半分辨率层级和角色 DeferredShading 接力均已落到可重复核验的文件、哈希与像素统计。

## 两次抓帧

| 场景 | 角色 draw | bit 7 像素 | Stencil 分布 | Deferred 精确回放 |
|---|---:|---:|---|---:|
| display | 24 | 604,914 | 128:536555, 132:33046, 144:33793, 148:1520 | 604,914 |
| store | 24 | 358,402 | 128:316539, 132:14277, 144:26936, 148:650 | 358,402 |

两套 24 次 draw 的相对顺序、索引数量/起点、StencilRef、VS/PS 哈希完全一致。每个 draw 的五目标改写计数见 JSON 的 `cases[].perDraw`。

## Shader 身份

- `NapAvatarUI`：`2083248778.blk` / `CAB-71c2b9f7f898cf35b459c692a301a19b` / pathID `-1264760374214708068`。
- `NapAvatarUIEye`：`2507100060.blk` / `CAB-94df647bf5ba440b9265117744cc5e53` / pathID `497480590774261793`。
- `NapAvatarUIFace`：`2507100060.blk` / `CAB-5557432d335cd4adeb6dd4253321ca7d` / pathID `2497034685900247571`。
- 14 个角色 VS/PS 哈希在上述序列化资产中有精确 DXBC 命中。发丝生成轮廓和两级半分辨率处理共 6 个哈希在 17 个相关序列化 Shader、12 个 block 中无命中，边界记录为运行时生成。

## G-buffer 与 DeferredShading

最终 `o0/o1/o2/o3/oD` 的 payload 分别逐字节接入 DeferredShading `t2/t3/t4/t0/t1`。初始化 pass 在角色 stencil 区域把 HDR 置零，并把辅助 R10 置为固定中性字 `521725`（整数通道 509/509/0/0）；共享 `fbb07bad65276f2d` 分支随后恰好改写所有 bit 7 像素，展示页为 604,914，商店页为 358,402。后续 StencilRef 16/32/2/32 分支对这两份隔离目标均没有改写。

该像素 Shader 的序列化身份与战斗 draw 523 相同：`Hidden/Universal Render Pipeline/DeferredShading`，`273563795.blk`，pathID `2543301894137999429`，record 26，pass 3，关键字 `ENVIRO_SIMPLE_FOG`。GPU D3D11 精确回放中，两份 UI RGBA16F HDR 四通道逐位一致，辅助 R10 输出无异常值。

## 半分辨率层级

第一步读取全分辨率深度和法线，在 2×2 中选最小深度，按 `1/(cb0[62].x*depth+cb0[62].y)` 线性化，并保存对应法线、样本编号与最大/最小深度。第二步对范围纹理做 2×2 max/min 归并。3Dmigoto 的第二步导出只包含当前基级视图，因此报告仅断言可见基级与上一步范围目标逐字节相同，不虚构未导出的 mip 内容。

## 证据边界

此报告证明原生 UI 角色绘制和 Deferred 接力的抓帧真值及精确回放。它不宣称 Unity 当前 HoyoToon 材质已经复刻 NapAvatarUI 的每个实时光照细节；那属于下一阶段的 Unity 集成与逐项视觉差分。
