// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System;
using System.Globalization;
using SAM.Batch;
using SAM.Game.Stats;

internal static class FloatStatInfoTests
{
    public static void Run(Action<string, Action> test)
    {
        test("表格浮点拒绝 NaN、Infinity、溢出、下溢且保留原值", NonFinite);
        test("表格浮点拒绝静默舍入与超长小数", Precision);
        test("表格浮点允许 0.1 与实际 float32 往返值", RoundTrip);
        test("表格浮点支持区域小数点、分组和指数符号", Cultures);
        test("表格浮点维持保护字段语义", Protected);
    }

    private static void NonFinite()
    {
        WithCulture("en-US", () =>
        {
            foreach (string text in new[] { "NaN", "Infinity", "-Infinity", "+Infinity", "1e1000", "1e-1000", "1e39", "1e-50", "", "  " })
                Reject(text);
        });
    }

    private static void Precision()
    {
        WithCulture("en-US", () =>
        {
            foreach (string text in new[] { "16777217", "1.23456789", "0.1000000001", "1.00000000000000000000000000000000001" })
                Reject(text);
        });
    }

    private static void RoundTrip()
    {
        WithCulture("en-US", () =>
        {
            var stat = new FloatStatInfo { Id = "test", OriginalValue = 5, FloatValue = 5 };
            stat.Value = "0.1";
            Assert(stat.FloatValue == 0.1f && stat.OriginalValue == 5 && stat.IsModified);
            foreach (float value in new[] { 0f, -0.1f, 1.2345679f, 16777216f, float.Epsilon, float.MinValue, float.MaxValue })
            {
                stat.Value = value.ToString("R", CultureInfo.CurrentCulture);
                Assert(stat.FloatValue.Equals(value), "Float32 round-trip input changed value.");
            }
        });
    }

    private static void Cultures()
    {
        foreach (string culture in new[] { "en-US", "de-DE", "fr-FR", "hi-IN", "zh-CN" })
        {
            WithCulture(culture, () =>
            {
                var stat = new FloatStatInfo { Id = "test", OriginalValue = 5, FloatValue = 5 };
                stat.Value = 0.1f.ToString("R", CultureInfo.CurrentCulture);
                Assert(stat.FloatValue == 0.1f, culture);
                stat.Value = 1234567.5.ToString("N1", CultureInfo.CurrentCulture);
                Assert(stat.FloatValue == 1234567.5f, culture);
                stat.Value = "1e" + CultureInfo.CurrentCulture.NumberFormat.NegativeSign + "1";
                Assert(stat.FloatValue == 0.1f, culture);
            });
        }
        WithCulture("de-DE", () => Reject("1.23,5"));
        WithCulture("en-US", () => { Reject("1,,234"); Reject("12,34"); });
    }

    private static void Protected()
    {
        WithCulture("en-US", () =>
        {
            var stat = new FloatStatInfo { Id = "test", Permission = 2, OriginalValue = 0.1f, FloatValue = 0.1f };
            stat.Value = "0.1";
            bool rejected = false;
            try { stat.Value = "0.2"; }
            catch (StatIsProtectedException) { rejected = true; }
            Assert(rejected && stat.FloatValue == 0.1f && !stat.IsModified);
        });
    }

    private static void Reject(string text)
    {
        var stat = new FloatStatInfo { Id = "test", OriginalValue = 5, FloatValue = 5 };
        bool rejected = false;
        try { stat.Value = text; }
        catch (FormatException) { rejected = true; }
        Assert(rejected, "Input should be rejected: " + text);
        Assert(stat.FloatValue == 5 && stat.OriginalValue == 5 && !stat.IsModified, "Rejected input changed the bound value.");
    }

    private static void WithCulture(string name, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); action(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void Assert(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
