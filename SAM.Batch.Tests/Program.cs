// Steam Stats Editor 新增代码；遵循仓库 LICENSE.txt 中的 zlib 许可证。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SAM.Batch;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Test("导出、解析、校验原样往返", RoundTrip);
        Test("错误 AppID 拒绝整批", () => RejectParse("format = steam-stats-v1\nappid = 440\n[stats]\na = 1"));
        Test("版本、区段与头部严格校验", Headers);
        Test("未知字段使整批失效", () => RejectValidate("a = 1\nunknown = 2", Integer()));
        Test("重复字段拒绝整批", () => RejectParse(File("a = 1\na = 2")));
        Test("缺少字段保持不变，零有效", MissingAndZero);
        Test("非法数字及逗号拒绝整批", InvalidNumbers);
        Test("32 位整数边界、溢出与原始精度", Integers);
        Test("注释中的限制不影响真实 schema", ActualSchema);
        Test("字段大小写敏感", () => RejectValidate("A = 1", Integer()));
        Test("不可读字段拒绝设置", () => RejectValidate("a = 0", Integer(available: false)));
        Test("受保护字段和未知权限只可原值回导", Permissions);
        Test("平均速率仅支持原值回导", AverageRates);
        Test("真实 min/max 边界", Bounds);
        Test("当前越界时不能规划越界的中间步骤", IntermediateBounds);
        Test("仅增字段禁止降低", IncrementOnly);
        Test("整数正向与反向分步", IntegerSteps);
        Test("多字段同轮并行取最大轮数", ParallelRounds);
        Test("整数跨越全部 int32 范围不溢出", IntegerFullRange);
        Test("maxchange=0 单次提交，非法限制拒绝", Limits);
        Test("float32 规范化、溢出与下溢", FloatNormalization);
        Test("float32 不可保留的目标精度必须明确报错", FloatPrecision);
        Test("float32 舍入绝不超步幅", FloatRounding);
        Test("float32 无法前进与途中不可达", FloatUnreachable);
        Test("大规模 float32 步进无逐轮枚举", FloatLargePlan);
        Test("float32 多指数区间往返计数", FloatSegments);
        Test("float32 全指数范围与正负极值", FloatExtremes);
        Test("不同区域设置与 UTF8 BOM", Cultures);
        Test("导出注释不允许换行注入", ExportInjection);
        Test("失败批次不暴露有效子集", AtomicFailure);
        Test("损坏与重复 schema 拒绝整批", InvalidSchema);
        Test("随机 float32 计划与逐步执行一致", RandomizedPlans);
        SchemaRegressionTests.Run(Test);
        FloatStatInfoTests.Run(Test);
        Console.WriteLine("通过 " + _passed + " 项，失败 " + _failed + " 项。");
        return _failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action action)
    {
        try { action(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception exception) { _failed++; Console.Error.WriteLine("FAIL " + name + "：" + exception); }
    }

    private static void Assert(bool condition, string message = "断言失败")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string File(string assignments) => "format = steam-stats-v1\nappid = 730\n[stats]\n" + assignments;

    private static StatDescriptor Integer(string id = "a", double current = 0, double maxChange = 0, bool available = true)
        => new StatDescriptor
        {
            Id = id, DisplayName = "示例 " + id, Kind = StatValueKind.Integer,
            CurrentValue = current, Minimum = int.MinValue, Maximum = int.MaxValue,
            MaxChange = maxChange, IsAvailable = available,
        };

    private static StatDescriptor Float(string id = "a", float current = 0, double maxChange = 0)
        => new StatDescriptor
        {
            Id = id, DisplayName = "浮点示例 " + id, Kind = StatValueKind.Float,
            CurrentValue = current, Minimum = -float.MaxValue, Maximum = float.MaxValue,
            MaxChange = maxChange,
        };

    private static ValidationResult Validate(string assignments, params StatDescriptor[] descriptors)
        => BatchValidator.Validate(BatchText.Parse(File(assignments), 730), descriptors);

    private static ValidationResult Direct(StatDescriptor descriptor, double value)
        => BatchValidator.ValidateTargets(new[] { descriptor }, new Dictionary<string, double> { [descriptor.Id] = value });

    private static void RejectParse(string text)
    {
        var parsed = BatchText.Parse(text, 730);
        Assert(!parsed.Success && parsed.Errors.Count > 0 && parsed.Targets.Count == 0);
    }

    private static void RejectValidate(string assignments, params StatDescriptor[] descriptors)
    {
        var result = Validate(assignments, descriptors);
        Assert(!result.Success && result.Errors.Count > 0 && result.Targets.Count == 0);
    }

    private static void RoundTrip()
    {
        var protectedStat = Integer("locked", 7); protectedStat.IsProtected = true;
        var average = Float("rate", 0.1f); average.Kind = StatValueKind.AverageRate;
        var schema = new[] { Integer(current: 34), Float("f", -0.1f), protectedStat, average, Integer("unreadable", available: false) };
        string text = BatchText.Export(730, schema);
        Assert(text.Contains("id=unreadable") && !text.Contains("\nunreadable = "));
        var parsed = BatchText.Parse(text, 730);
        Assert(parsed.Success, string.Join("; ", parsed.Errors));
        var result = BatchValidator.Validate(parsed, schema);
        Assert(result.Success, string.Join("; ", result.Errors));
        Assert(result.Targets.Count == 4 && result.Targets.All(t => !t.Changed) && result.EstimatedRounds == 0);
    }

    private static void Headers()
    {
        RejectParse(File("a = 1").Replace("steam-stats-v1", "steam-stats-v2"));
        RejectParse(File("a = 1").Replace("[stats]", "[other]"));
        RejectParse(File("a = 1\n[stats]\nb = 2"));
        RejectParse("appid = 730\n[stats]\na = 1");
        RejectParse("format = steam-stats-v1\nappid = 730\nappid = 730\n[stats]");
        RejectParse("format = steam-stats-v1\nappid = 730\na = 1");
        RejectParse("format = steam-stats-v1\nappid = 730\nunknown = x\n[stats]");
    }

    private static void MissingAndZero()
    {
        var result = Validate("a = 0", Integer(current: 99), Integer("b", 88));
        Assert(result.Success && result.Targets.Count == 1 && result.Targets[0].TargetValue == 0 && result.Targets[0].Changed);
        result = Validate("", Integer());
        Assert(result.Success && result.Targets.Count == 0);
    }

    private static void InvalidNumbers()
    {
        foreach (string value in new[] { "NaN", "Infinity", "-Infinity", "1,000", "1,5", "1e309", "1e-999", "abc", "0x10", "1 2", "--1", "1 = 2" })
            RejectParse(File("a = 3\nb = " + value));
        Assert(BatchText.Parse(File("a = -0e999\nb = +1.5e+2\nc = .5"), 730).Success);
    }

    private static void Integers()
    {
        foreach (string value in new[] { "2147483648", "-2147483649", "1.1", "2147483647.00000001", "9007199254740993", "1.0000000000000000000000000000000001" })
            RejectValidate("a = " + value, Integer());
        foreach (string value in new[] { "2147483647", "-2147483648", "2.147483647e9", "100e-2", "0.000", "-0", "1.00" })
            Assert(Validate("a = " + value, Integer()).Success, value);
    }

    private static void ActualSchema()
    {
        var descriptor = Integer(maxChange: 1); descriptor.Maximum = 10;
        RejectValidate("# max=999999 maxchange=999999\na = 20", descriptor);
        var result = Validate("# maxchange=99999\na = 7", descriptor);
        Assert(result.Success && result.EstimatedRounds == 7 && result.RequiresStepping);
    }

    private static void Permissions()
    {
        foreach (int permission in new[] { 0, 1, 2, 4, 8 })
        {
            var descriptor = Integer(current: 7); descriptor.Permission = permission;
            descriptor.IsProtected = permission == 0;
            RejectValidate("a = 8", descriptor);
            Assert(Validate("a = 7", descriptor).Success);
        }
        var trusted = Integer(current: 7); trusted.SetByTrustedGameServer = true;
        RejectValidate("a = 8", trusted);
        Assert(Validate("a = 7", trusted).Success);
    }

    private static void AverageRates()
    {
        var average = Float(current: 0.1f); average.Kind = StatValueKind.AverageRate;
        Assert(Validate("a = 0.1", average).Success);
        RejectValidate("a = 0.2", average);
    }

    private static void Bounds()
    {
        var descriptor = Integer(); descriptor.Minimum = -1; descriptor.Maximum = 3;
        Assert(Validate("a = -1", descriptor).Success && Validate("a = 3", descriptor).Success);
        RejectValidate("a = -2", descriptor); RejectValidate("a = 4", descriptor);
        descriptor.CurrentValue = 10;
        Assert(Validate("a = 10", descriptor).Success);
    }

    private static void IncrementOnly()
    {
        var descriptor = Integer(current: 8); descriptor.IncrementOnly = true;
        RejectValidate("a = 7", descriptor);
        Assert(Validate("a = 8", descriptor).Success && Validate("a = 9", descriptor).Success);
    }

    private static void IntermediateBounds()
    {
        var descriptor = Integer(current: -10, maxChange: 1); descriptor.Minimum = 0;
        RejectValidate("a = 5", descriptor);
        Assert(!StepPlanner.Next(descriptor, 5).Success);
        descriptor.MaxChange = 10;
        Assert(Validate("a = 5", descriptor).Success);
        Assert(StepPlanner.Next(descriptor, 5).Value == 0);
        descriptor = Integer(current: 10, maxChange: 1); descriptor.Maximum = 0;
        RejectValidate("a = -5", descriptor);
    }

    private static void IntegerSteps()
    {
        AssertSequence(Integer(current: 2, maxChange: 3), 10, new double[] { 5, 8, 10 });
        AssertSequence(Integer(current: 10, maxChange: 3), -1, new double[] { 7, 4, 1, -1 });
    }

    private static void AssertSequence(StatDescriptor descriptor, double target, double[] expected)
    {
        var result = Direct(descriptor, target);
        Assert(result.Success && result.EstimatedRounds == expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            var step = StepPlanner.Next(descriptor, target);
            Assert(step.Success && step.Value == expected[i] && step.Complete == (i == expected.Length - 1));
            descriptor.CurrentValue = step.Value;
        }
        Assert(StepPlanner.Next(descriptor, target).Complete);
    }

    private static void ParallelRounds()
    {
        var result = Validate("a = 9\nb = 10\nc = 0", Integer(maxChange: 2), Integer("b", maxChange: 3), Integer("c"));
        Assert(result.Success && result.EstimatedRounds == 5 && result.Targets.Count == 3);
    }

    private static void IntegerFullRange()
    {
        var result = Direct(Integer(current: int.MinValue, maxChange: 1), int.MaxValue);
        Assert(result.Success && result.EstimatedRounds == 4294967295L);
    }

    private static void Limits()
    {
        Assert(Direct(Integer(), 100).EstimatedRounds == 1);
        Assert(!Direct(Integer(maxChange: -1), 10).Success);
        Assert(!Direct(Integer(maxChange: 0.5), 10).Success);
        Assert(!Direct(Integer(maxChange: double.NaN), 10).Success);
    }

    private static void FloatNormalization()
    {
        var result = Validate("a = 0.1", Float());
        Assert(result.Success && result.Targets[0].TargetValue == (double)0.1f);
        RejectValidate("a = 1e40", Float()); RejectValidate("a = 1e-50", Float());
        var descriptor = Float(); descriptor.Maximum = 0.1;
        RejectValidate("a = 0.1", descriptor);
    }

    private static void FloatRounding()
    {
        var descriptor = Float(current: 1, maxChange: 0.1f);
        var step = StepPlanner.Next(descriptor, 2);
        Assert(step.Success && step.Value > 1 && step.Value - 1 <= descriptor.MaxChange);
        descriptor = Float(current: -1, maxChange: 0.1f);
        step = StepPlanner.Next(descriptor, -2);
        Assert(step.Success && step.Value < -1 && -1 - step.Value <= descriptor.MaxChange);
    }

    private static void FloatPrecision()
    {
        foreach (string token in new[] { "16777217", "0.123456789", "0.1000000000000000000000001", "1.0000000000000000000000001" })
            RejectValidate("a = " + token, Float());
        Assert(!StepPlanner.Next(Float(), 16777217).Success);
        foreach (string token in new[] { "0.1", "1e-1", "0.1000", "0.10000000149011612", "16777216", "-0e999" })
            Assert(Validate("a = " + token, Float()).Success, token);
        // 不能因不可表示的目标恰好舍入到当前值，就被当成“无需修改”。
        RejectValidate("a = 16777217", Float(current: 16777216));
        var result = Validate("a = 16777217", Float());
        Assert(result.Errors.Any(error => error.Contains("16777216")));
    }

    private static void FloatUnreachable()
    {
        Assert(!StepPlanner.Next(Float(current: 16777216, maxChange: 1), 16777218).Success);
        Assert(!Direct(Float(current: 16777215, maxChange: 1), 16777218).Success);
        Assert(!Direct(Float(current: -16777215, maxChange: 1), -16777218).Success);
    }

    private static void FloatLargePlan()
    {
        float epsilon = BitConverter.ToSingle(BitConverter.GetBytes(1u), 0);
        float end = BitConverter.ToSingle(BitConverter.GetBytes(0x01000000u), 0);
        var result = Direct(Float(maxChange: epsilon), end);
        Assert(result.Success && result.EstimatedRounds == 16777216L, string.Join("; ", result.Errors));
        float unreachable = BitConverter.ToSingle(BitConverter.GetBytes(0x01000001u), 0);
        Assert(!Direct(Float(maxChange: epsilon), unreachable).Success);
    }

    private static void FloatSegments()
    {
        CheckPlanAgainstExecution(Float(current: -32, maxChange: 0.3f), 33);
        CheckPlanAgainstExecution(Float(current: 32, maxChange: 0.3f), -33);
        CheckPlanAgainstExecution(Float(current: -1.01f, maxChange: 0.025f), 1.03f);
    }

    private static void FloatExtremes()
    {
        AssertSequence(Float(current: -float.MaxValue, maxChange: float.MaxValue), float.MaxValue,
            new double[] { 0, float.MaxValue });
        float epsilon = BitConverter.ToSingle(BitConverter.GetBytes(1u), 0);
        float boundary = BitConverter.ToSingle(BitConverter.GetBytes(0x01000000u), 0);
        var large = Direct(Float(current: -boundary, maxChange: epsilon), boundary);
        Assert(large.Success && large.EstimatedRounds == 33554432L);
        var random = new Random(731);
        for (int exponent = 0; exponent < 255; exponent++)
        {
            uint bits = ((uint)exponent << 23) | 0x7FFFC0u;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float current = sign * BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                float target = sign * BitConverter.ToSingle(BitConverter.GetBytes(bits - 30), 0);
                double gap = Math.Abs(current - (double)(sign * BitConverter.ToSingle(BitConverter.GetBytes(bits - 1), 0)));
                CheckPlanAgainstExecution(Float(current: current, maxChange: gap * (0.9 + random.NextDouble() * 5)), target);
                if (exponent < 254)
                {
                    target = sign * BitConverter.ToSingle(BitConverter.GetBytes(bits + 100), 0);
                    CheckPlanAgainstExecution(Float(current: current, maxChange: gap * (0.9 + random.NextDouble() * 5)), target);
                }
            }
        }
    }

    private static void Cultures()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "fr-FR", "zh-CN", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                var descriptor = Float(current: 1.25f);
                var parsed = BatchText.Parse("\uFEFF" + BatchText.Export(730, new[] { descriptor }), 730);
                Assert(parsed.Success && BatchValidator.Validate(parsed, new[] { descriptor }).Success, name);
                Assert(Validate("a = 2.5 # 中文注释", descriptor).Success, name);
                RejectParse(File("a = 2,5"));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void ExportInjection()
    {
        var descriptor = Integer(); descriptor.DisplayName = "name\na = 999\r[other]";
        string text = BatchText.Export(730, new[] { descriptor });
        var parsed = BatchText.Parse(text, 730);
        Assert(parsed.Success && parsed.Targets.Count == 1 && parsed.Targets["a"] == 0);
    }

    private static void AtomicFailure()
    {
        var schema = new[] { Integer(), Integer("b") };
        var result = Validate("a = 123\nb = 3.1", schema);
        Assert(!result.Success && result.Targets.Count == 0 && schema.All(s => s.CurrentValue == 0));
        var parsed = BatchText.Parse(File("a = 123\nb = nope"), 730);
        Assert(!parsed.Success && parsed.Targets.Count == 0);
    }

    private static void InvalidSchema()
    {
        RejectValidate("a = 1", Integer(), Integer());
        var descriptor = Integer(); descriptor.Minimum = 4; descriptor.Maximum = 3;
        RejectValidate("a = 1", descriptor);
        RejectValidate("a = 1", null, Integer());
        descriptor = Float(); descriptor.CurrentValue = 0.1;
        RejectValidate("a = 0.2", descriptor);
    }

    private static void RandomizedPlans()
    {
        var random = new Random(730);
        for (int i = 0; i < 600; i++)
        {
            float current = (float)(random.NextDouble() * 128 - 64);
            float target = (float)(random.NextDouble() * 128 - 64);
            float maxChange = (float)(0.05 + random.NextDouble() * 3);
            CheckPlanAgainstExecution(Float(current: current, maxChange: maxChange), target);
        }
        // 在 ULP 附近测试舍入边界，而不只测试容易表示的小数。
        for (int i = 0; i < 300; i++)
        {
            uint bits = (uint)random.Next(0x3F700000, 0x3F900000);
            float current = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
            float target = BitConverter.ToSingle(BitConverter.GetBytes(bits + (uint)random.Next(1, 100)), 0);
            double gap = BitConverter.ToSingle(BitConverter.GetBytes(bits + 1), 0) - (double)current;
            double cap = gap * (0.9 + random.NextDouble() * 5);
            CheckPlanAgainstExecution(Float(current: current, maxChange: cap), target);
        }
    }

    private static void CheckPlanAgainstExecution(StatDescriptor descriptor, double target)
    {
        var planned = Direct(descriptor, target);
        long actual = 0;
        while (descriptor.CurrentValue != (double)(float)target)
        {
            var step = StepPlanner.Next(descriptor, target);
            if (!step.Success)
            {
                Assert(!planned.Success, "规划声称可达，但实际无法前进");
                return;
            }
            Assert(Math.Abs(step.Value - descriptor.CurrentValue) <= descriptor.MaxChange);
            descriptor.CurrentValue = step.Value;
            actual++;
            Assert(actual <= 10000, "测试本身超过逐步上限");
        }
        Assert(planned.Success, string.Join("; ", planned.Errors));
        Assert(planned.EstimatedRounds == actual, "预计 " + planned.EstimatedRounds + "，实际 " + actual);
    }
}
