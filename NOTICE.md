# 来源与许可说明

Steam Stats Editor 是由 wyfang 维护的 Steam Achievement Manager 二次开发项目，须与上游原版软件区分。

## 上游代码

- 项目：[Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager)。
- 作者：Rick（Gibbed）。
- 起始提交：`de8b71048a0cee3c3e97cd8535e0f55ca86513e4`。
- 许可：[LICENSE.txt](./LICENSE.txt) 中的 zlib 许可证，原始版权与许可声明保留。

本项目新增原创代码与文档按同一 zlib 许可证提供。修改版本必须明确标记，不得冒充原作者的软件或移除原有许可声明。

## 图标

上游所包含的 Fugue Icons 保持原有归属：

- 作者：Yusuke Kamiyamane。
- 来源：<https://p.yusukekamiyamane.com/>。
- 许可：[Creative Commons Attribution 3.0 Unported（CC BY 3.0）](https://creativecommons.org/licenses/by/3.0/)。

代码的 zlib 许可证不替代图标的独立许可要求。

## 运行依赖

Windows 构建使用 Microsoft 的 `System.Resources.Extensions` 及其 `System.Memory`、`System.Buffers`、`System.Numerics.Vectors`、`System.Runtime.CompilerServices.Unsafe` 依赖，适用各 NuGet 包中的 MIT 许可及第三方声明。构建脚本将实际解析版本的完整许可与声明汇总到发行包的 `ThirdPartyNotices.txt`；这些依赖不改按本项目的 zlib 许可授权。

## 商标与数据

Steam、Valve、Counter-Strike 及相关商标属于各自权利人。游戏图标、游戏字段定义及用户统计数据不属于本项目原创代码与文档许可的范围。

本项目与 Valve 无隶属、赞助或认可关系。用户提供的个人统计清单不纳入仓库。
