using System.Globalization;
using System.Numerics;
using CodecHub.DevToys.Core;

int passed = 0;
int failed = 0;

Console.WriteLine("=== CodecHub.DevToys 编码 / 进制逻辑冒烟测试 ===");

// ---------------------------------------------------------------- URL
Index("URL 组件编码");
Eq("URL 编码中文与保留字", TextCodecs.UrlEncode("a b&c=中文", false), "a%20b%26c%3D%E4%B8%AD%E6%96%87");
Eq("URL 解码往返", TextCodecs.UrlDecode("a%20b%26c%3D%E4%B8%AD%E6%96%87", false), "a b&c=中文");
Eq("表单模式空格为 +", TextCodecs.UrlEncode("a b&c", true), "a+b%26c");
Eq("表单模式 + 解成空格", TextCodecs.UrlDecode("a+b%26c", true), "a b&c");
Eq("URL 模式下 + 保持原样", TextCodecs.UrlDecode("a+b", false), "a+b");
Eq("未保留字符不编码", TextCodecs.UrlEncode("a-b_c.d~", false), "a-b_c.d~");
Fails("URL 非法百分号报错", () => TextCodecs.UrlDecode("a%zz", false));
Fails("URL 截断百分号报错", () => TextCodecs.UrlDecode("a%2", false));

// ---------------------------------------------------------------- HTML
Index("HTML 实体");
Eq("HTML 编码", TextCodecs.HtmlEncode("<a href=\"x\">&"), "&lt;a href=&quot;x&quot;&gt;&amp;");
Eq("HTML 解码往返", TextCodecs.HtmlDecode("&lt;a href=&quot;x&quot;&gt;&amp;"), "<a href=\"x\">&");
Eq("数字实体解码", TextCodecs.HtmlDecode("&#x4E2D;&#20013;"), "中中");

// ---------------------------------------------------------------- Unicode
Index("Unicode 转义");
Eq("\\u 编码", TextCodecs.UnicodeEncode("中文A", TextEscapeStyle.Universal), "\\u4E2D\\u6587A");
Eq("Emoji 编码为代理对", TextCodecs.UnicodeEncode("😀", TextEscapeStyle.Universal), "\\uD83D\\uDE00");
Eq("码点写法", TextCodecs.UnicodeEncode("😀A", TextEscapeStyle.CodePoint), "U+1F600 U+0041");
Eq("\\x 写法", TextCodecs.UnicodeEncode("Aé中", TextEscapeStyle.HexEscape), "A\\xE9\\u4E2D");
Eq("\\u 解码", TextCodecs.UnicodeDecode("\\u4E2D\\u6587A"), "中文A");
Eq("代理对解码", TextCodecs.UnicodeDecode("\\uD83D\\uDE00"), "😀");
Eq("%u 解码", TextCodecs.UnicodeDecode("%u4E2D"), "中");
Eq("U+ 解码", TextCodecs.UnicodeDecode("U+4E2D U+1F600"), "中 😀");
Eq("普通文本原样保留", TextCodecs.UnicodeDecode("a\\x62c"), "abc");
Fails("孤立高位代理项报错", () => TextCodecs.UnicodeDecode("\\uD83D"));
Fails("非法码点报错", () => TextCodecs.UnicodeDecode("U+110000"));

// ---------------------------------------------------------------- JSON
Index("JSON 字符串转义");
Eq("JSON 转义", TextCodecs.JsonEscape("a\"b\n\\c\t"), "a\\\"b\\n\\\\c\\t");
Eq("JSON 反转义往返", TextCodecs.JsonUnescape("a\\\"b\\n\\\\c\\t"), "a\"b\n\\c\t");
Eq("控制字符转义", TextCodecs.JsonEscape("\u0001"), "\\u0001");
Eq("JSON 代理对反转义", TextCodecs.JsonUnescape("\\uD83D\\uDE00"), "😀");
Eq("斜杠转义", TextCodecs.JsonUnescape("a\\/b"), "a/b");
Fails("JSON 非法转义报错", () => TextCodecs.JsonUnescape("\\q"));

