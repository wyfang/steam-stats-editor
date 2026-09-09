# Steam Stats Editor

基于 Steam Achievement Manager 的 Windows 二次开发项目，用于通过纯文本清单批量编辑游戏统计数据。

[测试构建](https://github.com/wyfang/steam-stats-editor/actions/workflows/windows.yml) · [English](./README.en.md)

## 功能

开发中的第一版已实现：导出实际统计清单、导入校验与差异预览、填入界面目标值、提交一步、自动分步和实时日志。保存回调后重新读取数值；暂时错误有限退避，结果不明时停止。

已通过 70 项逻辑测试、GitHub Windows x86 构建及合成数据的离线界面检查。真实 Steam 读取、提交和实际限速仍待实机验证，当前提供测试包。

## 使用

目标运行环境为 Windows 和 .NET Framework 4.8，需要保持 Steam 客户端运行并登录账户，以及可用的网络连接。

登录 GitHub 后，从成功的 [Windows build](https://github.com/wyfang/steam-stats-editor/actions/workflows/windows.yml) 运行页面下载 `steam-stats-editor-windows` artifact，再解压其中的程序 ZIP。构建产物保留 14 天；测试时无需安装 .NET SDK。

从 `SAM.Picker.exe` 选择游戏，等待读取完成。点击“导出清单”，修改文本中等号右侧的目标数字，再用“导入清单”检查差异并填入界面。导入本身不会写入 Steam；随后选择“提交一步”或“自动分步”。删除某行表示不修改该字段，`0` 是有效目标。

构建需要 Windows、.NET 8 SDK、Visual Studio 2022 Build Tools 的 MSBuild 与 .NET Framework 4.8 targeting pack。在 Developer PowerShell 中执行：

```powershell
./scripts/build-windows.ps1
```

脚本运行逻辑测试、编译 x86 程序、渲染合成数据的导入预览，并在 `artifacts/windows-*/` 生成 ZIP。界面检查不连接 Steam。解压后保留所有依赖文件，与 `SAM.Picker.exe` 放在一起。

## 说明

游戏统计字段以 SAM 实际读取的数据为准。用户的个人清单与账户数据不纳入仓库。Steam 与游戏规则仍然适用；本项目不保证服务器接受修改或数据能够恢复。

受保护、可信服务器和平均速率字段只读。分步间隔默认 120 秒，最低可选 60 秒；Steam 未公开保证通过的最快间隔。停止只阻止后续轮次，不能撤销已发请求或 Steam 缓存改动。运行日志保存在本机 `%LOCALAPPDATA%/SteamStatsEditor/logs/`，不会自动上传。

## 版权说明

本项目基于 [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager)，上游作者为 Rick（Gibbed），起始提交为 `de8b71048a0cee3c3e97cd8535e0f55ca86513e4`。保留上游 [zlib 许可证](./LICENSE.txt)与版权声明；本项目新增原创代码与文档按同一许可证提供。

保留的 [Fugue Icons](https://p.yusukekamiyamane.com/) 由 Yusuke Kamiyamane 创作，适用 [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)。详细归属见 [NOTICE.md](./NOTICE.md)。Steam、Valve、游戏名称和素材属于各自权利人；本项目不是 Valve 官方软件，与 Valve 无隶属或认可关系。
