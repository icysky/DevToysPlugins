using DevToys.Api;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GoogleAuthenticator.Smoke;

/// <summary>只做本地文件操作、不弹系统对话框的 <see cref="IFileStorage" /> 替身。</summary>
internal sealed class FakeFileStorage : IFileStorage
{
    public string AppCacheDirectory => Path.GetTempPath();

    public ValueTask<FileStream?> PickSaveFileAsync(params string[] fileTypes)
        => ValueTask.FromResult<FileStream?>(null);

    public ValueTask<SandboxedFileReader?> PickOpenFileAsync(params string[] fileTypes)
        => ValueTask.FromResult<SandboxedFileReader?>(null);

    public ValueTask<SandboxedFileReader[]> PickOpenFilesAsync(params string[] fileTypes)
        => ValueTask.FromResult(Array.Empty<SandboxedFileReader>());

    public ValueTask<string?> PickFolderAsync() => ValueTask.FromResult<string?>(null);

    public bool FileExists(string relativeOrAbsoluteFilePath) => File.Exists(relativeOrAbsoluteFilePath);

    public FileStream OpenReadFile(string relativeOrAbsoluteFilePath) => File.OpenRead(relativeOrAbsoluteFilePath);

    public FileStream OpenWriteFile(string relativeOrAbsoluteFilePath, bool replaceIfExist)
        => replaceIfExist
            ? File.Create(relativeOrAbsoluteFilePath)
            : new FileStream(relativeOrAbsoluteFilePath, FileMode.CreateNew);

    public FileInfo CreateSelfDestroyingTempFile(string? desiredFileExtension = null)
        => new(Path.GetTempFileName());
}

/// <summary>记录写入内容的 <see cref="IClipboard" /> 替身。</summary>
internal sealed class FakeClipboard : IClipboard
{
    public string? LastCopiedText { get; private set; }

    public Task<object?> GetClipboardDataAsync() => Task.FromResult<object?>(null);

    public Task<string?> GetClipboardTextAsync() => Task.FromResult(LastCopiedText);

    public Task<FileInfo[]?> GetClipboardFilesAsync() => Task.FromResult<FileInfo[]?>(null);

    public Task<Image<Rgba32>?> GetClipboardImageAsync() => Task.FromResult<Image<Rgba32>?>(null);

    public Task SetClipboardTextAsync(string? data)
    {
        LastCopiedText = data;
        return Task.CompletedTask;
    }

    public Task SetClipboardFilesAsync(FileInfo[]? filePaths) => Task.CompletedTask;

    public Task SetClipboardImageAsync(Image? image) => Task.CompletedTask;
}
