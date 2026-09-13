# Kenney Tilemap 实用说明（关卡编辑）

本文说明如何用本工程已准备好的 **Kenney 瓦片资源 + Unity 自带 Tilemap / Tile Palette**，在现有示例地图基础上创作、扩展并保存地图。

面向：关卡策划 / 美术 / 程序中负责拼图的同学。

---

## 1. 这套东西是什么

本工程把 Kenney Top-Down Shooter 的切片，整理成 Unity Tilemap 可直接刷的资源，并提供若干编辑器菜单：

| 能力 | 作用 |
|------|------|
| 分类 Tile + Palette | 在 `Tile Palette` 窗口里选笔刷、画地图 |
| `MapScene` 示例大地图 | 已有分层 Tilemap，可直接改，不必从空白开始 |
| 一键准备资源菜单 | 资源丢了或 Palette 空了时可重建 |
| 一键生成示例大地图菜单 | 需要参考布局时重新生成一份样例 |

**创作主路径**：打开 `MapScene` → 打开 Tile Palette → 选层 → 刷图 → 保存场景。

---

## 2. 环境与入口

### 2.1 必要包

`Packages/manifest.json` 中需包含：

- `com.unity.2d.tilemap`
- `com.unity.2d.sprite`（建议保留）

若缺失，可用菜单重建资源前先确认 Package Manager 已装好 2D Tilemap。

### 2.2 关键场景

- 路径：`Assets/Scenes/MapScene.unity`
- 根物体：`KenneySampleMap`（带 `Grid`）
- 子层（从上到下绘制时注意层级）：

| 层名 | Sorting | 用途 | 备注 |
|------|---------|------|------|
| Ground | 0 | 草地、泥土、室外大底 | 整图铺满 |
| Floor | 1 | 室内地板（木板、方格、金属等） | 只铺室内 |
| Walls | 2 | 墙体 | 已挂 `TilemapCollider2D` |
| Decor | 3 | 地毯、窗玻璃条、纸屑等贴花 | 可穿透，无碰撞 |
| Props | 4 | 家具、箱子、灌木、柱子 | 装饰/遮挡 |

可选：`Characters` 为预览用小人（SpriteRenderer，不是 Tilemap）。正式关卡角色一般用 Prefab 另行摆放。

### 2.3 资源根目录

```
Assets/Arts/kenney_top-down-shooter/TilemapReady/
├── Tiles/                    # 分类 Tile 资产（约 524 个）
│   ├── 01_Ground/
│   ├── 02_Floors/
│   ├── 03_Walls_Orange/
│   ├── 04_Walls_Cyan/
│   ├── 05_Walls_Tan/
│   ├── 06_Water_Glass/
│   ├── 07_Nature/
│   ├── 08_Furniture/
│   ├── 09_Props/
│   └── 99_Misc/              # 其余未强分类的瓦片
├── Palettes/                 # Tile Palette 预制体
│   ├── Kenney_Ground.prefab
│   ├── Kenney_Floors.prefab
│   ├── Kenney_Walls_Orange.prefab
│   ├── Kenney_Walls_Cyan.prefab
│   ├── Kenney_Walls_Tan.prefab
│   ├── Kenney_Water_Glass.prefab
│   ├── Kenney_Nature.prefab
│   ├── Kenney_Furniture.prefab
│   ├── Kenney_Props.prefab
│   ├── Kenney_Misc.prefab
│   └── Kenney_Master_All.prefab   # 全部瓦片合集
└── 给地图编辑的使用说明.txt
```

原始 PNG 切片仍在：`Assets/Arts/kenney_top-down-shooter/PNG/Tiles/`（一般不用直接拖 PNG，用 Tile / Palette）。

---

## 3. 编辑器菜单（本工程提供的工具）

Unity 顶部菜单 **Tools → Kenney**：

