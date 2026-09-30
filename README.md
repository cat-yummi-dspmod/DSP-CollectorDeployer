# Collector Deployer | 轨道采集器一键部署

在气态巨星上，"手动采集"按钮左侧会出现"一键部署轨道采集器"按钮。

On gas giants, a "Deploy Orbital Collectors" button appears to the left of the manual collecting button.

## 功能 Features

- 点一下直接部署，无需确认：
  - 在赤道上按格点均匀建造 40 个轨道采集器（气巨赤道 400 个格点，每 10 格一个），立即完工
  - 已在格点上的原样保留，不在格点上的拆除重建（采集器及其储存物品返还背包）
- 轨道采集器不足时提示"轨道采集器不足"，不做任何改动
- 星球上已有超过 40 个轨道采集器（例如其他 mod 放宽了限制）时不部署
- 只有背包放不下拆除返还的物品时才会弹窗，列出会掉落的物品并询问是否继续
- 中文界面显示中文，其他语言显示英文

- One click, no confirmation:
  - Builds 40 collectors evenly on the equator grid (400 grid points on a gas giant equator, one every 10), completed instantly
  - Collectors already on the grid are kept; off-grid ones are removed and rebuilt (they and their stored items go back to your inventory)
- Shows "Not enough orbital collectors" and changes nothing if you don't have enough
- Does nothing if the planet already has more than 40 collectors (e.g. another mod lifted the limit)
- Only asks for confirmation when your inventory can't hold what the removal returns, listing the items that would drop
- Chinese UI shows Chinese text, all other languages show English

## 说明 Notes

建造使用官方流程：`PlanetFactory.AddPrebuildDataWithComponents` + `BuildFinally`；拆除使用 `PlayerAction_Build.DoDismantleObject`。
间距与赤道限制和手动放置规则一致。

Building uses the game's own `AddPrebuildDataWithComponents` + `BuildFinally`; removal uses `DoDismantleObject`.