// ---------------------------------------------------------------- Base64
Index("Base64 / Base64url");
Eq("RFC 4648 f", BinaryCodecs.Base64Encode("f"u8.ToArray(), false, true), "Zg==");
Eq("RFC 4648 fo", BinaryCodecs.Base64Encode("fo"u8.ToArray(), false, true), "Zm8=");
Eq("RFC 4648 foo", BinaryCodecs.Base64Encode("foo"u8.ToArray(), false, true), "Zm9v");
Eq("RFC 4648 foobar", BinaryCodecs.Base64Encode("foobar"u8.ToArray(), false, true), "Zm9vYmFy");
Eq("无填充", BinaryCodecs.Base64Encode("f"u8.ToArray(), false, false), "Zg");
Eq("URL 安全字母表", BinaryCodecs.Base64Encode(new byte[] { 0xFB, 0xFF }, true, true), "-_8=");
Eq("URL 安全无填充", BinaryCodecs.Base64Encode(new byte[] { 0xFB, 0xFF }, true, false), "-_8");
Eq("Base64 解码忽略换行", System.Text.Encoding.UTF8.GetString(BinaryCodecs.Base64Decode("Zm9v\nYmFy", false)), "foobar");
Eq("无填充可解码", System.Text.Encoding.UTF8.GetString(BinaryCodecs.Base64Decode("Zg", false)), "f");
Eq("URL 安全可解码", Convert.ToHexString(BinaryCodecs.Base64Decode("-_8=", true)), "FBFF");
Fails("非法 Base64 报错", () => BinaryCodecs.Base64Decode("Zm#v", false));

// ---------------------------------------------------------------- Base32 / Base58
Index("Base32 / Base58");
Eq("Base32 f", BinaryCodecs.Base32Encode("f"u8.ToArray()), "MY======");
Eq("Base32 fo", BinaryCodecs.Base32Encode("fo"u8.ToArray()), "MZXQ====");
Eq("Base32 foo", BinaryCodecs.Base32Encode("foo"u8.ToArray()), "MZXW6===");
Eq("Base32 解码", System.Text.Encoding.UTF8.GetString(BinaryCodecs.Base32Decode("MZXW6===")), "foo");
Eq("Base58 已知向量", BinaryCodecs.Base58Encode("hello world"u8.ToArray()), "StV1DL6CwTryKyV");
Eq("Base58 解码", System.Text.Encoding.UTF8.GetString(BinaryCodecs.Base58Decode("StV1DL6CwTryKyV")), "hello world");
Eq("Base58 前导零", Convert.ToHexString(BinaryCodecs.Base58Decode("112")), "000001");
Eq("Base58 空串", BinaryCodecs.Base58Encode([]), "");
Fails("Base58 禁用字符报错", () => BinaryCodecs.Base58Decode("0OIl"));

// ---------------------------------------------------------------- Hex
Index("Hex 字节");
Eq("Hex 编码", BinaryCodecs.HexEncode("Hello"u8.ToArray(), true, ""), "48656C6C6F");
Eq("Hex 小写带分隔符", BinaryCodecs.HexEncode("Hello"u8.ToArray(), false, "-"), "48-65-6c-6c-6f");
Eq("Hex 带分隔符解码", System.Text.Encoding.UTF8.GetString(BinaryCodecs.HexDecode("48-65-6c-6c-6f", "-")), "Hello");
Eq("Hex 忽略空白", Convert.ToHexString(BinaryCodecs.HexDecode("48 65 6C", "")), "48656C");
Fails("Hex 奇数长度报错", () => BinaryCodecs.HexDecode("ABC", ""));
Fails("Hex 非法字符报错", () => BinaryCodecs.HexDecode("ZZ", ""));

// ---------------------------------------------------------------- 字符集
Index("字符集转码");
Eq("UTF-8 字节", CodecPrimitives.HexEncode(CodecPrimitives.TextToBytes("中文", CharsetName.Utf8, false)), "E4B8ADE69687");
Eq("GB18030 字节", CodecPrimitives.HexEncode(CodecPrimitives.TextToBytes("中文", CharsetName.Gb18030, false)), "D6D0CEC4");
Eq("GB18030 往返", CodecPrimitives.BytesToText(CodecPrimitives.HexDecode("D6D0CEC4"), CharsetName.Gb18030), "中文");
Eq("UTF-16LE + BOM", CodecPrimitives.HexEncode(CodecPrimitives.TextToBytes("中文", CharsetName.Utf16Le, true)), "FFFE2D4E8765");
Eq("UTF-8 + BOM", CodecPrimitives.HexEncode(CodecPrimitives.TextToBytes("A", CharsetName.Utf8, true)), "EFBBBF41");
Fails("ASCII 无法表示中文", () => CodecPrimitives.TextToBytes("中文", CharsetName.Ascii, false));
Fails("非法 UTF-8 报错", () => CodecPrimitives.BytesToText(new byte[] { 0xFF }, CharsetName.Utf8));