| 菜单 | 何时用 |
|------|--------|
| **Prepare Tilemap Resources For Editors** | 首次接入、资源损坏、换机后缺 Tile/Palette 时：重新校正 Sprite、生成分类 Tile、生成 Palette，并确保 `MapScene` 分层存在 |
| **Create Tile Palettes Only** | Tile 资产已在，但 Palette 下拉为空/没画上瓦片时：只重建 Palette |
| **Build Large Varied Map** | 需要重新生成一份约 60×40 的多样式示例大地图（会覆盖 `KenneySampleMap` 内 Tilemap 内容） |
| **Build Sample-Style Map** | 同上入口（当前与大地图生成共用实现） |

> 注意：点「Build Large…」会**重建示例地图内容**。若你已手工改过图，请先另存场景或备份，再点生成。

脚本位置：

- `Assets/Editor/KenneyTilemapResourcePreparer.cs` — 资源准备 / Palette
- `Assets/Editor/KenneySampleMapBuilder.cs` — 示例大地图生成

---

## 4. 从零到一张可玩地图的完整流程

### 步骤 A：打开场景

1. 双击 `Assets/Scenes/MapScene.unity`
2. Hierarchy 展开 `KenneySampleMap`，确认五层都在
3. Scene 视图切到 **2D**（工具栏 2D 按钮），方便俯视刷图

### 步骤 B：打开 Tile Palette

1. 菜单：`Window → 2D → Tile Palette`
2. 窗口顶部下拉框选择 Palette，例如：
   - 铺草地 → `Kenney_Ground`
   - 铺室内 → `Kenney_Floors`
   - 砌墙 → `Kenney_Walls_Orange` / `Cyan` / `Tan`
   - 摆家具 → `Kenney_Furniture` / `Kenney_Props`
3. 若下拉没有 `Kenney_*`：执行 `Tools → Kenney → Create Tile Palettes Only`，等 Console 出现 Done 后再打开下拉

### 步骤 C：选中要画的层

在 Hierarchy **先点中**目标 Tilemap，例如：

- 画草地 → 选中 `KenneySampleMap/Ground`
- 画墙 → 选中 `KenneySampleMap/Walls`

Tile Palette 只会画到**当前选中的 Tilemap** 上。选错层是最常见问题。

### 步骤 D：刷图

1. Tile Palette 中点选一块瓦片
2. 工具栏选 **Paint（笔刷）** / **Box Fill（矩形填充）** / **Erase（橡皮）** 等
3. 在 Scene 视图格子上拖拽绘制
4. 建议顺序：
   1. `Ground` 铺满室外
   2. `Floor` 铺室内范围
   3. `Walls` 围墙、开门洞
   4. `Decor` 地毯、窗条、碎屑
   5. `Props` 桌椅箱桶植被

### 步骤 E：保存

- `Ctrl+S` 保存场景，或 `File → Save`
- 地图数据存在 `MapScene.unity`（以及场景引用的 Tile 资产）里
- **不要**只改 Palette 预制体当关卡；Palette 只是笔刷盘，关卡内容在场景的 Tilemap 上

### 步骤 F：给交互物写格子备注（可选）

多格动态物体先用静帧切成 1×1 铺满占地，再在**左下锚点**写备注。导出进 JSON；PlayScene / shooting 加载后清占位砖并换成 Prefab。

1. Hierarchy 点中对应层（如 `Props`）
2. Tile Palette 工具改成 **Select**，在 Scene 里点左下那一格（或框选整块占地）
3. Inspector 里 **Grid Selection** 顶部会出现「单元格备注」；也可 `Tools → Kenney → Map → Cell Notes`
4. 填 **类型**（Prefab 文件名）、**列×行**（地图占地）、**开口**（看摆好的图：上/左/下/右）。旋转会自动算
5. 点 **保存备注**，再 `Ctrl+S`。Prefab 原画来自 `KenneyPlaceable`，或该类型第一次保存时登记

框选了多格时可用「用当前选区作为占地」。参考 Prefab 放 `Assets/KenneyShooter/Prefabs/`，放入后 `Rebuild Placeable Catalog`。

### 步骤 G（可选）：新开一张空地图

