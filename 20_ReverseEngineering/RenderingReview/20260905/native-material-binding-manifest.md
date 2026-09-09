# Remielle 原生材质运行时绑定清单

- 结果：**PASS**
- 19 个 Remielle draw 共 215 个资源槽，归并为 39 份唯一无损 payload（122.86 MiB）。
- 其中 36 份是可导入的捕获静态纹理；t0 四层 R16 阴影、角色 R16 阴影和 t1 结构化场景缓冲必须由实时生产器替换。
- API 日志恢复了 3 个真实 D3D11 sampler handle 和 7 种 PS/材质绑定模式；同一个 `77bdd348772c62c8` PS 确有两种 s3 绑定，不能用一个静态 inline sampler 假装覆盖全部 draw。当前取得精确描述符：0/3。

## 唯一资源格式

| 格式 | 数量 |
|---|---:|
| `BC1_UNORM` | 1 |
| `BC1_UNORM_SRGB` | 5 |
| `BC6H_UF16` | 12 |
| `BC7_UNORM_SRGB` | 10 |
| `R16G16B16A16_FLOAT` | 1 |
| `R16_TYPELESS` | 2 |
| `R8G8B8A8_UNORM` | 6 |
| `R8G8B8A8_UNORM_SRGB` | 1 |
| `StructuredBuffer` | 1 |

## 已关闭和未关闭的边界

资源类型、尺寸、mip、数组层数、格式、payload SHA-256、Shader 访问操作、sampler 槽、实际 handle、draw/网格/子网格/材质关系均已写入 JSON。旧 2026-09-04 API 日志没有保存 `D3D11_SAMPLER_DESC` 的 Filter/Address/ComparisonFunc 字段，因此当前清单只确认三个状态对象的身份和逐 draw 槽位。实验补丁曾因签名校验失败而撤回，目前运行的是官方原版。不能指望再次 F8 自动得到描述符；源 TextureSettings 也不能替代运行时 sampler 的 GetDesc 证据。

## 产物

- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\native-material-binding-manifest.json`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\native-material-binding-manifest.md`
