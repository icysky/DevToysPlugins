# ColorSchemer

Windows 桌面的取色 / 配色 / 色板工具，参考已停产的 ColorSchemer Studio 的操作习惯重做；
只用于日常选色与接口联调配色，不依赖原软件。

## 功能

- **基色编辑**：HEX、RGB、HSV 三种输入双向联动；滑块与文本框都可以改
- **色轮**：外圈色相环 + 内部 SV 方块，鼠标点击/拖拽实时取色
- **配色规则**：互补、邻近（±60°）、三角、分裂互补、四角、单色、明暗梯度、自定义角度
- **色板**：12 色相 × 5 明度的网格，点击任意色块设为基色
- **屏幕取色**：全屏冻结 + 跟随鼠标的放大镜（21×21 像素、8 倍放大），单击取中心像素，右键/Esc 取消
- **收藏**：常用颜色保存到 `%APPDATA%\ColorSchemer\favorites.json`，下次启动自动恢复
- **复制**：配色与收藏列表每行右侧的 ⧉ 会弹出复制菜单：
  `70C88B`（不带 #）、`#70C88B`、`112, 200, 139`、`hsl(158, 30%, 61%)`、`--color-1: #70C88B;`；
  点击左侧大色块可直接复制当前 HEX

## 使用

- 直接运行 `ColorSchemer.exe`（自包含单文件，不需要装 .NET）
- 取色时也可以按顶部「屏幕取色」，移动鼠标查看放大镜，左键确认

## 开发

```powershell
# 运行
dotnet run --project .\apps\ColorSchemer\ColorSchemer.csproj

# 颜色核心回归（纯逻辑）
powershell -ExecutionPolicy Bypass -File .\tools\ColorSchemer.Smoke\run.ps1

# 自包含单文件（输出到 publish\ColorSchemer\ColorSchemer.exe）
powershell -ExecutionPolicy Bypass -File .\apps\ColorSchemer\publish.ps1
```

- 目标框架：`net8.0-windows`（WPF）
- 目录：`Core/` 纯颜色逻辑（可单测）、`Controls/` 自绘色轮 / 色板 / 取色器、`Services/` 收藏持久化
- 取色使用 `System.Drawing.Common` 的 `CopyFromScreen`（仅 Windows），随单文件一起发布

## 已知限制

- 仅 Windows
- 多显示器混合 DPI 时，取色覆盖层按虚拟屏幕的 DIP 尺寸布局，极端缩放下放大镜位置可能略有偏移
- 没有原软件的 Variations / Mixer / Library，也暂不支持收藏拖放
