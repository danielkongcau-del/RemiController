# 旧逆向资料复核与补全入口

日期：2026-09-05。机器清单：[prior-reverse-reuse.json](prior-reverse-reuse.json)。

此前确实已得到大量可用的原生数据。此次重新读取权威合同、最新版视觉证据账本及其继承链，核对了 45 个带原始 SHA-256 的文件引用，全部仍在且哈希一致；另对六个镜头数据族的源文件和解码文件建立了当前哈希记录。这里没有重新执行游戏注入或采集。

同时核对了当前安装游戏的 `GameAssembly.dll`：SHA-256 与控制器定义合同固定的原生模块一致（`251f48b343393c3fd21a5d820f8dcc5ba3c45c7682d9682be7e918d011b73fef`）。现有控制器定义没有出现原生模块版本漂移；具体 Unity 实现仍需按合同逐项验证。

## 已有资料与接入方式

以下路径以 `E:\ZZZ` 为根；完整路径、哈希和逐条边界保存在机器清单中。

| 数据 | 当前证据 | 怎样用于后续开发 |
|---|---|---|
| 原生控制器定义 | `extracted/analysis/controller-completion-contract-20260831-v2/controller-completion-state.json`：14 项核心定义关闭；包含 52 个 Ability 根、6 个 AnimatorController、7 个行为树，冻结范围内无未解行为引用 | 按合同和 native evidence graph 实现输入、状态与动作事件适配；当前动画预览按钮不能当作已完成的游戏控制器 |
| 控制器引擎依赖 | 同目录 `controller-engine-audit-appendix.json`：996 个 native 方法、188 类序列化类型、11,622 个调用点；353 个间接调用点均有有限叶节点 | 使用已有 RVA、ABI、条件与数据结构证据；不凭名称重新猜逻辑 |
| 镜头作者数据 | `extracted/analysis/remielle-camera-authored-config-20260831-v1/camera-authored.json`：五份镜头文件、258 条完整消费记录；第六个文件为辅助数据族 | 使用原 FOV、跟随/注视偏移、持续时间、混入混出、轨道与曲线键；通过原名称关联动作事件 |
| 镜头计算与 Unity 提交 | `remielle-camera-native-formulas-20260831-v1/camera-native.json`，`remielle-camera-unity-submission-20260901-v1/camera-unity-submission.json` 和 `camera-game-to-unity-bridge.json` | 复用原计算和提交顺序；已有 16 个间接 Unity API 槽身份，不需要先设计一套替代运镜规则 |
| 特效静态装配 | 最新视觉账本引用：5,645 个可解析最新布局 MonoBehaviour、23,394 个 simulator 对象、735 个依赖对象和 334 个灯光关联 | 按源身份装配粒子、拖尾、材质和附加灯；这些计数是覆盖集合，不等于当前 Unity 已实例化的特效数量 |
| 特效运行时 | 1,894 个已观察特效均有身份和完整组件数组；609 对销毁生命周期闭合，29 次销毁明确是采集窗口开始前创建的对象 | 使用已有生命周期记录校对动作事件；不能把一个观测窗口当作全游戏所有特效的穷尽证明 |
| 精确特效音频归属 | 最新账本已关闭 Skill01 AirState Back trail 的 12/12 Audio plugin 原始记录 | 更早 synthesis 中此音频项仍为缺口的描述已经过时，以 2026-09-02 v5 账本继承的关闭证据为准 |
| 实际镜头关联 | 19,367 次 CameraState 提交无拷贝差异，4,728 个 Delay 模块成员已关联到活跃 EJK/Nap 链 | 可用于日后控制器到运镜的事件与提交验证；本轮没有新运行游戏 |
| 主菜单 Timeline | 18 条轨道、14 个放置 clip、7 个精确 pathID 的 AnimationClip；PlayableDirector 的 8 个非空绑定均已定位，另 10 个原本就是序列化 null | 复用装配与原曲线；运行时管理器绑定的 10 个 null 不能当作丢失文件补造 |
| 环境与光照 | Timeline 到 WeatherConfig 的 CAB/fileID/pathID 链、云/耀斑/天空烘焙依赖、Weather 实际消费者已解析；展示/商店抓帧常量另存于当前渲染复核目录 | 当前先使用两页有捕获依据的光照配置，再实现动态生产；不因旧 Weather 字段名缺口重复进行全量采集 |

视觉资料统一入口是 `extracted/analysis/remielle-visual-authority-ledger-20260902-v5/ledger.json`。该账本的 16 项关闭描述指“资产、原生定义或特定运行时观测已有证据”，不代表它们已经全部成为独立 Unity 功能。

## 仍然保留的资料边界

- 两个旧 WeatherConfig 对象的完整字节仍在，但历史命名字段布局未恢复。现有本地范围没有匹配 TypeTree/历史 assembly；不能按当前结构强行解释。若以后必须动态重现该旧布局，需要匹配历史元数据或明确的反序列化后对象字段证据。
- 三个原始游戏 D3D11 sampler 描述符仍未知。材质 TextureSettings、已捕获的句柄和 Unity 测试采样器都不能代替原始 `GetDesc`。
- 角色展示帧的 s2 未在本帧日志中记录；商店帧同槽已记录为 A。展示测试明确选择夹具 B，不能借商店记录倒推展示前一帧状态。
- 模型旧字节基线仍为 81/82：82 份文件均存在，一个 prefab 的旧字节尚未找回。本轮没有重建模型、动画或 Player，也没有重写旧基线。

## 本轮已经实际补上的内容

1. 移植展示/商店基础材质的两套顶点程序、五套像素程序，并按原始 DXBC 指令修正反编译数值问题。
2. 在独立 D3D11 和 Unity 中使用同一捕获几何、常量与纹理，16 项基础表面绘制均通过逐位对照；三项未修复负向用例能够检出错误。
3. 从旧提取入口找回 `Remielle_Ramiel_Wings_N` 的精确源包，恢复展示材质实际使用的 11 级原始纹理链。名字含 Ramiel 并不构成剔除共享纹理的依据。

结果、资产位置与复现方式见 [展示材质补全报告](ui-native-material/README.md)。

## 接下来的实现顺序

先完成每页剩余的描边、眼睛、模板修正与发丝后续绘制，连接已有 Deferred/Bloom/后处理回放。随后把固定捕获的骨架、相机、分辨率、光照和上一帧数据改成每帧输入，形成可独立运动的展示效果。再按现有控制器合同接动作事件，最后使用原特效装配与镜头记录接特效和运镜。

下一阶段主要是移植与接线。只有遇到上述明确缺口，才增加相应的定向取证；不再从零猜测已经恢复的控制器、特效或镜头数据。
