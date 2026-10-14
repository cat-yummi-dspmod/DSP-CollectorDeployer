# Changelog

## 1.2.0

- **YummiHarness**: `Harness.PatchAll` + `[Guard]` on patches; Thunderstore dependency `cat_yummi-YummiHarness`.

## 1.1.1

- Fix false **“planet cannot power orbital collectors”** when playing with **Genesis Book** (and similar overhauls that zero `gasTotalHeat` / collector `workEnergyPerTick`).
- Deploy eligibility now matches in-game build rules for gas giants instead of the vanilla gas-heat formula only.

## 1.1.0

- Optional **whole-galaxy deploy** button (`EnableGalaxyDeploy` in config, default off).
- `manifest.json`: add `website_url`.

## 1.0.0

- One-click deploy **40** orbital collectors on a gas giant equator grid; keep on-grid collectors, rebuild off-grid ones.
- Dependency: BepInEx 5.4.17.

---

# 更新日志

## 1.2.0

- **YummiHarness**：使用 `Harness.PatchAll` 与 `[Guard]` 指纹校验；Thunderstore 依赖 `cat_yummi-YummiHarness`。

## 1.1.1

- 修复与 **创世之书** 等同玩法 mod 联用时误报 **「该星球的资源不足以驱动轨道采集器」**（创世之书将 `gasTotalHeat` 置 0、采集器不消耗气体发热等）。
- 可否部署的判定与游戏内（及创世之书 patch 后）的手动放置规则一致，不再仅依赖原版发热公式。

## 1.1.0

- 可选 **全银河一键部署**（配置项 `EnableGalaxyDeploy`，默认关闭）。
- `manifest.json` 增加 `website_url`。

## 1.0.0

- 气态巨星赤道上 **一键部署 40 个**轨道采集器；保留格点上的采集器，拆除并重建格点外的。
- 依赖 BepInEx 5.4.17。
