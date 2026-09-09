// Steam Stats Editor 新增代码；遵循仓库 LICENSE.txt 中的 zlib 许可证。
using System;
using System.Globalization;
using System.IO;
using System.Text;
using SAM.Game;

internal static class SchemaRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("实际 schema 字符串数字跨 locale 解析", SchemaNumbersAcrossCultures);
        test("实际 schema 二进制跨 locale 往返", BinarySchemaAcrossCultures);
        test("schema 单字节与全部数字类型截断及时报错", TruncatedNumbers);
        test("schema 未终止和半字符字符串及时报错", TruncatedStrings);
        test("schema 字符串与数字支持合法短读", PartialReads);
        test("schema 二进制所有截断前缀拒绝加载", TruncatedBinary);
        test("schema 合法嵌套保留兄弟字段", ValidNestedSchema);
        test("schema 嵌套非法类型必须使整棵树加载失败", InvalidNestedSchema);
        test("schema 失败后不保留旧有效状态或部分字段", FailedSchemaIsInvalid);
    }

    private static void Assert(bool condition, string message = "schema 断言失败")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectEndOfStream(Action action)
    {
        try { action(); }
        catch (EndOfStreamException) { return; }
        throw new InvalidOperationException("截断输入必须抛出 EndOfStreamException。");
    }

    private static void InCultures(Action action)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "en-US", "de-DE", "fr-FR", "zh-CN", "ar-EG" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                action();
            }
            // 标准 locale 未必改变整数解析，额外改变正负号和小数点检测隐式区域依赖。
            var custom = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            custom.NumberFormat.NegativeSign = "~";
            custom.NumberFormat.PositiveSign = "!";
            custom.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = custom;
            action();
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static KeyValue StringValue(string value, KeyValueType type = KeyValueType.String)
        => new KeyValue { Type = type, Value = value, Valid = true };

    private static void SchemaNumbersAcrossCultures()
    {
        InCultures(() =>
        {
            foreach (var type in new[] { KeyValueType.String, KeyValueType.WideString })
            {
                Assert(StringValue("-2147483648", type).AsInteger(17) == int.MinValue);
                Assert(StringValue("+2147483647", type).AsInteger(17) == int.MaxValue);
                Assert(StringValue("2147483648", type).AsInteger(17) == 17);
                Assert(StringValue("1,000", type).AsInteger(17) == 17);
                Assert(StringValue("-0.125", type).AsFloat(17) == -0.125f);
                Assert(StringValue("1.25e2", type).AsFloat(17) == 125f);
                Assert(StringValue("1,25", type).AsFloat(17) == 17);
                Assert(StringValue("1,000", type).AsFloat(17) == 17);
                Assert(StringValue("-1", type).AsBoolean(false));
                Assert(!StringValue("0", type).AsBoolean(true));
            }
            Assert(new KeyValue().AsInteger(19) == 19);
            Assert(new KeyValue().AsFloat(19) == 19);
        });
    }

    private static byte[] MakeSchema()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write((byte)KeyValueType.String);
            WriteString(writer, "maxchange"); WriteString(writer, "0.125");
            writer.Write((byte)KeyValueType.String);
            WriteString(writer, "min"); WriteString(writer, "-123");
            writer.Write((byte)KeyValueType.Int32);
            WriteString(writer, "count"); writer.Write(-123456789);
            writer.Write((byte)KeyValueType.Float32);
            WriteString(writer, "ratio"); writer.Write(-1.25f);
            writer.Write((byte)KeyValueType.UInt64);
            WriteString(writer, "wide"); writer.Write(0xFEDCBA9876543210UL);
            writer.Write((byte)KeyValueType.Color);
            WriteString(writer, "color"); writer.Write(0xFEDCBA98U);
            writer.Write((byte)KeyValueType.String);
            WriteString(writer, "display"); WriteString(writer, "中文字段 😀");
            writer.Write((byte)KeyValueType.End);
            return stream.ToArray();
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }

    private static void BinarySchemaAcrossCultures()
    {
        byte[] bytes = MakeSchema();
        InCultures(() =>
        {
            using (var stream = new ChunkedStream(bytes))
            {
                var schema = new KeyValue();
                Assert(schema.ReadAsBinary(stream));
                Assert(schema["maxchange"].AsFloat(19) == 0.125f);
                Assert(schema["min"].AsInteger(19) == -123);
                Assert(schema["count"].AsInteger(19) == -123456789);
                Assert(schema["ratio"].AsFloat(19) == -1.25f);
                Assert((ulong)schema["wide"].Value == 0xFEDCBA9876543210UL);
                Assert((uint)schema["color"].Value == 0xFEDCBA98U);
                Assert(schema["display"].AsString(null) == "中文字段 😀");
                Assert(schema.Children.Count == 7);
            }
        });
    }

    private static void TruncatedNumbers()
    {
        CheckTruncated(1, stream => stream.ReadValueU8());
        CheckTruncated(4, stream => stream.ReadValueS32());
        CheckTruncated(4, stream => stream.ReadValueU32());
        CheckTruncated(4, stream => stream.ReadValueF32());
        CheckTruncated(8, stream => stream.ReadValueU64());
    }

    private static void CheckTruncated(int width, Action<Stream> read)
    {
        for (int size = 0; size < width; size++)
        {
            using (var stream = new ChunkedStream(new byte[size]))
                ExpectEndOfStream(() => read(stream));
        }
    }

    private static void TruncatedStrings()
    {
        foreach (byte[] bytes in new[] { Array.Empty<byte>(), new byte[] { 65 }, Encoding.UTF8.GetBytes("没有结束符"), new byte[] { 0xE4, 0xB8 } })
        {
            using (var stream = new ChunkedStream(bytes))
                ExpectEndOfStream(() => stream.ReadStringUnicode());
        }
        using (var stream = new ChunkedStream(new byte[] { 65 }))
            ExpectEndOfStream(() => stream.ReadStringAscii());
        using (var stream = new ChunkedStream(new byte[] { 65, 0, 0 }))
            ExpectEndOfStream(() => stream.ReadStringInternalDynamic(Encoding.Unicode, '\0'));
        using (var stream = new ChunkedStream(new byte[] { 65, 0, 0, 0, 0, 0, 0 }))
            ExpectEndOfStream(() => stream.ReadStringInternalDynamic(Encoding.UTF32, '\0'));
    }

    private static void PartialReads()
    {
        using (var stream = new ChunkedStream(new byte[] { 255 })) Assert(stream.ReadValueU8() == 255);
        using (var stream = new ChunkedStream(BitConverter.GetBytes(-123456789))) Assert(stream.ReadValueS32() == -123456789);
        using (var stream = new ChunkedStream(BitConverter.GetBytes(0xFEDCBA98U))) Assert(stream.ReadValueU32() == 0xFEDCBA98U);
        using (var stream = new ChunkedStream(BitConverter.GetBytes(0xFEDCBA9876543210UL))) Assert(stream.ReadValueU64() == 0xFEDCBA9876543210UL);
        using (var stream = new ChunkedStream(BitConverter.GetBytes(-1.25f))) Assert(stream.ReadValueF32() == -1.25f);
        string large = new string('A', 257) + "中文 😀";
        foreach (Encoding encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.UTF32 })
        {
            using (var stream = new ChunkedStream(encoding.GetBytes(large + "\0suffix")))
            {
                Assert(stream.ReadStringInternalDynamic(encoding, '\0') == large);
                Assert(stream.Position == encoding.GetByteCount(large + "\0"));
            }
        }
        using (var stream = new ChunkedStream(new byte[] { 0, 65 }))
        {
            Assert(stream.ReadStringAscii() == "");
            Assert(stream.Position == 1);
        }
    }

    private static void TruncatedBinary()
    {
        byte[] valid = MakeSchema();
        for (int size = 0; size < valid.Length; size++)
        {
            var truncated = new byte[size];
            Array.Copy(valid, truncated, size);
            using (var stream = new ChunkedStream(truncated))
                Assert(!new KeyValue().ReadAsBinary(stream), "截断长度 " + size + " 不能加载成功。");
        }
        using (var stream = new ChunkedStream(valid)) Assert(new KeyValue().ReadAsBinary(stream));
        using (var stream = new ChunkedStream(new byte[] { (byte)KeyValueType.None, 65, 0 }))
            Assert(!new KeyValue().ReadAsBinary(stream));
    }

    private static byte[] MakeNestedSchema(int depth, byte? invalidType = null)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            for (int level = 0; level < depth; level++)
            {
                writer.Write((byte)KeyValueType.None);
                WriteString(writer, "level" + level);
            }
            writer.Write(invalidType ?? (byte)KeyValueType.Int32);
            WriteString(writer, "value");
            if (invalidType == null) writer.Write(123);
            for (int level = 0; level < depth; level++) writer.Write((byte)KeyValueType.End);
            if (invalidType == null)
            {
                writer.Write((byte)KeyValueType.String);
                WriteString(writer, "sibling"); WriteString(writer, "retained");
                writer.Write((byte)KeyValueType.End);
            }
            return stream.ToArray();
        }
    }

    private static void ValidNestedSchema()
    {
        for (int depth = 1; depth <= 4; depth++)
        {
            using (var stream = new ChunkedStream(MakeNestedSchema(depth)))
            {
                var root = new KeyValue();
                Assert(root.ReadAsBinary(stream) && root.Valid);
                Assert(root["sibling"].AsString(null) == "retained");
                KeyValue current = root;
                for (int level = 0; level < depth; level++)
                {
                    current = current["level" + level];
                    Assert(current.Valid);
                }
                Assert(current["value"].AsInteger(-1) == 123);
            }
        }
    }

    private static void InvalidNestedSchema()
    {
        for (int depth = 1; depth <= 4; depth++)
        {
            foreach (byte invalid in new byte[] { (byte)KeyValueType.WideString, 9, 255 })
            {
                using (var stream = new ChunkedStream(MakeNestedSchema(depth, invalid)))
                {
                    var schema = new KeyValue();
                    Assert(!schema.ReadAsBinary(stream), "嵌套深度 " + depth + " 的非法类型 " + invalid + " 被错误接受。");
                    Assert(!schema.Valid && schema.Children.Count == 0);
                }
            }
            byte[] valid = MakeNestedSchema(depth);
            for (int length = 0; length < valid.Length; length++)
            {
                var truncated = new byte[length];
                Array.Copy(valid, truncated, length);
                using (var stream = new ChunkedStream(truncated))
                    Assert(!new KeyValue().ReadAsBinary(stream), "截断嵌套 schema 被错误接受。");
            }
        }
    }

    private static void FailedSchemaIsInvalid()
    {
        var schema = new KeyValue();
        using (var valid = new ChunkedStream(MakeSchema())) Assert(schema.ReadAsBinary(valid));
        using (var invalid = new ChunkedStream(MakeNestedSchema(2, 9)))
            Assert(!schema.ReadAsBinary(invalid));
        Assert(!schema.Valid && schema.Children.Count == 0);
        byte[] bytes = MakeSchema();
        Array.Resize(ref bytes, bytes.Length + 1);
        using (var trailing = new ChunkedStream(bytes)) Assert(!schema.ReadAsBinary(trailing));
        Assert(!schema.Valid && schema.Children.Count == 0);
    }

    private sealed class ChunkedStream : MemoryStream
    {
        internal ChunkedStream(byte[] bytes) : base(bytes, false) { }

        public override int Read(byte[] buffer, int offset, int count)
            => base.Read(buffer, offset, Math.Min(count, 1));
    }
}
