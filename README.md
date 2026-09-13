# MapBuilder（Kenney 地编工程）

独立关卡编辑工程：在 Unity 里用 Tilemap 拼图，导出 `.map.json`，本工程可用 **PlayScene** 回放验图，再交给游戏工程 **shooting** 本地加载。

- Unity：**2022.3.x**（与 shooting 一致）
- 交付物：**仅 JSON**（不要交 `.unity` 场景当正式关卡）
- 远程仓库：`git@gitee.com:shanghai-zero-maze-network/map-builder.git`

---

## 一、首次打开工程

1. 用 Unity Hub 打开本仓库根目录（含 `Assets/`、`Packages/`、`ProjectSettings/`）。
2. 等待右下角资源导入完成（首次会较久）。
3. 打开场景：`Assets/Scenes/MapScene.unity`。
4. Scene 视图点顶部 **2D**，在 Hierarchy 里双击 `KenneySampleMap` 聚焦地图。

若整图粉红：确认存在目录  
`Assets/Arts/kenney_top-down-shooter/TileAssets/`  
（示例地图引用的是这里的 Tile，不是只有 `TilemapReady`）。然后 `Assets → Refresh`。

---

## 二、制作地图（完整步骤）

### 2.1 打开 Tile Palette

1. 菜单：`Window → 2D → Tile Palette`
2. 窗口顶部下拉选择笔刷盘，例如：
   - `Kenney_Master_All`（全部）
   - `Kenney_Ground` / `Kenney_Floors` / `Kenney_Walls_*` / `Kenney_Props` 等
3. 选中某个 Tile，再在 Scene 里刷到对应层上。

若下拉里没有 `Kenney_*`：

- `Tools → Kenney → Create Tile Palettes Only`
- 仍没有：`Tools → Kenney → Prepare Tilemap Resources For Editors`（会重建 `TilemapReady`）

### 2.2 选对图层再刷

Hierarchy 结构：

```
KenneySampleMap          ← 根（必须叫这个名字，导出靠它）
├── Ground               ← 草地/泥土大底
├── Floor                ← 室内地板
├── Walls                ← 墙（带碰撞）
├── Decor                ← 贴花（地毯、玻璃条等）
├── Props                ← 家具/箱子/灌木等
└── Characters           ← 仅预览小人，不进 JSON
```

**操作：** 在 Hierarchy **先点中要画的那一层**（如 `Walls`），再到 Scene 里刷。  
刷错层会导致导出后排序/碰撞不对。

| 层 | Sorting | 用途 | 碰撞 |
|----|---------|------|------|
| Ground | 0 | 室外大底，建议铺满 | 无 |
| Floor | 1 | 室内地板 | 无 |
| Walls | 2 | 墙体、门洞两侧 | 有（TilemapCollider2D） |
| Decor | 3 | 装饰贴花 | 无 |
| Props | 4 | 家具道具 | 无 |

### 2.3 常用编辑操作

| 操作 | 说明 |
|------|------|
| 画笔 Brush | Palette 里选 Tile 后在 Scene 点击/拖拽 |
| 橡皮 Eraser | Palette 工具栏切换 Eraser 擦除 |
| 框选/填充 | 用 Palette 自带的 Box / Fill（视 Unity 版本工具栏而定） |
| 保存 | `Ctrl+S` 保存 `MapScene`（方便下次继续改；正式交付仍靠 JSON） |

墙体建议用成套转角/直墙 Tile（橙墙等），门洞处留空，两侧用门柱 Tile。

### 2.4 可选：重新生成示例大地图

需要一份参考布局时：

- `Tools → Kenney → Build Large Varied Map`（大地图样例）
- `Tools → Kenney → Build Sample-Style Map`（较小样例）

会重建 `KenneySampleMap` 内容，**覆盖当前场景里已有笔刷结果**，慎用。

### 2.5 制作检查清单

- [ ] 根物体名仍是 `KenneySampleMap`
- [ ] Ground 基本铺满，无大块空洞（除非刻意）
- [ ] 墙在 `Walls` 层，不在 Ground/Floor
- [ ] 道具在 `Props`，不挡关键通道时注意留路
- [ ] Scene 里目视正常（非粉红 Missing）
- [ ] 已 `Ctrl+S` 保存场景

### 2.6 格子备注（多格 Prefab 占位）

沙发、门这类多格物体：先用静帧砖铺满占地，再给**左下锚点**写备注。导出进 `cellNotes`；PlayScene / 游戏加载时清占位砖并换成 Prefab。

1. Hierarchy 点中对应层（如 `Props`）
2. Tile Palette 切成 **Select**，点左下那一格（或框选整块）
3. Inspector「单元格备注」（或 `Tools → Kenney → Map → Cell Notes`）填：列×行、开口、类型（Prefab 文件名）、描述
4. 点 **保存备注**，再 `Ctrl+S`

参考 Prefab 放 `Assets/KenneyShooter/Prefabs/`（文件名 = 类型）。放入后执行 `Rebuild Placeable Catalog`。没有 Prefab 时占位砖会留下，Console 会 Warning。

---

## 三、导出地图 JSON（完整步骤）

### 3.1 导出

