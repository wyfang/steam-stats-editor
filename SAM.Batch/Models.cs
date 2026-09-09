// Steam Stats Editor 新增代码；遵循仓库 LICENSE.txt 中的 zlib 许可证。
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SAM.Batch
{
    public enum StatValueKind { Integer, Float, AverageRate }

    public sealed class StatDescriptor
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public StatValueKind Kind { get; set; }
        public double CurrentValue { get; set; }
        public double Minimum { get; set; } = double.MinValue;
        public double Maximum { get; set; } = double.MaxValue;
        public double MaxChange { get; set; }
        public bool IncrementOnly { get; set; }
        public bool IsProtected { get; set; }
        public bool IsAvailable { get; set; } = true;
        public bool SetByTrustedGameServer { get; set; }
        public int Permission { get; set; }
    }

    public sealed class ParseResult
    {
        public bool Success => Errors.Count == 0;
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyDictionary<string, double> Targets { get; }
        internal IReadOnlyDictionary<string, string> Tokens { get; }

        internal ParseResult(List<string> errors, Dictionary<string, double> values,
            Dictionary<string, string> tokens)
        {
            Errors = errors.AsReadOnly();
            // 不向调用方暴露半份成功数据。
            Targets = new ReadOnlyDictionary<string, double>(errors.Count == 0
                ? values : new Dictionary<string, double>(StringComparer.Ordinal));
            Tokens = new ReadOnlyDictionary<string, string>(errors.Count == 0
                ? tokens : new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }

    public sealed class StatTarget
    {
        public StatDescriptor Descriptor { get; }
        public double TargetValue { get; }
        public bool Changed => TargetValue != Descriptor.CurrentValue;
        public long EstimatedRounds { get; }
        public bool RequiresStepping => EstimatedRounds > 1;

        internal StatTarget(StatDescriptor descriptor, double value, long rounds)
        {
            Descriptor = descriptor;
            TargetValue = value;
            EstimatedRounds = rounds;
        }
    }

    public sealed class ValidationResult
    {
        public bool Success => Errors.Count == 0;
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<StatTarget> Targets { get; }
        public long EstimatedRounds { get; }
        public bool RequiresStepping => EstimatedRounds > 1;

        internal ValidationResult(List<string> errors, List<StatTarget> targets)
        {
            Errors = errors.AsReadOnly();
            Targets = (errors.Count == 0 ? targets : new List<StatTarget>()).AsReadOnly();
            foreach (var target in Targets)
                EstimatedRounds = Math.Max(EstimatedRounds, target.EstimatedRounds);
        }
    }

    public sealed class StepResult
    {
        public bool Success => Error == null;
        public string Error { get; }
        public double Value { get; }
        public bool Complete { get; }

        internal StepResult(double value, bool complete, string error = null)
        {
            Value = value;
            Complete = complete;
            Error = error;
        }
    }
}
