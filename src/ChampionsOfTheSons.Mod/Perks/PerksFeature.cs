using ChampionsOfTheSons.Mod.Book;
using SonsSdk;

namespace ChampionsOfTheSons.Mod.Perks;

/// <summary>The perk key (Config.PerksKey, U) opens the guide book on its Perks page (COS-168: the standalone perk window is gone).</summary>
internal sealed class PerksFeature : IModFeature
{
    public void Init() => Config.PerksKey.Notify(TogglePerksPage);

    /// <summary>Opens the book on the Perks page, or closes it when that page is already showing.</summary>
    public static void TogglePerksPage()
    {
        if (BookHost.IsOpen && BookHost.Tab == BookTab.Perks) BookHost.Close();
        else BookHost.Open(BookTab.Perks);
    }

    public void Update(float dt, bool inWorld) { }

    public void RegisterSelfTests() => PerksSelfTest.Register();
}