1. 确认当前打开的是带 `KenneySampleMap` 的场景（一般是 `MapScene`）。
2. 菜单：`Tools → Kenney → Map → Export Active Scene Map To JSON`
3. 在弹出的保存框里选路径（默认目录为项目根下 **`Export/`**），文件名例如：`level_01.map.json`
4. 确定后会生成 JSON，并自动镜像到：
   - 项目根目录 `Export/`
   - `Assets/StreamingAssets/Maps/`（方便本工程内对照）

导出时会自动执行一次 `Rebuild Tile Catalog`。有格子备注会写入 `cellNotes`（formatVersion = 2）。

### 3.2 本工程验图（推荐先做）

不要在 `MapScene` 里点 Play 来验 JSON（那是场景里烘焙的砖，不是导出文件）。

1. `Tools → Kenney → Map → Play Exported Map`
2. 弹出框会打开项目根目录 **`Export/`**，列出其中的 `.json`
3. 选中要验的文件，点确认 → 自动拷到 `StreamingAssets/Maps/`，打开 `PlayScene` 并进 Play
4. WASD 平移、滚轮缩放、`F` 框选全图；左上角仍可临时切换其它已镜像的 json
5. 核对：层是否对、墙是否有碰撞、备注占地是否换成 Prefab（缺 Prefab 则砖还在）

也可手动打开 `Assets/Scenes/PlayScene.unity` 再点 Play。

### 3.3 交给游戏工程 shooting

1. 把 `Export/你的关卡.map.json` 复制到：  
   `shooting/Assets/StreamingAssets/Maps/`
2. 在游戏场景里找到挂有 `KenneyMapLoader` 的对象，把地图文件名配成对应 json（如 `level_01.map.json`）。
3. 进游戏验证加载（缺文件时可能保留场景里烘焙地图，以 Loader 逻辑为准）。

### 3.4 其他菜单

| 菜单 | 用途 |
|------|------|
| `Export Active Scene Map To JSON` | **日常交付用**，自选文件名 |
| `Export MapScene Sample (StreamingAssets)` | 固定导出 `sample_large.map.json` 到 StreamingAssets |
| `Play Exported Map` | 打开 Export/ 选 json，再进 PlayScene Play |
| `Rebuild Tile Catalog` | 仅重建 `KenneyTileCatalog.asset` |
| `Rebuild Placeable Catalog` | 扫描 Prefabs 目录，登记类型 → Prefab |
| `Cell Notes` | 格子备注窗口 |

---

## 四、目录说明

| 路径 | 内容 |
|------|------|
| `Assets/Scenes/MapScene.unity` | 编辑主场景（含示例大地图） |
| `Assets/Scenes/PlayScene.unity` | 读 JSON 验图（空场景 + Loader） |
| `Assets/Arts/.../TilemapReady/` | 分类 Tile + Palette（刷图用） |
| `Assets/Arts/.../TileAssets/` | **MapScene 示例图实际引用的 Tile**（缺了会整图粉红） |
| `Assets/Arts/.../PNG/Tiles/` | 原始 Kenney 切片 |
| `Assets/Editor/` | 资源准备 / 示例地图生成 |
| `Assets/KenneyShooter/Editor/` | JSON 导出、格子备注、验图菜单 |
| `Assets/KenneyShooter/Scripts/Map/` | JSON 契约 + Loader + 备注/Prefab |
| `Assets/KenneyShooter/Prefabs/` | 地编参考与加载替换共用 |
| `Assets/KenneyShooter/Config/KenneyTileCatalog.asset` | Tile id ↔ Tile 资产表 |
| `Assets/KenneyShooter/Config/KenneyPlaceableCatalog.asset` | 类型 ↔ Prefab |
| `Export/` | 交给程序的 JSON 出口 |
| `doc/` | 更细的 Tilemap / JSON 说明 |

---

## 五、不要做的事

- 不要往本工程塞 Photon Fusion / 玩法战斗逻辑（保持地编工程轻量）。
- 不要把 `.unity` 场景当正式关卡交付；**只交 `.map.json`**。
- 不要删除或改名根物体 `KenneySampleMap`，否则导出失败。
- 不要只拷 `TilemapReady` 却漏掉 `TileAssets` 再开示例场景（会粉红）。

---

## 六、故障排查

| 现象 | 处理 |
|------|------|
| 整图粉红 | 检查 `TileAssets/` 是否存在；`Assets → Refresh`；必要时重开 Unity |
| Palette 没有 Kenney | `Create Tile Palettes Only` 或 `Prepare Tilemap Resources For Editors` |
| 导出提示找不到 KenneySampleMap | 打开 `MapScene`，确认根物体名称 |
| PlayScene 是空的 | 先导出，确认 `StreamingAssets/Maps/` 有 json；缺砖则 `Rebuild Tile Catalog` |
| 备注没换成 Prefab | 类型是否等于 Prefab 文件名；是否 Rebuild Placeable Catalog |
| 交给 shooting 后地图不对 | json 是否放到 shooting 的 `StreamingAssets/Maps/`，且 Loader 文件名一致 |
| 墙穿模/无碰撞 | 墙是否画在 `Walls` 层 |

更细说明见：

- `doc/Kenney_Tilemap实用说明.md`
- `doc/Kenney_地图JSON交付与本地加载.md`
