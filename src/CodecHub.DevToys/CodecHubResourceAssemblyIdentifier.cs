using System.ComponentModel.Composition;
using DevToys.Api;

namespace CodecHub.DevToys;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(CodecHubResourceAssemblyIdentifier))]
internal sealed class CodecHubResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
        => ValueTask.FromResult(Array.Empty<FontDefinition>());
}