// ---------------------------------------------------------------- 进制
Index("进制互转");
Eq("10 → 2", NumberCodecs.ConvertRadix("255", 10, 2), "11111111");
Eq("16 → 10", NumberCodecs.ConvertRadix("FF", 16, 10), "255");
Eq("负数", NumberCodecs.ConvertRadix("-10", 10, 2), "-1010");
Eq("0x 前缀", NumberCodecs.ConvertRadix("0xFF", 16, 8), "377");
Eq("36 进制", NumberCodecs.ConvertRadix("Z", 36, 10), "35");
Check("BigInteger 精度", () =>
{
    BigInteger big = BigInteger.Parse("123456789012345678901234567890");
    return NumberCodecs.ConvertRadix(NumberCodecs.ConvertRadix(big.ToString(), 10, 16), 16, 10) == big.ToString();
});
Eq("分隔符数字", NumberCodecs.ConvertRadix("1_000", 10, 16), "3E8");
Fails("进制非法数字报错", () => NumberCodecs.ConvertRadix("2", 2, 10));
Fails("非法进制报错", () => NumberCodecs.ConvertRadix("1", 1, 10));

// ---------------------------------------------------------------- 补码 / 字节序
Index("补码与字节序");
Eq("-1 八位补码", NumberCodecs.EncodeTwosComplement(-1, 8, true, ByteOrder.Big), "FF");
Eq("255 八位无符号", NumberCodecs.EncodeTwosComplement(255, 8, false, ByteOrder.Big), "FF");
Eq("1 十六位小端", NumberCodecs.EncodeTwosComplement(1, 16, false, ByteOrder.Little), "0100");
Eq("1 十六位大端", NumberCodecs.EncodeTwosComplement(1, 16, false, ByteOrder.Big), "0001");
Eq("-1 三十二位", NumberCodecs.EncodeTwosComplement(-1, 32, true, ByteOrder.Big), "FFFFFFFF");
Eq("八位有符号解码", NumberCodecs.DecodeTwosComplement("FF", 8, true, ByteOrder.Big).ToString(), "-1");
Eq("八位无符号解码", NumberCodecs.DecodeTwosComplement("FF", 8, false, ByteOrder.Big).ToString(), "255");
Eq("小端解码", NumberCodecs.DecodeTwosComplement("0100", 16, false, ByteOrder.Little).ToString(), "1");
Check("补码视图含定宽二进制", () => NumberCodecs.TwosComplementViews(-1, 8, true, ByteOrder.Big)
    .Any(v => v.Text == "11111111"));
Fails("有符号越界报错", () => NumberCodecs.EncodeTwosComplement(128, 8, true, ByteOrder.Big));
Fails("位宽非法报错", () => NumberCodecs.EncodeTwosComplement(1, 12, true, ByteOrder.Big));

// ---------------------------------------------------------------- IPv4
Index("IPv4");
Eq("IP → 整数", NumberCodecs.EncodeIp("192.168.1.1"), "3232235777");
Eq("整数 → IP", NumberCodecs.DecodeIp("3232235777"), "192.168.1.1");
Eq("Hex → IP", NumberCodecs.DecodeIp("0xC0A80101"), "192.168.1.1");
Eq("主机序视图", NumberCodecs.IpViews("192.0.2.1").First(v => v.Title == "整数（主机序）").Text, "16908480");
Fails("非法 IP 报错", () => NumberCodecs.EncodeIp("999.1.1.1"));
Fails("越界整数报错", () => NumberCodecs.DecodeIp("4294967296"));

