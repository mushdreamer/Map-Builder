# PSD / 拆分 PNG 到 Unity 地图的自动还原方案

## 0. 结论与本次审阅边界

本任务应被实现为一条**受 Gameplay 约束的视觉逆向装配流水线**，而不是“根据 Wall / Cover 轮廓随机铺相似素材”：

1. TMX 只负责冻结 Gameplay 几何；
2. PSD 的图层像素、图层 bounds、组树和叠放顺序负责提供实例候选；
3. 最终合成图负责评价整个重建结果；
4. 拆分 PNG 是必须被识别和尽量完整使用的资产字典；
5. 求解器输出带来源、置信度和误差的实例清单；
6. Unity 只消费已求解的清单，批量生成 Prefab、场景预览和可导出数据；
7. 低置信度或存在多个等价解的实例进入人工 Review，而不是被静默猜测。

初次方案审阅时仓库还没有生产样本，因此当时无法验证 PSD 图层可用率、命名规律、对齐误差或最终还原率。现在 Milestone 0 由 `tools/station_reconstruction/audit.py` 执行；它可以直接读取生产 ZIP 或解压目录并生成可复现的 manifest、健康报告和 exact-match 候选。任何样本更新都应先重跑该只读体检，再决定是否进入 Unity 写场景阶段。

---

## 1. 现有工程审阅结果

### 1.1 必须保留的结构

- 地图根节点约定为 `KenneySampleMap`。
- 固定 Tilemap 顺序为 `Ground / Floor / Walls / Decor / Props`，默认 sorting order 为 `0..4`。
- `KenneyPlaceable` 保存 Prefab 在 0° 时的 `nativeCols / nativeRows / nativeOpening`。
- `KenneyPlaceableCatalog` 以 `type` 查 Prefab，同时可取得原生 footprint 与 opening。
- `KenneyCellNoteStore` 随 Scene 保存备注；`KenneyCellNote` 使用**左下格为锚点**，footprint 沿 Unity `+X / +Y` 增长。
- 当前 Export 将五个 Tilemap 序列化到 `layers`，将 `KenneyCellNoteStore.notes` 复制到 `cellNotes`。
- Runtime Loader 先恢复 Tilemap，再按 `cellNotes.type` 查 Catalog、清掉 note footprint 对应的 Tile，然后在 footprint 几何中心实例化 Prefab。

这些约定可继续作为 Gameplay 骨架与兼容入口，不应推倒重写。

### 1.2 当前坐标系

现有 TMX importer 只支持有限、orthogonal、CSV 编码地图。Tiled CSV 按左上到右下读入：

```text
Tiled cell:  tx = i % width, ty = i / width   (原点左上，Y 向下)
Unity cell:  ux = tx,          uy = height - 1 - ty (原点左下，Y 向上)
```

Unity 的单元格边界为 `[x, x+1] × [y, y+1]`，格中心为 `(x+0.5, y+0.5)`；现有 Loader 也是按 footprint 的世界几何中心放置 Prefab。PSD / 概念图通常是左上原点且 Y 向下，因此经标定后的基础变换应写成：

```text
u = (px - originPxX) / pixelsPerCellX
v = mapHeight - (py - originPxY) / pixelsPerCellY
```

这里的 `(px, py)` 必须使用 PSD 文档坐标，不可直接使用裁剪后 PNG 的局部坐标。若概念图存在额外留白、非方形像素、整体缩放或轻微旋转，应先求一个 PSD→TMX 的相似/仿射变换，再转换到 Unity；不可假定图片左下角就是 cell `(0,0)`。

### 1.3 当前实现不能表达验收目标的部分

`KenneyCellNote` 目前只能可靠表达：层、整数锚点、整数 footprint、四向旋转、opening、type 和 description。它不能无损表达：

- 亚格/任意像素位置与 pivot offset；
- 非 90° 旋转；
- 非单位或非均匀缩放、镜像；
- 单实例 sorting order、PSD z-index、sorting layer；
- “视觉 bounds”与“碰撞 footprint”分离；
- 同一资源多次实例的稳定 ID；
- 匹配来源、置信度、人工确认状态；
- Prefab 自身的碰撞形状和 `NoColition_` 语义。

