# Survivalcraft 语音提示系统（Voice Prompt Core + 语音包）

给生存战争插件版（[SurvivalcraftApi](https://atomgit.com/SC-SPM/SurvivalcraftApi/)）做的游戏提示配音系统：当游戏弹出提示——"你饿了""好冷啊""弩已装填"……——自动播放当前选中语音包里的对应语音。

采用**核心 + 语音包**架构：本仓库的核心模组包含全部逻辑，语音由独立的纯资源包提供。任何人都可以不写一行代码制作自己的语音包（角色音、VTuber 音、方言音……），多个语音包可共存、随时切换。

## 功能特性

- 覆盖 **150+ 提示键位**（饥饿/体力/睡眠/体温/感冒/装填/挖矿/开场剧情/跨年烟花等，完整清单见[对照表](docs/语音包-音频文件对照表.md)）
- 文本 → 语言键位**反查**匹配：游戏切任何语言都能命中，与文本翻译解耦
- 拦截小字提示与画面浮字两个通道；多人游戏只对本地玩家播放，不打扰别人
- **缺失语音静默跳过**——语音包配几条就能用几条，不需要配齐
- 可调开关、音量倍率、每句冷却（默认 5 秒），外加 1 秒全局间隔防刷屏
- 多语音包共存：核心按包名前缀自动发现，在模组设置里选择（全局生效）
- 语音包可用根目录的 `ExtraPrompts.json` 声明第三方模组提示的映射，为其他模组的提示配音
- 界面多语言（内置 简体中文 / English，欢迎补充其他语言）
- 兼容插件版 API **1.9.3 及以上**（Harmony 运行时绑定，不依赖新版钩子）

## 仓库内容

```
├── VoicePromptCore.csproj          构建工程（dotnet build 一键出包）
├── VoicePromptCoreLoader.cs        核心：提示拦截/文本反查/冷却/语音包发现与激活
├── VoicePackSelectionWidget.cs     设置页「语音包」选择控件
├── modinfo.json                    模组信息与设置声明
├── Assets/Lang/                    界面多语言（en-US 兜底 + zh-CN）
└── docs/
    ├── 语音包制作指南.md           ← 想做自己的语音包看这个
    ├── 语音包-音频文件对照表.md     全部 150 键的文件名/台词/触发场景/优先级
    └── 语音包-游戏提示文本清单.md   各提示通道与触发条件总览
```

本仓库只包含**核心模组**；语音包是独立的纯资源模组，单独制作与分发（官方示例包 Neuro 另行发布，做法见[制作指南](docs/语音包制作指南.md)）。

## 安装使用（玩家）

1. 从发布页下载两个文件：`VoicePromptCore.scmod`（核心）和任意语音包如 `NeuroVoicePack.scmod`。
2. 一起放进游戏的 `Mods` 目录（Windows 开发版在 `Survivalcraft.Windows\bin\Debug\Mods`；正式版在游戏目录的 `Mods`）。
3. 进游戏即可。装了多个语音包时，在 **模组管理 → Voice Prompt Core 设置 → 语音包** 里选择用哪个（全局生效，所有存档共用）。

> 若装过早期一体版（PackageName `shuimo.neurovoicepack`），请先删掉旧的 `.scmod`，否则会双声。

### 设置项

| 设置 | 默认 | 说明 |
|---|---|---|
| 启用语音 | 开 | 总开关，关闭后只显示文字 |
| 语音包 | 自动 | 当前使用的音频包；自动 = 取 LoadOrder 最大的已安装包 |
| 语音音量 | 1.0 | 相对游戏音效音量的倍率（0–2） |
| 同一句冷却秒数 | 5 | 同一句语音重复播放的最小间隔 |

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```bash
dotnet build    # 产物 bin/Debug/VoicePromptCore.scmod
```

NuGet 源（nuget.org + SurvivalcraftAPI 官方源）已在 `nuget.config` 里配好；包缓存固定在仓库**同级**目录（`nuget.config` 的 `global-packages-folder`），不写入用户主目录，也不会进仓库。

## 自动构建与发布（GitHub Actions）

| 工作流 | 触发 | 作用 |
|---|---|---|
| `ci.yml` | push 到 main / PR / 手动 | Release 构建检查，并上传 `.scmod` 构建产物 |
| `release.yml` | 推送 `v*` 标签 | 自动把 `modinfo.json` 的 `Version` 同步为标签号 → Release 构建 → 创建 GitHub Release（附 `VoicePromptCore.scmod` 与 sha256） |

发新版本只需要：

```bash
git tag v1.0.1
git push origin v1.0.1
```

Release 页面几分钟后就会出现带 `.scmod` 附件的发行版。

## 制作自己的语音包

不需要编程：准备音频 + 一个 modinfo.json，打包成 `.scmod` 即可。核心会自动发现并列入设置。

👉 完整教程：**[docs/语音包制作指南.md](docs/语音包制作指南.md)**

## 翻译贡献

核心界面文案位于 `Assets/Lang/`（`en-US.json` 为英文兜底，`zh-CN.json` 为简体中文）。复制 `en-US.json` 为对应语言文件并翻译即可，游戏支持：en-US / zh-CN / ru-RU / es-419 / pt-BR / ro-RO / tr-TR / vi-VN。

## 已知限制

- 覆盖**小字提示**、**画面浮字**与**大字横幅**（开场剧情、跨年烟花）三个通道；死亡原因记录不配音（不弹提示）
- 多人游戏中，语音只对本地玩家播放（远程玩家的提示不会触发你的语音）
- 同一提示在冷却期内只播一次；1 秒内最多播一句
- 语音包必须把音频放在**自己包 id 的子目录**（`Assets/Audio/Voice/<包id>/`），否则多包会互相覆盖

## 许可证

- 代码：待定（正式开源前请补充，例如 MIT）
- 制作你自己的语音包时，请使用你有权使用的音频。

## 致谢

- [SC-SPM/SurvivalcraftApi](https://atomgit.com/SC-SPM/SurvivalcraftApi/) —— 插件版 API 与模组系统
