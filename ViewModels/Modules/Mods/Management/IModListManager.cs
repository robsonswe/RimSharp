using RimSharp.Models;
using System;
using System.Collections.Generic;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
public interface IModListManager
{
    IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods { get; }
    IReadOnlyList<ModItem> AllInactiveMods { get; }
    event EventHandler ListChanged;
    void Initialize(IEnumerable<ModItem> allAvailableMods, IEnumerable<string> activeModPackageIds);
    void ActivateMod(ModItem mod);
    void ActivateModAt(ModItem mod, int index);
    void DeactivateMod(ModItem mod);
    void ReorderMod(ModItem mod, int newIndex);
    void ClearActiveList();
    bool SortActiveList();
    
    // Add these new methods
    bool IsModActive(ModItem mod);
    IEnumerable<ModItem> GetAllMods();
}
}