此外，当前 Loader 会把每个 Prefab 的所有 `SpriteRenderer.sortingOrder` 强制改成所属 Tilemap 的 order，这会摧毁 Prefab 内多 Sprite 的相对排序；它还会清除 note 覆盖的 Tilemap 单元。如果把 TMX Wall 骨架直接作为 placeable footprint 占位，这一步可能删除权威 Gameplay 墙体。当前导出器也只遍历 Tilemap 和 notes，不会导出 Scene 中普通 GameObject 的 transform。因此，“仅在 Editor Scene 中摆出 100 个 SpriteRenderer”无法通过正式 JSON/Runtime 链路验收。

### 1.4 TMX 信息的可靠程度

当前 importer：

- 对 `Wall` 和可选 `Cover` 只读取“是否非空”，会丢弃原 tile ID、Tiled flip flags（只剥离）、自定义属性及对象层信息；
- 把 Wall 和 Cover 都画进同一 `Walls` Tilemap；重叠时 Cover 视觉上覆盖 Wall；
- Floor 不是从 Tiled `Floor` 层读取，而是根据 Wall 做边界 flood-fill 后推断封闭内部；
- `Ground` 填满全图，`Decor / Props` 被清空。

因此，在当前代码路径下，**Wall/Cover occupancy 可作为约束，推断 Floor 只能作为临时预览，不应宣称等价于原始 Tiled Floor**。正式数据体检必须确认真实 TMX 是否确有 Floor 层、对象层、旋转/属性或无限地图；新版解析器应保存原始层语义，而非沿用当前丢失信息的布尔解析结果。

---

## 2. Ground Truth 分级

### A 级：硬约束（自动流程不得破坏）

1. TMX 的地图尺寸、tile size、Wall occupancy、Cover occupancy；若真实 TMX 有明确 Floor，则 Floor occupancy 也属于 A 级。
2. 拆分 PNG 文件集合、原始像素、alpha、尺寸。每个文件应有稳定 `assetId`（建议内容 SHA-256 + 相对路径），重命名不影响身份。
3. 已由人确认的坐标标定点、碰撞分类和实例匹配。
4. `NoColition_` 明确表示“该资产实例自身不生成碰撞”；拼写虽为 `Colition`，解析必须按实际前缀兼容。

### B 级：强证据（可自动接受，但需可回溯）

1. PSD 图层/组的文档坐标 bounds、可见性、opacity、blend mode、clipping mask、父子层级与 z-order。
2. PNG 与某个 PSD layer/group 在像素和 alpha 上近乎精确吻合的匹配。
3. PNG 在最终概念图中经遮挡感知模板匹配得到的唯一高分位置。
4. 文件名的 `1x1 / 1x2 / 1x3 / 2x2` 尺寸提示与 alpha bounds、TMX occupancy 同时一致。

### C 级：软证据（用于排序候选，不可单独定案）

1. PSD layer 名与 PNG 文件名的模糊相似度；需求已经说明二者不能直接对应。
2. 单纯按颜色直方图、感知 hash、尺寸或 alpha 面积的相似度。
3. 从 Wall/Cover 形状猜具体美术资产。
4. “同类素材均匀轮换”或为了覆盖地图而重复少数资产。

最终概念图是**全局视觉 Ground Truth**，但不是每个实例的完整像素真值：被上层遮住的像素不可见，重复纹理会造成多解，阴影/调色/组混合也会影响模板匹配。它最适合做全局渲染误差与可见区域校验；PSD 则负责补足遮挡后的实例和 z-order。

---

## 3. 建议的核心数据模型

不要把算法临时数据直接塞进 Scene，也不要继续让 `cellNotes` 同时承担 Gameplay、视觉 transform 和审核记录。建议保留 v2 字段并新增 format v3 的独立 `placements`：

