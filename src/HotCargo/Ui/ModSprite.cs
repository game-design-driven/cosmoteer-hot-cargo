using Halfling;
using Halfling.Graphics;
using Halfling.ObjectText;
using HotCargo.Core;

namespace HotCargo.Ui;

/// <summary>Sprites of the mod's own icons, read like the vanilla icon nodes (for example ToolIcon) from a rules file of the mod.</summary>
static class ModSprite
{
    /// <summary>The sprite node <paramref name="name"/> of the rules file at <paramref name="file"/>, a path under the mod folder.</summary>
    public static ISprite Read(string file, string name)
    {
        string path = Path.Combine(Mod.ModRoot, file);
        return App.ObjectTextSerializer.ReadFromPath<ISprite>(new OTFile(path), name) ?? throw new InvalidDataException($"{name} missing in {path}");
    }
}
