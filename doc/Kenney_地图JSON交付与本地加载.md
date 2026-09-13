# Kenney 地图交付与本地加载约定

面向：本工程 **MapBuilder** 导出 ↔ 本工程 PlayScene 验图 ↔ 游戏工程 `shooting` 本地加载。

**已定决策**

- 地图：**本地读 JSON**（`StreamingAssets/Maps/`），不从服务器拉图。
- 交付物：**只交 `.map.json`**，不附缩略图。

---

## 1. 工程分工

| 工程 | 职责 |
|------|------|
| **MapBuilder（本工程）** | Tile Palette 拼图、格子备注、导出 JSON；`PlayScene` 回放验图 |
| 游戏工程 `shooting` | `KenneyMapLoader` 读同一份 JSON → 重建 Tilemap；玩法 / Fusion |

两边共用同一套瓦片包：`Assets/Arts/kenney_top-down-shooter/TilemapReady/`（瓦片 ID 为 Kenney 数字，如 `tile_109` → `109`）。  
标注 Prefab 目录两边都是 `Assets/KenneyShooter/Prefabs/`（类型名 = 文件名）。

---

## 2. 文件位置

| 路径 | 说明 |
|------|------|
| `Assets/StreamingAssets/Maps/*.map.json` | 导出镜像，PlayScene 运行时读这里 |
| `Export/*.map.json` | 交给 shooting 的出口 |
| `Assets/KenneyShooter/Config/KenneyTileCatalog.asset` | id → Tile |
| `Assets/KenneyShooter/Config/KenneyPlaceableCatalog.asset` | 类型 → Prefab |
| `Assets/KenneyShooter/Prefabs/` | 地编参考 + 加载替换 |
| `Assets/KenneyShooter/Scripts/Map/` | 数据契约 + Loader |
| `Assets/Scenes/PlayScene.unity` | 空场景，只用来读 JSON 验图 |

默认示例：`sample_large.map.json`（`KenneyMapFormat.DefaultMapFile`）。

---

## 3. JSON 契约（formatVersion = 2）

```json
{
  "formatVersion": 2,
  "name": "sample_large",
  "width": 60,
  "height": 40,
  "cellSize": 1,
  "layers": [
    {
      "name": "Ground",
      "sortingOrder": 0,
      "hasCollider": false,
      "tiles": [ { "x": 0, "y": 0, "id": 401 } ]
    },
    {
      "name": "Walls",
      "sortingOrder": 2,
      "hasCollider": true,
      "tiles": [ { "x": 5, "y": 11, "id": 109 } ]
    }
  ],
  "cellNotes": [
    {
      "layer": "Props",
      "x": 30,
      "y": 22,
      "width": 3,
      "height": 1,
      "opening": 90,
      "rotation": 90,
      "type": "Sofa",
      "description": "占位砖运行时换成 Prefab"
    }
  ],
  "meta": {
    "spawnPoints": [ { "x": 10.5, "y": 4.5, "tag": "player" } ]
  }
}
```

固定层名（顺序）：`Ground` / `Floor` / `Walls` / `Decor` / `Props`。  
`Walls` 加载时会确保有 `TilemapCollider2D`。

`cellNotes` 可空。锚点 `(x, y)` 为占地左下格，`width`/`height` 为地图列×行。`opening` 是摆好后的开口（0上/90左/180下/270右）。`rotation` = 世界开口 − Prefab 原画开口。`type` 对应 Prefabs 目录文件名。加载时清占地砖并在几何中心生成 Prefab。旧的 formatVersion = 1 仍可加载。

---

## 4. 本工程菜单（Tools → Kenney → Map）

| 菜单 | 作用 |
|------|------|
| **Rebuild Tile Catalog** | 扫描 TilemapReady，生成/更新 `KenneyTileCatalog` |
| **Rebuild Placeable Catalog** | 扫描 Prefabs，生成/更新 `KenneyPlaceableCatalog` |
| **Export Active Scene Map To JSON** | 把当前场景 `KenneySampleMap` 导出为 JSON（含备注） |
| **Export MapScene Sample (StreamingAssets)** | 打开 MapScene 并导出 `sample_large.map.json` |
| **Cell Notes** | 给 Select 选中的格子写占地 / 开口 / 类型 / 描述 |
| **Play Exported Map** | 打开 Export/ 选 json → 镜像到 StreamingAssets → PlayScene Play |

---

## 5. 本工程验图

1. 在 `MapScene` 导出到 `Export/`（会镜像到 `StreamingAssets/Maps/`）
2. `Tools → Kenney → Map → Play Exported Map`
3. 在弹出的 **Export/** 目录里选一个 `.json`，确认后进 `PlayScene` Play
4. `KenneyMapLoader` 从 `StreamingAssets/Maps/` 读所选文件，刷到新建的 `KenneySampleMap`
5. 有 `cellNotes` 时按类型查 Prefab，清砖后生成；缺 Prefab 则 Warning 并保留占位砖
6. PlayScene **没有**烘焙地图；JSON 缺失会直接失败（`keepBakedMapIfMissing = false`）

不要在 MapScene 点 Play 来验导出文件。

---

## 6. 交给 shooting

1. 把 `Export/你的关卡.map.json` 复制到 `shooting/Assets/StreamingAssets/Maps/`
2. 游戏场景 `KenneyMapLoader` 的 `mapFileName` 配成该文件
3. Prefabs / PlaceableCatalog 两边保持同一套文件名

缺砖时两边都要先 **Rebuild Tile Catalog**。

---

## 7. 隔离关系

- MapBuilder **不要**塞 Photon Fusion / 对局玩法。
- shooting 不依赖本工程；只认 JSON + TileCatalog + Prefabs。
- 瓦片包与 Prefabs：定期复制或共用目录，两边 ID / 文件名必须一致。
