# 莉莉娅中文修改器 (LilliaTrainer)

《見習いサキュバスリリア〜人間界搾精日誌〜》(DLsite RJ01724797) 的实时内存修改器。
单文件 C# WinForms 程序，零第三方依赖，使用 .NET Framework 4.0 自带的 csc 即可编译。

> 仅供个人学习与游戏体验调整使用。工具只读写运行中游戏的内存，不修改、不分发任何游戏文件。

## 功能

- **概览仪表盘**：金币 / 精液储量 / 天数 / 等级 / 地图耐力 / 背包 / 任务 / 服装解锁实时总览，含当前穿搭与发型收藏
- **资源与锁定**：金币、精液储量、日期、难度、体型、最大耐力修改；资源锁 / 全耐力锁 / 无限天数三项 0.5s 周期持续锁定
- **背包**：查看全部容器（背包 / 秘密口袋 / 仓库 / 发型槽 / 已装备槽 / 商店）既有物品，修改数量、补满消耗品、跨容器移动
- **任务**：查看与写满既有任务进度（奖励仍需回游戏提交）
- **服装图鉴**：36 系列服装目录（提取自游戏资源目录，共 421 个服装部件键），自动对比当前存档拥有状态并列出未解锁清单
- **进阶数据**：成就 / 通关计数 / 7 个位掩码 / 无限游戏 / 兔子警察解锁 / 兴奋度 / 7 个统计数组（按字段 + 下标读写）
- **安全设施**：GameAssembly.dll SHA256 版本校验、存档身份指纹（PID + 进程启动时间 + 对象地址）、写入回读校验、会话级撤销、一键存档备份

![概览](screenshots/preview-overview.png)

## 构建源码与运行

```
git clone https://github.com/sakuradairong/lillia-trainer.git
cd lillia-trainer
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -codepage:65001 ^
  -target:winexe -platform:x64 -optimize+ -out:LilliaTrainer.exe ^
  -reference:System.dll -reference:System.Windows.Forms.dll ^
  -reference:System.Drawing.dll ChineseTrainer.cs
```

把 `LilliaTrainer.exe` 放到游戏根目录（与 `NoviceSuccubusLillia.exe` 同级）运行：
先启动游戏并载入存档，再运行修改器，顶部显示「已连接 · 存档已载入」后各项功能可用。
修改只写入内存，需要在游戏内手动保存才会保留。

## 使用说明

完整说明见 [docs/使用说明.txt](docs/使用说明.txt)，含当前存档未解锁服装参考清单。

## 技术要点

- **IL2CPP 静态链定位**：不扫描特征码，从 `GameAssembly.dll+0x2FD1D58` 的方法指针出发，经 `method→klass→rgctx→单例klass→静态字段` 五级指针拿到 `DataHolder` 单例，再校验类名字符串（ASCII C-String）防误绑
- **服装目录提取**：解码游戏 Addressables `catalog.json` 的 `m_KeyDataString`（Base64，自定义 类型字节+int32长度 键编码），正则提取全部 `Image_<系列>_<配色>_<部位>` 缩略图键，生成内置 36 系列图鉴；拥有状态按背包物品名关键词匹配（商店柜台不计入）
- **写入安全边界**：只写既有基元字段与既有数组元素，绝不分配托管对象、不改写指针引用；所有写操作带回读校验并进入撤销缓冲；存档/进程身份变化时自动停锁、清空撤销、拒绝过期写入
- **三种诊断模式**：`--probe`（打印一行 JSON 存档摘要）、`--selftest`（进程内合成内存自测 67 项，绝不接触真实游戏）、`--preview`（渲染 6 页截图 + UI 文字溢出 lint）
- **Explore.cs**：配套的只读内存探索器，用于 dump 对象布局、集合元素、字典条目

## 仓库结构

```
ChineseTrainer.cs     修改器全部源码（单文件）
Explore.cs            只读内存探索器
docs/使用说明.txt      详细使用说明
screenshots/          界面截图（预览模式生成，含模拟数据）
```

## 限制

- 仅支持 `GameAssembly.dll` SHA256 为 `F2D1139B…FD86D0` 的游戏版本，版本不符会安全停止
- 不提供无敌、免警察等战斗类效果；不修改存档文件内容；不提供离线存档破解
- 地图掉落表在游戏运行时按需加载，修改器不枚举具体掉落点

## License

MIT
