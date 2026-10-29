using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoogleAuthenticator.DevToys.Core;

/// <summary>
/// 统一的 JSON 序列化设置。
/// </summary>
/// <remarks>
/// 这里刻意**不对外暴露任何 <c>System.Text.Json</c> 类型**（例如 <c>JsonSerializerOptions</c>）。
/// DevToys 加载插件时会反射扫描插件里所有类型的成员签名与特性，
/// 一旦签名里出现 <c>System.Text.Json</c> 的类型，宿主就会尝试解析
/// <c>System.Text.Json, Version=10.0.0.0</c>；而 DevToys 2.0.9 自带的是 8.0.0.0，于是插件加载失败。
/// 因此只暴露泛型帮助方法。
/// </remarks>
internal static class JsonConfig
{
    private static readonly JsonSerializerOptions CachedOptions = CreateOptions();

    public static byte[] SerializeToUtf8Bytes<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, CachedOptions);

    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json)
        => JsonSerializer.Deserialize<T>(utf8Json, CachedOptions);

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