```json
{
  "formatVersion": 3,
  "layers": ["现有 Tilemap 数据，继续保存权威 Gameplay 骨架"],
  "cellNotes": ["继续兼容旧的规则型 Prefab 工作流"],
  "placements": [{
    "instanceId": "稳定 GUID",
    "assetType": "1x2_17",
    "prefabKey": "Art/1x2_17",
    "semantic": "Wall|Cover|Floor|Decor|Prop",
    "position": {"x": 12.375, "y": 8.5, "z": 0},
    "rotationDeg": 90.0,
    "scale": {"x": 1.0, "y": 1.0},
    "sortingLayer": "MapArt",
    "sortingOrder": 231,
    "psdZIndex": 231,
    "collidable": true,
    "footprint": {"x": 12, "y": 8, "width": 1, "height": 2},
    "clearSourceTiles": false,
    "confidence": 0.97,
    "reviewState": "autoAccepted|needsReview|confirmed|rejected",
    "evidenceId": "match-report entry id"
  }]
}
```

设计原则：

- `layers` 是 Gameplay 事实，`placements` 是视觉/实体装配事实；二者默认并存，而不是互相覆盖。
- `clearSourceTiles` 默认 `false`。只有旧式“占位 tile 替换 Prefab”的 `cellNotes` 才保持现有清砖行为。
- `footprint` 是碰撞/Gameplay 占地，不等于 sprite alpha bounds，也不决定视觉 pivot。
- `assetType/prefabKey` 使用 PNG 文件导入后的稳定、唯一键；显示名可以改，但键不可碰撞。
- transform 用 float 保存并完整往返；四向规则资产仍可继续用现有 `KenneyCellNote`。
- sorting 必须逐实例保存。Prefab 子 renderer 保存相对 order，加载时应做 `instanceBaseOrder + childRelativeOrder`，不可全部覆盖成同一个值。
- 自动分析的中间文件（PSD layer inventory、候选矩阵、残差 heatmap）不进入运行时 JSON；正式 JSON 只携带运行必要字段和可选审计引用。

若短期不能升级 Runtime，可先将 v3 placement 一对一降级为扩展 note，但这会丢失任意 transform / sorting，不建议作为最终方案。

---

## 4. 最高成功率的求解流水线

### 阶段 A：只读数据体检与标准化

建立一个可重复运行、不会修改源文件的分析工具（推荐 Python CLI；PSD 解码与图像优化比直接塞进 Unity Editor 更容易测试）：

1. **TMX inventory**：记录 orientation、finite/infinite、map/tile 尺寸、所有 layer/group/object、offset、opacity、properties、GID flip flags；导出 Wall/Cover/Floor 二值 mask。
2. **PSD inventory**：记录文档尺寸/色彩模式、完整 layer tree、每层 bounds、可见性、opacity、blend、mask、clipping、smart object/linked asset、text/effect；分别导出 raw pixels、composited layer/group pixels 和最终 composite。
3. **PNG inventory**：对每个 PNG 计算尺寸、非透明 alpha bounds、质心、透明边距、颜色/alpha hash、感知 hash、边缘图、可能的 cell footprint、文件名前缀语义。
4. **一致性报告**：最终概念图与 PSD composite 的尺寸及像素误差；PNG 数量、重复内容/近重复内容；缺 alpha、色彩空间差异、premultiplied alpha、PSD 不支持效果等。
5. 所有产物落入带 source hash 的 `reconstruction.manifest.json`，保证输入不变时结果可复现。

**第一道 Go/No-Go**：若 PSD composite 与最终概念图明显不同，或关键 smart object/效果不能正确栅格化，应优先让 Photoshop 脚本导出“每个可见层/组选区 + bounds + z-order + composite”，不要在错误的 PSD 解码结果上继续优化。

### 阶段 B：坐标标定

不要手工猜 pixels-per-cell。依次尝试：

1. 从 TMX map pixel size 与 PSD/概念图边界得到初始 scale；
2. 将 TMX Wall/Cover mask 栅格化，和概念图/PSD 的结构边缘做多尺度搜索；
3. 优化 translation、uniform/non-uniform scale，必要时允许极小 rotation；
4. 在 Unity Review 窗口显示 TMX 网格 overlay，让人确认 2–4 个控制点；
5. 将最终矩阵与 RMS cell error 固化到 manifest。

硬验收建议：关键 Gameplay 边界的标定 RMS ≤ 0.1 cell；超过阈值时禁止自动写 Scene。

### 阶段 C：PNG ↔ PSD 内容身份匹配