// ---------------------------------------------------------------- 时间戳
Index("Unix 时间戳");
Eq("Unix 纪元秒", NumberCodecs.TimeToEpoch("1970-01-01T00:00:00Z", "s", true), "0");
Eq("带偏移的同一时刻", NumberCodecs.TimeToEpoch("1970-01-01T08:00:00+08:00", "s", true), "0");
Eq("毫秒/秒关系", NumberCodecs.TimeToEpoch("1970-01-01T00:00:01Z", "ms", true), "1000");
Eq("微秒", NumberCodecs.TimeToEpoch("1970-01-01T00:00:01Z", "us", true), "1000000");
Eq("纳秒", NumberCodecs.TimeToEpoch("1970-01-01T00:00:01Z", "ns", true), "1000000000");
Check("秒/毫秒往返", () =>
{
    string text = "2026-11-12 11:27:30";
    string epoch = NumberCodecs.TimeToEpoch(text, "ms", true);
    return NumberCodecs.EpochToTime(epoch, "ms", true).Contains("2026-11-12 11:27:30", StringComparison.Ordinal);
});
Check("UTC 视图存在", () => NumberCodecs.TimeViews(DateTimeOffset.UnixEpoch).Any(v => v.Title == "UTC" && v.Text.Contains("1970-01-01", StringComparison.Ordinal)));
Fails("非法日期报错", () => NumberCodecs.TimeToEpoch("不是日期", "s", true));
Fails("越界时间戳报错", () => NumberCodecs.EpochToTime("999999999999999999999", "s", true));

// ---------------------------------------------------------------- GUID
Index("GUID 格式");
Eq("GUID 规范化", NumberCodecs.NormalizeGuid("550E8400-E29B-41D4-A716-446655440000"), "550e8400-e29b-41d4-a716-446655440000");
Eq("花括号 GUID", NumberCodecs.NormalizeGuid("{550e8400-e29b-41d4-a716-446655440000}"), "550e8400-e29b-41d4-a716-446655440000");
Check("GUID 视图含五种格式", () =>
{
    IReadOnlyList<CodecView> views = NumberCodecs.GuidViews(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"));
    return views.Any(v => v.Title.StartsWith("N", StringComparison.Ordinal)) &&
           views.Any(v => v.Title.StartsWith("X", StringComparison.Ordinal)) &&
           views.Any(v => v.Title.Contains("混合序", StringComparison.Ordinal));
});
Fails("非法 GUID 报错", () => NumberCodecs.NormalizeGuid("not-a-guid"));

// ---------------------------------------------------------------- 引擎
Index("统一引擎");
Eq("引擎标题", CodecEngine.ModeTitle(CodecMode.Base64, CodecDirection.Decode), "Base64 → 解码");
Check("文本模式附带多视图", () => CodecEngine.Run("中文", new CodecOptions { Mode = CodecMode.Base64 }).Views.Count >= 3);
Check("链式转换", () =>
{
    CodecResult result = CodecEngine.Run("YSUyMGI=", new CodecOptions
    {
        Mode = CodecMode.Base64,
        Direction = CodecDirection.Decode,
        FollowUp = CodecMode.UrlComponent
    });
    return result.PrimaryText == "a b";
});
Check("二进制回退不抛异常", () =>
{
    CodecResult result = CodecEngine.Run("/w==", new CodecOptions { Mode = CodecMode.Base64, Direction = CodecDirection.Decode });
    return result.Views.Any(v => v.Title.Contains("Latin-1", StringComparison.Ordinal));
});
Fails("引擎异常转成 CodecException", () => CodecEngine.Run("###", new CodecOptions { Mode = CodecMode.Base64, Direction = CodecDirection.Decode }));

Console.WriteLine();
Console.WriteLine($"通过 {passed} 项，失败 {failed} 项。");
return failed == 0 ? 0 : 1;

void Index(string title)
{
    Console.WriteLine();
    Console.WriteLine($"--- {title} ---");
}

void Check(string name, Func<bool> condition)
{
    try
    {
        if (condition())
        {
            passed++;
            Console.WriteLine($"PASS  {name}");
        }
        else
        {
            failed++;
            Console.WriteLine($"FAIL  {name}");
        }
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

void Eq(string name, string actual, string expected)
    => Check(name, () =>
    {
        if (actual == expected)
        {
            return true;
        }

        Console.WriteLine($"        期望：{expected}");
        Console.WriteLine($"        实际：{actual}");
        return false;
    });

void Fails(string name, Action action)
    => Check(name, () =>
    {
        try
        {
            action();
            return false;
        }
        catch (CodecException)
        {
            return true;
        }
    });
