// Steam Stats Editor 新增代码；遵循仓库 LICENSE.txt 中的 zlib 许可证。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SAM.Batch
{
    public static class BatchText
    {
        public const string FormatVersion = "steam-stats-v1";
        public const int MaximumTextLength = 8 * 1024 * 1024;
        private static readonly Regex IdPattern = new Regex(@"\A[A-Za-z0-9_.:/-]+\z",
            RegexOptions.CultureInvariant);

        public static string Export(uint appId, IEnumerable<StatDescriptor> descriptors)
        {
            if (descriptors == null) throw new ArgumentNullException(nameof(descriptors));
            var text = new StringBuilder();
            text.AppendLine("# Steam Stats Editor：只修改等号右侧数值；删除字段表示保持不变，0 是有效目标。");
            text.AppendLine("# # 后为注释；字段和限制来自本次 Steam 实际读取，导入时重新核验。");
            text.AppendLine("# 受保护、可信服务器和平均速率字段仅供查看，不支持通过清单修改。");
            text.AppendLine("format = " + FormatVersion);
            text.AppendLine("appid = " + appId.ToString(Numeric.Culture));
            text.AppendLine();
            text.AppendLine("[stats]");
            foreach (var stat in descriptors.OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                text.AppendLine();
                text.AppendLine("# " + Comment(stat.DisplayName ?? stat.Id));
                text.Append("# id=").Append(Comment(stat.Id)).Append(" type=").Append(stat.Kind);
                text.Append(" current=").Append(stat.IsAvailable ? Numeric.Format(stat.CurrentValue, stat.Kind) : "unavailable");
                text.Append(" min=").Append(stat.Minimum.ToString("R", Numeric.Culture));
                text.Append(" max=").Append(stat.Maximum.ToString("R", Numeric.Culture));
                text.Append(" maxchange=").Append(stat.MaxChange.ToString("R", Numeric.Culture));
                text.Append(" incrementonly=").Append(stat.IncrementOnly ? "1" : "0");
                text.Append(" protected=").Append(stat.IsProtected ? "1" : "0");
                text.Append(" trustedserver=").Append(stat.SetByTrustedGameServer ? "1" : "0");
                text.Append(" permission=").AppendLine(stat.Permission.ToString(Numeric.Culture));
                if (stat.IsAvailable && Numeric.Finite(stat.CurrentValue) && IsId(stat.Id))
                    text.AppendLine(stat.Id + " = " + Numeric.Format(stat.CurrentValue, stat.Kind));
                else
                    text.AppendLine("# 此字段当前不可读取或 ID 无法在本格式表达，未生成赋值行。");
            }
            return text.ToString();
        }

        public static ParseResult Parse(string text, uint expectedAppId)
        {
            var errors = new List<string>();
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            if (text == null || text.Length > MaximumTextLength)
            {
                errors.Add("清单为空或超过 8 MiB 字符长度限制。");
                return new ParseResult(errors, values, tokens);
            }
            bool hasFormat = false, hasAppId = false, hasStats = false, inStats = false;
            var headers = new HashSet<string>(StringComparer.Ordinal);
            using (var reader = new StringReader(text.TrimStart('\uFEFF')))
            {
                string line;
                int lineNumber = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    int commentAt = line.IndexOf('#');
                    if (commentAt >= 0) line = line.Substring(0, commentAt);
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    string prefix = "第 " + lineNumber.ToString(Numeric.Culture) + " 行：";
                    if (line.StartsWith("[", StringComparison.Ordinal))
                    {
                        if (line != "[stats]") errors.Add(prefix + "未知区段；只支持 [stats]。");
                        else if (hasStats) errors.Add(prefix + "[stats] 区段重复。");
                        else { hasStats = true; inStats = true; }
                        continue;
                    }
                    int equalAt = line.IndexOf('=');
                    if (equalAt <= 0 || equalAt == line.Length - 1 || line.IndexOf('=', equalAt + 1) >= 0)
                    {
                        errors.Add(prefix + "应为 字段名 = 数字，不支持其他文本。");
                        continue;
                    }
                    string id = line.Substring(0, equalAt).Trim();
                    string token = line.Substring(equalAt + 1).Trim();
                    if (!inStats)
                    {
                        if (!headers.Add(id)) errors.Add(prefix + "头部字段重复：" + id);
                        if (id == "format")
                        {
                            hasFormat = true;
                            if (token != FormatVersion) errors.Add(prefix + "不支持的 format，必须为 " + FormatVersion + "。");
                        }
                        else if (id == "appid")
                        {
                            hasAppId = true;
                            if (!uint.TryParse(token, System.Globalization.NumberStyles.None, Numeric.Culture, out uint appId)
                                || appId != expectedAppId)
                                errors.Add(prefix + "appid 与当前游戏不一致，当前为 " + expectedAppId.ToString(Numeric.Culture) + "。");
                        }
                        else errors.Add(prefix + "未知头部字段：" + id);
                        continue;
                    }
                    if (!IsId(id)) { errors.Add(prefix + "字段 ID 格式无效。"); continue; }
                    if (tokens.ContainsKey(id)) { errors.Add(prefix + "字段重复：" + id); continue; }
                    // 即使第一次数值无效，也记录 ID，以识别重复赋值。
                    tokens.Add(id, token);
                    if (!Numeric.TryNumber(token, out double value))
                        errors.Add(prefix + id + " 必须为有限数字，使用小数点，不支持千位分隔符或数字下溢。");
                    else values.Add(id, value);
                }
            }
            if (!hasFormat) errors.Add("缺少 format = " + FormatVersion + "。");
            if (!hasAppId) errors.Add("缺少 appid。");
            if (!hasStats) errors.Add("缺少 [stats] 区段。");
            return new ParseResult(errors, values, tokens);
        }

        private static bool IsId(string id) => !string.IsNullOrEmpty(id) && id.Length <= 512 && IdPattern.IsMatch(id);
        private static string Comment(string value) => (value ?? "").Replace('\r', ' ').Replace('\n', ' ')
            .Replace('\u2028', ' ').Replace('\u2029', ' ');
    }
}