1. `File → New Scene` 或复制 `MapScene` 改名（如 `Map_Forest.unity`）
2. 若新场景没有 `KenneySampleMap`：再跑一次  
   `Tools → Kenney → Prepare Tilemap Resources For Editors`  
   （会确保分层；不会强制清掉你已有场景里的画，但请确认当前打开的是目标场景）
3. 或从 `MapScene` 复制根物体到新场景，再清空各层 Tile 后重画

---

## 5. 墙体怎么拼才不会错边

Kenney 厚墙是「深色墙身 + 彩色描边」的模块件。每种颜色一套角/边，**不要混色拼同一圈墙**。

### 5.1 三套墙 Palette

| Palette | 风格 | 典型用途 |
|---------|------|----------|
| `Kenney_Walls_Orange` | 橙边 | 民宅 / Sample 风格 |
| `Kenney_Walls_Cyan` | 青边 | 实验室 / 科技房 |
| `Kenney_Walls_Tan` | 棕边 | 仓库 / 工业区 |

### 5.2 矩形房间外轮廓（橙墙为例）

程序生成时用的对应关系（便于对照 Palette 里的 `tile_xxx`）：

| 部位 | 橙墙 ID | 青墙 ID | 棕墙 ID |
|------|---------|---------|---------|
| 左上角 NW | 109 | 280 | 118 |
| 右上角 NE | 110 | 281 | 119 |
| 左下角 SW | 136 | 307 | 145 |
| 右下角 SE | 137 | 308 | 146 |
| 横墙 H | 111 | 282 | 120 |
| 竖墙 V | 138 | 309 | 147 |
| 南门西侧门框（橙边朝门） | 114 | 285 | 123 |
| 南门东侧门框 | 142 | 313 | 151 |

**矩形围一圈：**

1. 四角放 NW/NE/SW/SE  
2. 上下边用 H 填满（门洞位置留空）  
3. 左右边用 V 填满  
4. 南侧大门洞两侧用门框瓦（HEnd），中间两格不放墙，露出下面 Floor/Ground  

**内隔断：** 只用竖墙 V（或横墙 H）排成一条，中间留 1～2 格当门。

### 5.3 窗户

不要把半透明「玻璃条」直接替换整段厚墙（容易露黑边）。推荐：

1. 墙仍用完整 H/V  
2. 在 `Decor` 层叠 `Kenney_Water_Glass` 里的窗条（如横条 436、竖条 433）  

---

## 6. 分层与选砖建议

| 你想做的事 | 选哪层 | 建议 Palette |
|------------|--------|--------------|
| 大片草地 | Ground | `Kenney_Ground`（401 纯草 + 1/2/3 草丛） |
| 泥土小路 | Ground | `Kenney_Ground` 里偏棕的 dirt 系 |
| 室内木地板 | Floor | `Kenney_Floors`（42～47 等） |
| 瓷砖/方格 | Floor | `Kenney_Floors`（7～11、271～279 等） |
| 水面 | Ground 或 Floor | `Kenney_Water_Glass`（19/20 等） |
| 沙发桌椅 | Props | `Kenney_Furniture` |
| 箱子碎片 | Decor / Props | `Kenney_Props` |
| 树丛 | Props | `Kenney_Nature` |

`Kenney_Master_All`：一次看全 524 块，适合「找某一块」；日常拼图仍建议用分类 Palette，更快。

---

## 7. 推荐创作节奏（在示例图上改）

1. **保留**现有大地图的路网与建筑轮廓，先改局部  
2. 用橡皮清掉某一房间内部，再换地板风格  
3. 换墙色时：整圈同一 Palette，避免橙墙角接青墙边  
4. 家具对齐格子；沙发等组合件按 Palette 里左右中顺序摆（如 447/448/449）  
5. 每隔一段时间 `Ctrl+S`，并偶尔进 Play Mode 看层级与碰撞是否合理  

若要「推倒重来」：先备份场景，再执行 `Build Large Varied Map` 得到新底图，然后手工精修。

---

## 8. 碰撞与运行时注意

- `Walls` 层已挂 `TilemapCollider2D`；墙 Tile 资源侧为 Grid 碰撞  
- `Ground` / `Floor` / `Decor` / `Props` 默认无碰撞；需要挡人时：  
  - 把该 Tile 的 Collider 改成 Grid，或  
  - 另加碰撞层 / 手动 Collider  
