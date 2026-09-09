// Steam Stats Editor 新增代码；遵循仓库 LICENSE.txt 中的 zlib 许可证。
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Batch
{
    public static class BatchValidator
    {
        public static ValidationResult Validate(ParseResult parsed, IEnumerable<StatDescriptor> descriptors)
        {
            if (parsed == null) throw new ArgumentNullException(nameof(parsed));
            if (descriptors == null) throw new ArgumentNullException(nameof(descriptors));
            var errors = new List<string>(parsed.Errors);
            var targets = new List<StatTarget>();
            if (!parsed.Success) return new ValidationResult(errors, targets);
            var schema = new Dictionary<string, StatDescriptor>(StringComparer.Ordinal);
            foreach (var descriptor in descriptors)
            {
                if (descriptor == null || string.IsNullOrEmpty(descriptor.Id))
                    errors.Add("当前 Steam 字段定义缺少 ID。");
                else if (schema.ContainsKey(descriptor.Id))
                    errors.Add("当前 Steam 字段定义有重复 ID：" + descriptor.Id);
                else schema.Add(descriptor.Id, descriptor);
            }
            foreach (var pair in parsed.Targets)
            {
                if (!schema.TryGetValue(pair.Key, out var descriptor))
                {
                    errors.Add("未知字段，不在当前 Steam 实际清单中：" + pair.Key);
                    continue;
                }
                double target = pair.Value;
                if (descriptor.Kind == StatValueKind.Integer)
                {
                    if (!Numeric.TryInteger(parsed.Tokens[pair.Key], out int integer))
                    {
                        errors.Add(pair.Key + "：目标必须是精确的 32 位整数，不能含非零小数或溢出。");
                        continue;
                    }
                    target = integer;
                }
                string error = StepPlanner.CheckTarget(descriptor, target, out double normalized);
                if (error != null) { errors.Add(error); continue; }
                if (descriptor.Kind != StatValueKind.Integer
                    && !Numeric.IsFloatToken(parsed.Tokens[pair.Key], normalized))
                {
                    errors.Add(Numeric.FloatPrecisionError(pair.Key, normalized));
                    continue;
                }
                if (!StepPlanner.TryEstimate(descriptor, normalized, out long rounds, out error))
                {
                    errors.Add(error);
                    continue;
                }
                targets.Add(new StatTarget(descriptor, normalized, rounds));
            }
            return new ValidationResult(errors, targets);
        }

        // UI 中已经是 int/float 的目标可直接验证，无需先生成临时文本。
        public static ValidationResult ValidateTargets(IEnumerable<StatDescriptor> descriptors,
            IDictionary<string, double> targets)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            var values = new Dictionary<string, double>(targets, StringComparer.Ordinal);
            var tokens = targets.ToDictionary(pair => pair.Key,
                pair => pair.Value.ToString("R", Numeric.Culture), StringComparer.Ordinal);
            return Validate(new ParseResult(new List<string>(), values, tokens), descriptors);
        }
    }
}