这是恢复“具体资产”的主路径，优先级应高于在整张 composite 上盲搜：

1. 针对每个 PNG 与每个 PSD leaf layer、可合并 sibling group、smart-object raster 建候选；
2. 先用尺寸/alpha bounds/hash 剪枝，再计算 alpha-aware 像素误差、边缘相似、颜色差异；
3. 搜索允许的变换：先 exact translation/crop；再 90° 旋转和镜像；最后才放开有限 scale 与任意 rotation；
4. 对“资源是从 PSD 中裁剪导出”的情况，用 alpha 非零像素与 PSD 未遮挡像素做 exact/near-exact 对齐；
5. 一个 PNG 可对应多个 PSD 实例，一个 PSD 实例只能选择一个资产；完全相同的 PNG 内容要按不同 `assetId` 保留歧义，而不是擅自合并文件；
6. 保存 top-K 候选、分数差距和证据图。最佳候选很高但与第二名接近时仍应 Review。

PSD layer 名只作为极弱 tie-breaker。若一个美术资源由多个 PSD layer/group 组合而成，候选生成器应测试“同父组内相邻图层的小规模组合”，但设置组合数量上限，避免指数爆炸。

### 阶段 D：从最终 composite 补漏与遮挡验证

对未匹配 PNG 或无法可靠导出的 PSD 效果：

- 使用透明模板的可见像素在最终图上做多尺度、多角度匹配；
- loss 仅计算模板 alpha 与当前 z-order 下应该可见的像素；
- 对部分遮挡实例，结合 PSD 上方图层生成 occlusion mask；
- 先减去已确认实例渲染，再在 residual 上搜剩余资产；
- 阴影、发光等效果应区分“烘焙在 PNG 内”和“PSD layer effect 产生”，避免重复生成。

不建议用通用目标检测器作为主算法：只有约 100 个特定资产、没有标注训练集，而且本问题通常存在近乎逐像素的源证据。传统图像匹配 + PSD 结构 + 约束优化更可解释、更容易达到具体资产级准确率；学习式特征只能作为低分候选召回的补充。

### 阶段 E：全局约束求解，而非逐个贪心

将候选组成整数/图优化问题：

- 每个 PSD 实例至多选一个 PNG；
- 允许同一 PNG 被多次实例化，但增加“无证据重复”的惩罚；
- 对资产交付集合设置 coverage 奖励并报告 unused assets，**但不能为了 100% 使用而强塞资产**；
- collidable placement 的 footprint 与对应 TMX Wall/Cover mask 必须满足覆盖/不越界约束；
- `NoColition_` 不参与碰撞覆盖约束，但仍参与视觉误差；
- z-order 尽量服从 PSD 顺序，只有像素证据明确时才偏离；
- 总目标综合：像素 residual、alpha/edge mismatch、transform 偏离、Gameplay mask mismatch、重复惩罚、未解释 PSD 区域。

建议分区（room/connected component）求解后做全图 refinement，避免全图组合爆炸。输出必须包括：asset usage histogram、每实例置信度、TMX 几何违规数、全图差异图和遮挡冲突。

### 阶段 F：Unity 导入与 Prefab 批量生成

Unity Editor 只做确定性的消费与预览：

1. 把 PNG 按统一设置导入 Sprite（保留 full rect、正确 PPU、pivot 元数据；视觉 pivot 默认根据求解的文档坐标推导，不用 alpha 中心猜）。
2. 每个 PNG 创建一个独立 Prefab，根节点挂 `KenneyPlaceable`；`nativeCols/nativeRows` 来自已确认 footprint 或文件名 + alpha/TMX 的一致推断。
3. collidable 资产只生成与 Gameplay footprint 一致的 collider（优先规则化 Box/Composite，而非直接用复杂 alpha polygon）；`NoColition_` 不添加 collider。
4. 建立/增量更新 `KenneyPlaceableCatalog`。不要删除人工维护字段；内容 hash 未变化时保持 prefab GUID 稳定。
5. 导入 placements 到 `KenneySampleMap/Placeables` 仅作可编辑预览，同时把同一 placement 数据存入根节点的序列化 Store，使 Export 有唯一数据源。
6. 场景 overlay 同时显示概念图、TMX mask、实例 bounds、asset 名、confidence、collision footprint 和 residual heatmap。

