using System.ComponentModel.Composition;
using DevToys.Api;

namespace JsFormat.DevToys;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(JsFormatResourceAssemblyIdentifier))]
internal sealed class JsFormatResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
        => ValueTask.FromResult(Array.Empty<FontDefinition>());
}
