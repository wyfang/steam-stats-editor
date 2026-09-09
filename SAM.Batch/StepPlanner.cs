// Steam Stats Editor 新增代码；遵循仓库 LICENSE.txt 中的 zlib 许可证。
using System;

namespace SAM.Batch
{
    public static class StepPlanner
    {
        // 必须传入最近一次 Steam 读回的 CurrentValue，不能把待提交值当成确认值。
        public static StepResult Next(StatDescriptor descriptor, double target)
        {
            string error = CheckTarget(descriptor, target, out double normalized);
            if (error != null) return new StepResult(descriptor?.CurrentValue ?? 0, false, error);
            var step = NextValue(descriptor.Kind, descriptor.CurrentValue, normalized, descriptor.MaxChange);
            if (step.Success && step.Value != descriptor.CurrentValue
                && (step.Value < descriptor.Minimum || step.Value > descriptor.Maximum))
                return new StepResult(descriptor.CurrentValue, false,
                    descriptor.Id + "：当前值已在 min/max 范围外，下一步仍超出边界，无法形成合法分步计划。");
            return step;
        }

        internal static string CheckTarget(StatDescriptor descriptor, double target, out double normalized)
        {
            normalized = target;
            if (descriptor == null) return "缺少字段定义。";
            string id = descriptor.Id + "：";
            if (!descriptor.IsAvailable) return id + "当前无法读取，不能设置目标。";
            if (!Numeric.Finite(target) || !Numeric.Finite(descriptor.CurrentValue)) return id + "数值必须有限。";
            if (descriptor.Kind == StatValueKind.Integer)
            {
                if (target < int.MinValue || target > int.MaxValue || target != Math.Truncate(target))
                    return id + "目标必须是 32 位整数。";
                if (descriptor.CurrentValue < int.MinValue || descriptor.CurrentValue > int.MaxValue
                    || descriptor.CurrentValue != Math.Truncate(descriptor.CurrentValue))
                    return id + "当前值不是有效的 32 位整数。";
            }
            else if (descriptor.Kind == StatValueKind.Float || descriptor.Kind == StatValueKind.AverageRate)
            {
                normalized = (float)target;
                if (!Numeric.Finite(normalized) || (normalized == 0 && target != 0))
                    return id + "目标超出 float32 范围或下溢为零。";
                if (!Numeric.IsFloatToken(target.ToString("R", Numeric.Culture), normalized))
                    return Numeric.FloatPrecisionError(descriptor.Id, normalized);
                if (descriptor.CurrentValue != (double)(float)descriptor.CurrentValue)
                    return id + "当前值不是 Steam 实际使用的 float32 数值。";
            }
            else return id + "不支持的字段类型。";

            // 不改变数值时允许只读字段原样回导，即使当前值已不符合最新边界。
            if (normalized == descriptor.CurrentValue) return null;
            if (descriptor.IsProtected || descriptor.Permission != 0 || descriptor.SetByTrustedGameServer)
                return id + "字段受保护或由可信服务器管理，不能批量修改。";
            if (descriptor.Kind == StatValueKind.AverageRate)
                return id + "平均速率字段需要累计量和时间，不能作为普通数值赋值。";
            if (!Numeric.Finite(descriptor.Minimum) || !Numeric.Finite(descriptor.Maximum)
                || descriptor.Minimum > descriptor.Maximum || !Numeric.Finite(descriptor.MaxChange)
                || descriptor.MaxChange < 0)
                return id + "字段限制无效，无法安全规划。";
            if (normalized < descriptor.Minimum || normalized > descriptor.Maximum)
                return id + "目标超出实际字段的 min/max 范围。";
            if (descriptor.IncrementOnly && normalized < descriptor.CurrentValue)
                return id + "字段只允许增加。";
            return null;
        }

        private static StepResult NextValue(StatValueKind kind, double current, double target, double maxChange)
        {
            if (current == target) return new StepResult(current, true);
            double distance = Math.Abs(target - current);
            if (maxChange == 0 || distance <= maxChange) return new StepResult(target, true);
            int direction = target > current ? 1 : -1;
            double amount = kind == StatValueKind.Integer ? Math.Floor(maxChange) : maxChange;
            double next = current + direction * Math.Min(distance, amount);
            if (kind != StatValueKind.Integer)
            {
                next = (float)next;
                // float32 四舍五入可能超过 maxchange；向当前值退一个可表示值。
                if (Math.Abs(next - current) > maxChange)
                    next = Numeric.Adjacent(next, -direction);
            }
            if (!Numeric.Finite(next) || next == current || (next - current) * direction <= 0
                || Math.Abs(next - current) > maxChange || (target - next) * direction < 0)
                return new StepResult(current, false, "maxchange 小于当前精度允许的步幅，目标无法通过此字段类型的分步提交达到。");
            return new StepResult(next, next == target);
        }

        internal static bool TryEstimate(StatDescriptor descriptor, double target, out long rounds, out string error)
        {
            rounds = 0;
            error = null;
            double current = descriptor.CurrentValue;
            if (current == target) return true;
            var first = Next(descriptor, target);
            if (!first.Success) { error = first.Error; return false; }
            if (descriptor.MaxChange == 0) { rounds = 1; return true; }
            if (descriptor.Kind == StatValueKind.Integer)
            {
                double step = Math.Floor(descriptor.MaxChange);
                if (step < 1) { error = descriptor.Id + "：maxchange 小于整数最小步幅 1，目标不可达。"; return false; }
                rounds = (long)Math.Ceiling(Math.Abs(target - current) / step);
                return true;
            }

            // 在同一二进制指数区间，float32 间距固定，合法步幅也固定。
            // 一次跳过区间中的等距步骤，最多经过正负两侧的 512 个指数区间；
            // 不逐条循环数十亿次，也不使用忽略 float 舍入的 distance/maxchange 估算。
            for (int segment = 0; segment < 2048; segment++)
            {
                var step = NextValue(descriptor.Kind, current, target, descriptor.MaxChange);
                if (!step.Success) { error = descriptor.Id + "：" + step.Error; return false; }
                if (step.Complete) { rounds = checked(rounds + 1); return true; }
                long count = 1;
                double next = step.Value;
                uint currentBits = Numeric.Bits((float)current);
                uint nextBits = Numeric.Bits((float)next);
                if ((currentBits & 0xFF800000u) == (nextBits & 0xFF800000u))
                {
                    uint exponent = currentBits & 0x7F800000u;
                    double low = Numeric.Float(exponent);
                    double high = Numeric.Float(exponent | 0x007FFFFFu);
                    if ((currentBits & 0x80000000u) != 0)
                    {
                        double oldLow = low;
                        low = -high;
                        high = -oldLow;
                    }
                    double boundary = target > current ? Math.Min(high, target) : Math.Max(low, target);
                    double delta = next - current;
                    double repetitions = Math.Floor(Math.Abs((boundary - current) / delta));
                    if (repetitions >= 1)
                    {
                        count = (long)repetitions;
                        next = (float)(current + delta * count);
                        if (next == target) { rounds = checked(rounds + count); return true; }
                    }
                }
                rounds = checked(rounds + count);
                current = next;
            }
            error = descriptor.Id + "：分步计划超过浮点区间计算上限，无法确认目标可达。";
            return false;
        }
    }
}