- 角色移动、相机跟随属于玩法系统，不在本 Tilemap 资源范围内；拼好地图后把角色 Prefab 摆进场景即可  

---

## 9. 常见问题

**Q: Tile Palette 里没有 Kenney_xxx？**  
A: 执行 `Tools → Kenney → Create Tile Palettes Only`。仍没有则执行完整 `Prepare Tilemap Resources For Editors`，并确认已安装 `com.unity.2d.tilemap`。

**Q: 笔画了但场景看不见？**  
A: 是否选错了 Tilemap 层；或 Scene 被别的物体挡住；或画在视野外。选中对应层，按 `F` 聚焦。

**Q: 墙角对不齐 / 描边断开？**  
A: 检查是否混用了不同颜色墙套件；四角是否用了正确 NW/NE/SW/SE；横竖是否用了 H/V。

**Q: 点了 Build Large Map，我改的图没了？**  
A: 该菜单会重建 `KenneySampleMap` 内 Tilemap。创作中的图请另存场景名，或先复制场景再生成。

**Q: 想从 Project 窗口直接拖 Tile？**  
A: 可以。从 `TilemapReady/Tiles/...` 拖到已选中的 Tilemap 上，或拖进 Palette 自定义笔刷盘。正式流程仍建议用官方 Tile Palette。

**Q: Palette 里格子是空的？**  
A: 再跑一次 `Create Tile Palettes Only`。完整 Master 应能刷出约 524 块。

---

## 10. 检查清单（交付一关前）

- [ ] 场景已保存，且是目标 `Map_*.unity` / `MapScene.unity`
- [ ] Ground 无大片空洞；室内有 Floor
- [ ] 外墙闭合，门洞可走；墙色套件统一
- [ ] 家具在 Props，贴花在 Decor，没有画进 Walls 导致挡路异常
- [ ] 已用 PlayScene 回放 JSON，层/墙碰撞看起来对
- [ ] 有格子备注时：类型名对得上 Prefabs，或已接受占位砖仍在

---

## 11. 相关文件速查

| 路径 | 说明 |
|------|------|
| `Assets/Scenes/MapScene.unity` | 主编辑场景与示例地图 |
| `Assets/Scenes/PlayScene.unity` | 读 JSON 验图 |
| `Assets/KenneyShooter/Prefabs/` | 标注 Prefab（类型=文件名） |
| `Assets/Arts/kenney_top-down-shooter/TilemapReady/` | Tile + Palette 成品 |
| `Assets/Arts/kenney_top-down-shooter/PNG/Tiles/` | 原始 PNG 切片 |
| `Assets/Arts/kenney_top-down-shooter/Sample.png` | 官方小场景风格参考 |
| `Assets/Editor/KenneyTilemapResourcePreparer.cs` | 资源准备工具 |
| `Assets/Editor/KenneySampleMapBuilder.cs` | 示例大地图生成工具 |

---

## 12. 一句话总结

**打开 `MapScene` → `Window/2D/Tile Palette` 选 `Kenney_*` → Hierarchy 点对层 → 刷图 → 可选格子备注 → `Ctrl+S` → 导出 JSON → `Play Exported Map` 验图。**  
资源坏了用 `Tools/Kenney/Prepare…`，Palette 空了用 `Create Tile Palettes Only`，要新底图再用 `Build Large Varied Map`（注意覆盖）。

---

## 13. 地编工程交付到游戏（JSON）

关卡定稿后不要交 Scene，只交 JSON。说明见：

**`doc/Kenney_地图JSON交付与本地加载.md`**

快捷菜单：`Tools → Kenney → Map → Export Active Scene Map To JSON`  
本工程验图：`Tools → Kenney → Map → Play Exported Map`（先选 `Export/` 里的 json，再进 `PlayScene`）  
游戏侧：把 JSON 放到 shooting 的 `StreamingAssets/Maps/`，由 `KenneyMapLoader` 加载。
