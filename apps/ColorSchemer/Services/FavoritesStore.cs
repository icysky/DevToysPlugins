using System.IO;
using System.Text.Json;

namespace ColorSchemer.Services;

/// <summary>收藏颜色持久化到 %APPDATA%\ColorSchemer\favorites.json。</summary>
public sealed class FavoritesStore
{
    private readonly string _path;

    public FavoritesStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ColorSchemer",
            "favorites.json");
    }

    public List<string> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<string> hexColors)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(hexColors.Distinct().ToList()));
        }
        catch
        {
            // 收藏保存失败不影响主流程
        }
    }
}
