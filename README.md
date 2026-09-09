# Steam Stats Editor

基于 Steam Achievement Manager（SAM）的 Windows 二次开发工具，增加纯文本清单批量编辑、差异预览和定时分步提交。

[下载](https://github.com/wyfang/steam-stats-editor/releases) · [English](./README.en.md)

## 功能

保留 SAM 的游戏选择、成就和统计编辑功能，并新增：

| 新增功能 | 用途 |
| --- | --- |
| 文本清单导入导出 | 批量修改 `字段=数值`，可交给外部 AI 工具生成或编辑；程序不内置 AI |
| 导入校验与差异预览 | 检查字段、数值类型、范围和权限，确认后一次填入界面 |
| 目标值与读取值分列 | 区分待提交目标和 Steam 已读取的数值 |
| 单步与定时分步提交 | 按字段限制推进目标，提交后读回核验；暂时错误有限退避，结果不明时停止 |
| 实时日志 | 自动保存到程序旁的 `logs/`，便于检查进度和错误 |
| 界面与发行包整理 | 操作分组、增加按钮间距，统一使用 `SteamStatsEditor.exe` 启动 |

## 使用

需要 Windows、.NET Framework 4.8、已运行并登录的 Steam 客户端及网络连接。运行程序无需安装 .NET SDK。

1. 从 [Releases](https://github.com/wyfang/steam-stats-editor/releases) 下载 v1.0.0 的 Windows ZIP，完整解压到可写的新文件夹，运行 `SteamStatsEditor.exe`。保留同级的 `app/` 和 `licenses/` 目录。
2. 选择游戏并等待读取完成。“导出 Steam 已读取值”可保存原值备份；“导出界面目标值”包含当前界面中的待提交数值。
3. 修改清单等号右侧的目标数字，再“导入清单”，检查差异并填入界面。删除某行表示不修改该字段，`0` 是有效目标。
4. 选择“提交一步”或“自动分步”。导入只改变界面，提交才会向 Steam 发送修改。

从源码构建需要 Windows、.NET 8 SDK、Visual Studio 2022 Build Tools 的 MSBuild 和 .NET Framework 4.8 targeting pack。在 Developer PowerShell 中执行：

```powershell
./scripts/build-windows.ps1
```

脚本运行测试并生成 `artifacts/windows-*/steam-stats-editor-windows.zip`；也可使用 [GitHub Actions](https://github.com/wyfang/steam-stats-editor/actions/workflows/windows.yml) 构建。

## 说明

- 字段以 SAM 实际读取的数据为准。受保护、可信服务器和平均速率字段只读；分步提交不能绕过字段权限。
- 自动分步默认间隔 120 秒，最低可选 60 秒。Steam 未公开保证通过的最快间隔；停止只阻止后续轮次，不能撤销已发请求或 Steam 缓存改动。
- 日志保存在 `SteamStatsEditor.exe` 同级的 `logs/`，每次打开游戏编辑窗口新建文件，不会自动删除或上传。关闭程序后可手动清理；目录不可写时会提示失败，可在日志窗口另存。旧版 AppData 日志不会自动迁移或清理。
- 导出的清单与日志保存在本地，个人统计数据不纳入仓库。修改前请保留原值备份；Steam 与游戏规则仍然适用，本项目不保证所有游戏接受修改或数据能够恢复。

## 版权说明

基于 Rick（Gibbed）的 [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) 二次开发，起始提交为 `de8b71048a0cee3c3e97cd8535e0f55ca86513e4`。保留上游 [zlib 许可证](./LICENSE.txt)及版权声明，新增原创代码与文档按同一许可证提供。

保留的 [Fugue Icons](https://p.yusukekamiyamane.com/) 由 Yusuke Kamiyamane 创作，适用 [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)。第三方依赖遵循各自许可，详细归属见 [NOTICE.md](./NOTICE.md)。Steam、Valve、游戏名称和素材属于各自权利人；本项目与 Valve 无隶属或认可关系。