Prefab 批量生成与 placement 导入要分成两个按钮/命令，均提供 dry-run、变更摘要和 Undo；避免重新分析时破坏人工确认结果。

### 阶段 G：人工 Review

自动接受建议要求同时满足：

- 资产身份分数 ≥ 高阈值；
- 第一与第二候选有足够 margin；
- 位置误差 ≤ 0.1 cell（规则实体）或约定像素阈值（纯 Decor）；
- transform 在允许范围；
- collidable footprint 无 TMX 违规；
- 局部重渲染误差低于阈值。

Review 队列优先展示：候选冲突、部分遮挡、近重复资产、任意角旋转/非均匀缩放、PSD 组合层、TMX mask 不一致、unused asset。操作者可以在 top-K 之间切换、拖动、调层级、确认 footprint；确认结果作为 override 文件保存，下次重跑不可覆盖。

---

## 5. Gameplay 几何保护策略

1. 导入 TMX 后生成不可变的 `GameplaySnapshot`（Wall/Cover/Floor mask + hash）。
2. 视觉重建不直接改写该 Tilemap；可以隐藏占位视觉，但 collider 来源保持独立和权威。
3. 每个 collidable placement 只声明它解释了哪些 TMX cells，不拥有删除这些 cells 的权力。
4. Export 前执行 validator：
   - 当前 mask 与 snapshot 完全一致；
   - 所有 collidable footprint 都在允许语义 mask 内；
   - `NoColition_` Prefab 不含 Collider2D；
   - 无未登记 Prefab key；
   - instance ID 唯一；
   - transform、sorting、数值均有限且可往返。
5. Runtime Load 后再次计算碰撞 occupancy，与 JSON snapshot hash 对比；不一致时明确报错，而不是静默继续。

Cover 是否与 Wall 使用同一物理碰撞层、是否影响子弹/玩家不同，需要玩法方明确。当前工程只有 `Walls` Tilemap collider，尚不能保真表达两类物理语义，实施前必须决策。

---

## 6. Export / Runtime 接入方案

推荐增量升级而非另起一套旁路：

- `KenneyMapFile.formatVersion` 升到 3，保留所有 v2 字段；新增 `placements` 与可选 `gameplayGeometryHash`。
- 新增 `KenneyPlacementStore` 挂在 `KenneySampleMap`，Editor 场景实例与 Store 通过 `instanceId` 同步。
- `BuildMapFileFromScene` 同时导出 Tilemaps、旧 notes 和 placements。
- `KenneyMapLoader` 先恢复 Tilemap，再加载旧 notes，最后加载 placements；v2 文件行为保持不变。
- placement 实例化必须应用完整 position/rotation/scale 与 per-instance sorting；Prefab 子 renderer 使用相对顺序。
- 自动资产 Prefab 必须被 Catalog 或独立、可序列化的 Placement Catalog 引用，禁止运行时依赖 AssetDatabase。
- Loader 清砖逻辑只对旧 `cellNotes` 生效，placement 默认不清 Gameplay Tile。
- 在 Editor 增加 JSON round-trip 测试：Scene → JSON → 空 Scene Load → JSON/渲染快照比较。

若游戏项目与 Map Builder 分仓，必须交付三件套并做版本校验：`.map.json`、Prefab/Catalog bundle、格式 schema/loader。只复制 JSON 无法恢复新 PNG Prefab。

---

## 7. 建议的实施顺序与决策门

### Milestone 0：真实数据样本体检（先做，不改现有地图）

需要提供至少一套完整、同版本的 TMX + PSD + final composite + PNG 文件夹。产出 inventory、PSD composite 差异、PNG 重复表、坐标 overlay 和 10–20 个代表资产的匹配 proof。

**Go 条件**：坐标可稳定标定；大部分代表 PNG 能从 PSD layer/group 或 composite 找到唯一/少量候选；PSD 解码结果可信。

### Milestone 1：离线 PoC

只输出 `reconstruction.manifest.json`、候选联系表和一张离线重渲染结果，不碰 Unity Scene。用真实数据测：

