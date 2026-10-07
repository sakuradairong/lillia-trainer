# 莉莉娅中文修改器 (LilliaTrainer) v2.1.0

《見習いサキュバスリリア〜人間界搾精日誌〜》(DLsite RJ01724797) 的实时内存修改器。
WinForms / .NET Framework 4.0 程序，无第三方运行时依赖，换装组件以原生 DLL 内嵌在 exe 中。

> 仅供个人学习与游戏体验调整使用。工具只读写运行中游戏的内存，不修改、不分发任何游戏文件。

## 功能

- **概览仪表盘**：金币 / 精液储量 / 天数 / 等级 / 地图耐力 / 背包 / 任务 / 服装解锁实时总览，含当前穿搭与发型收藏
- **资源与锁定**：金币、精液储量、日期、难度、体型、最大耐力修改；资源锁 / 全耐力锁 / 无限天数三项 0.5s 周期持续锁定
- **背包**：查看全部容器（背包 / 秘密口袋 / 仓库 / 发型槽 / 已装备槽 / 商店）既有物品，修改数量、补满消耗品；跨容器移动目前禁用
- **仓库扩容**：支持仓库 / 服装、消耗品仓库、杂物仓库三类仓库；每边最多 256、总容量最多 4096 格，只允许扩大并保留既有物品坐标。目标布局按物品实际占位校验，例如损坏的 10x80 服装网格至少需要 10x133 才能扩到 10x160。扩容后需在游戏内保存并重新载入存档，网格才会按新尺寸重建
- **一键替换套装**：在游戏基地换装界面打开后，于「服装图鉴」选择系列与配色执行替换；通过 Unity 主线程接口调用，内嵌 bridge DLL 仅在执行动作时加载。只替换本存档已有的可穿戴部件，不解锁未拥有服装，冲突部件换下后返回仓库
- **任务**：查看与写满既有任务进度（奖励仍需回游戏提交）
- **服装图鉴**：36 系列服装目录（提取自游戏资源目录，共 421 个服装部件键），自动对比当前存档拥有状态并列出未解锁清单
- **进阶数据**：成就 / 通关计数 / 7 个位掩码 / 无限游戏 / 兔子警察解锁 / 兴奋度 / 7 个统计数组（按字段 + 下标读写）
- **安全设施**：GameAssembly.dll SHA256 版本校验、存档身份指纹（PID + 进程启动时间 + 对象地址）、写入回读校验、会话级撤销、一键存档备份

![概览](screenshots/preview-overview.png)

## 构建

仓库自带 `build.ps1`，在 Windows 上运行：

```
pwsh -NoProfile -File build.ps1 -Check
```

默认输出 `dist\莉莉娅中文修改器.exe`（可用 `-OutFile` 覆盖）。依赖：

- MSVC x64 C++ 工具（`cl.exe`，含 x64 目标库）
- Windows 10/11 SDK
- .NET Framework 4.0 csc（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）
- `build.ps1` 会自动探测 VS 18 Insiders 或 VS 2022 BuildTools 的安装根目录，并使用其中 MSVC x64 C++ 工具与 Windows SDK

脚本先编译原生换装组件 `OutfitBridge.cpp`（`OutfitCore.h`），再用 csc 编译 `ChineseTrainer.cs`、`OutfitFeature.cs`、`WarehouseFeature.cs`、`WarehouseCapacity.cs`，并把 bridge DLL 内嵌进 exe。`-Check` 另外编译运行 `OutfitCoreTests.cpp` 离线布局测试和修改器 `--selftest`。中间产物写入忽略的 `temp/`。

## 运行

把 `dist\莉莉娅中文修改器.exe` 放到游戏根目录（与 `NoviceSuccubusLillia.exe` 同级）运行。先启动游戏并载入存档，再运行修改器；顶部显示「已连接 · 存档已载入」后各项功能可用。
修改只写入内存，需要在游戏内手动保存才会保留。

## 使用说明

完整说明见 [docs/使用说明.txt](docs/使用说明.txt)。

## 技术要点

- **IL2CPP 静态链定位**：不扫描特征码，从 `GameAssembly.dll+0x2FD1D58` 的方法指针出发，经 `method→klass→rgctx→单例klass→静态字段` 五级指针拿到 `DataHolder` 单例，再校验类名字符串（ASCII C-String）防误绑
- **服装目录提取**：解码游戏 Addressables `catalog.json` 的 `m_KeyDataString`（Base64，自定义 类型字节+int32长度 键编码），正则提取全部 `Image_<系列>_<配色>_<部位>` 缩略图键，生成内置 36 系列图鉴；拥有状态按背包物品名关键词匹配（商店柜台不计入）
- **一键换装**：由 `OutfitFeature.cs` 走 Unity 主线程接口调用原生 bridge，按槽位与冲突规则只替换既有可穿戴部件
- **仓库目标布局校验**：由 `WarehouseCapacity.cs` 依据现有物品占位计算最小可容纳尺寸，只允许扩大
- **普通编辑的安全边界**：对既有基元字段与既有数组元素的标量编辑做读取回读校验，并在存档/进程身份变化时自动停锁、清空撤销、拒绝过期写入。会话级撤销只覆盖下方「撤销范围」列出的当前存档标量字段，不含数组、背包、任务、仓库与套装。一键换装由 `OutfitFeature.cs` 调用游戏自身的 Unity 主线程接口完成
- **撤销范围**：仅覆盖当前存档的标量字段（金币、资源、日期、难度、体型、最大耐力、若干掩码/计数等）。背包物品、任务进度、数组，以及仓库扩容与套装替换都不进入通用撤销，请依赖游戏内存档或修改器备份
- **三种诊断模式**：`--probe`（打印一行 JSON 存档摘要）、`--selftest`（进程内合成内存自测，不接触真实游戏）、`--preview`（渲染界面截图 + UI 文字溢出 lint，截图为模拟数据并已标注）
- **Explore.cs**：配套的只读内存探索器，用于 dump 对象布局、集合元素、字典条目

## 仓库结构

```
ChineseTrainer.cs          修改器主程序与 UI
OutfitFeature.cs           服装图鉴与一键替换套装
OutfitBridge.cpp           原生换装 bridge（编译为内嵌 DLL）
OutfitCore.h               换装槽位与冲突处理核心
OutfitCoreTests.cpp        离线换装/扩容布局测试
WarehouseFeature.cs        仓库扩容 UI 与逻辑
WarehouseCapacity.cs       仓库容量与目标布局校验
Explore.cs                 只读内存探索器
build.ps1                  构建脚本
docs/使用说明.txt           详细使用说明
screenshots/               界面截图（预览模式生成，含模拟数据）
```

## 限制

- 仅支持 `GameAssembly.dll` SHA256 为 `F2D1139BA6B833CF89044896F34BF76F3897E985623A5563A5F6BF03C7FD86D0` 的完整游戏版本，版本不符会安全停止
- 跨容器移动暂不可用；仓库扩容与套装替换不在通用撤销范围内
- 不提供无敌、免警察等战斗类效果；不修改存档文件内容；不提供离线存档破解
- 界面截图来自 `--preview` 模拟数据；游戏内换装后的外观、保存及重新载入效果尚未完成验收

## License

MIT
