using System.ComponentModel.Composition;
using DevToys.Api;

namespace GmCrypto.DevToys;

/// <summary>四个工具共用一套资源（Strings.resx），因此只需要一个标识器。</summary>
[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(GmCryptoResourceAssemblyIdentifier))]
internal sealed class GmCryptoResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
        => ValueTask.FromResult(Array.Empty<FontDefinition>());
}