- exact/high-confidence identity 比例；
- instance precision/recall；
- asset usage coverage；
- transform 中位/95 分位误差；
- 全图 alpha-aware MAE/SSIM 与 residual heatmap；
- TMX occupancy violation = 0。

### Milestone 2：Prefab 工厂 + Review 窗口

先支持 exact/90°/uniform scale、高置信度实例；人工确认闭环稳定后，再加入任意角、组合层和遮挡推断。

### Milestone 3：JSON v3 + Runtime

完成 schema、export、loader、兼容测试、round-trip 和 Gameplay hash validator。不要等场景拼完后才补导出，否则会重复返工数据模型。

### Milestone 4：全图调优与验收

锁定自动阈值，清空 Review 队列，比较 Unity 正交相机截图与 final composite；输出 unused/overused assets、几何零违规报告以及运行时加载截图。

---

## 8. 验收指标

不要只用“看起来像”。建议每次构建自动产生：

| 维度 | 指标 |
|---|---|
| Prefab | PNG→Prefab 成功数、缺 Sprite/重复 key/错误 importer 数 |
| 具体资产 | 已解释 PSD 实例比例、top-1 人工抽检准确率、unused asset 列表 |
| 实例 | 重复次数差、位置/角度/缩放误差、漏实例/多实例 |
| 排序 | 与 PSD z-order 冲突数、遮挡边界像素误差 |
| 视觉 | Unity screenshot 对 final 的 alpha-aware MAE、SSIM、差异热图 |
| Gameplay | Wall/Cover/Floor mask XOR cell 数，目标为 0 |
| 数据链路 | Scene→JSON→Runtime 的 placement 数、asset key、transform、sorting 全量一致 |
| Review | auto-accepted / confirmed / unresolved 数量与原因 |

像素指标必须在统一相机、orthographic size、分辨率、色彩空间、抗锯齿和背景下计算，否则数值没有意义。

---

## 9. 正式开发前需要产品/玩法确认的问题

这些问题不阻止 Milestone 0/1，但会阻止最终 schema 与碰撞验收：

1. 真实 TMX 是否确实包含 Floor，还是 Floor 永远允许从 Wall 推断？
2. Cover 对玩家、敌人、子弹分别如何碰撞？是否必须与 Wall 分物理层？
3. PNG 的尺寸名表示视觉尺寸还是严格 Gameplay footprint？旋转后是否交换 occupancy？
4. `NoColition_` 是否仅禁用自身 collider，还是它也不应解释任何 Wall/Cover cell？
5. PSD 中隐藏层、备选组、smart object 是否属于交付内容？最终图与 PSD 哪个版本更新？
6. 美术导出 PNG 时是否保留统一 canvas/pivot，还是全部 tight crop？是否烘焙阴影和 layer effect？
7. 最终允许的 transform 范围：仅 90°、可镜像、可任意角、可非均匀缩放？
8. 同一 PNG 是否允许多次使用；约 100 个文件是否保证每个至少使用一次？
9. Runtime 渲染是否使用 Built-in/URP、Pixel Perfect Camera、透明排序轴或 SortingGroup？

---

## 10. 明确不采用的方案

- 不按 `1xN` 名字从相似素材池随机填充；名字只约束 footprint 候选。
- 不把所有 PNG 聚成十几个“代表素材”后重复铺设。
- 不仅从最终扁平图做无约束模板搜索；必须优先利用 PSD layer tree 与遮挡信息。
- 不把自动结果只保存为 Scene GameObject；正式数据必须进入 JSON 和 Runtime Catalog。
- 不让视觉 Prefab 删除或改写 TMX 权威 Gameplay 几何。
- 不把低置信度 top-1 当成事实；必须保留 top-K、证据和人工 override。
- 不一开始训练模型或大改 Loader；先用真实样本证明可标定、可匹配、可评价，再扩 schema。

这一路线把最难且最有价值的问题——“每个具体 PNG 在 PSD 成品中对应哪个实例”——放在 PSD 像素与结构证据上解决，同时让 TMX 始终作为 Gameplay 硬约束，让最终合成图负责全局视觉验收，并通过独立 placement 数据把完整 transform、排序和碰撞语义送进现有 Export / Runtime 主链路。
