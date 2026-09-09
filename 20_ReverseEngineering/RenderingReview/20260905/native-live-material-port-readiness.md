# Remielle 原生实时材质移植就绪审计

- 结果：**PASS**
- 已确认 Remielle 表面绘制：19；PS 变体：6；VS 变体：2。
- 19 个 draw 均有 5 组 PS 常量缓冲和全部已绑定纹理槽的无损 mip0 `.buf/.dds`；每个缓冲都不短于对应 Shader 声明，可按声明字节数直接绑定。
- VS→PS 接口完整：19/19；语义覆盖 TEXCOORD0–8 与 SV_POSITION。
- 19 个 draw 的实时网格/子网格都已解析：12 个通过‘全部 UV + 唯一材质’严格联接，其余由唯一逐索引拓扑补齐。两套捕获 VS 都只消费 UV0/2/3、不消费 UV1，因此 Hair 的 UV1 差异不阻断原生 PS 接口。
- 跨 draw 共 215 个无损纹理 mip0 绑定、39 份唯一捕获载荷、24 份唯一 PS 常量缓冲载荷。
- 原 F8 捕获边界：2 份完整、33 份只有 mip0、1 份 8 层数组只有第 1 层 mip0。
- 精确源流恢复后：33 个 Texture2D mip 链与完整 8 层 × 5 mip matcap 数组已补齐；当前暂存 36/36 份完整，剩余 0 份只有 mip0。
- 本地全量元数据扫描与 Crunch 解包已补齐先前 8 份缺项，原始压缩块及半精度数据保持原值；sampler 创建描述符仍不等同于源 TextureSettings。

## 结论

六个 PS、两套 VS、19 个 draw 的常量与所有纹理链都已落库，36/36 份静态纹理完整。两套顶点计算已替换编译候选的占位代码，原始 DXBC GPU 对照 19/19、44 组合成分支通过；详见 native-vertex-port.md。六套 PS 的 57 组独立测试也已逐位通过，详见 native-pixel-port.md。下一步检查 Unity 实际绑定读回、确认 sampler，并把捕获槽拆为每材质固定绑定与每帧动态绑定。

## 仍须实时化的输入

- 当前/上一帧蒙皮位置、对象矩阵、相机 VP。
- 相机、主光、屏幕尺寸、雾与 SceneManager 的 cb0–cb3 动态寄存器。
- t0 四级联比较阴影、t1 结构化场景光/胶囊数据、各 PS 不同槽位的角色阴影。
- 启用分支中的时间相关发光与 ScreenImage 输入。

## 产物

- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\native-live-material-port-readiness.json`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\native-live-material-port-readiness.md`
