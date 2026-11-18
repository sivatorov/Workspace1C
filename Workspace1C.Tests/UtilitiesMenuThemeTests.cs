using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты темизации меню «Утилиты» (Задача 6): цвета пунктов/групп резолвятся из активной
/// схемы (<see cref="UtilitiesMenuCatalog.ResolveIconColor"/> и друзья) с fallback на
/// статичную палитру каталога, а WPF-ключи иконок (PackIcon) отображаются на единые
/// ключи геометрии Icon* (<see cref="UtilitiesMenuCatalog.WpfIconGeometryKey"/>), которые
/// резолвятся конвертером с учётом активного набора иконок. Чистая логика без UI.
/// </summary>
public sealed class UtilitiesMenuThemeTests
{
    // ======================= Цвета из активной схемы =======================

    [Theory]
    [InlineData("#3B82F6", "FolderColor")]
    [InlineData("#2D6CDF", "FolderColor")]
    [InlineData("#8B5CF6", "FavoriteFolderColor")]
    [InlineData("#F59E0B", "FavoriteColor")]
    [InlineData("#64748B", "TextSecondaryColor")]
    public void PaletteKeyForIconColor_KnownColor_ReturnsPaletteRole(string hex, string expectedKey)
    {
        // Статичный цвет каталога имеет роль в палитре схемы — при переключении темы
        // цвет берётся из активной схемы и перекрашивается на лету.
        Assert.Equal(expectedKey, UtilitiesMenuCatalog.PaletteKeyForIconColor(hex));
    }

    [Theory]
    [InlineData("#14B8A6")] // бирюзовый — в палитре схемы роли нет
    [InlineData("#22C55E")] // зелёный — нет
    [InlineData("#EF4444")] // красный — нет
    [InlineData("")]
    [InlineData(null)]
    public void PaletteKeyForIconColor_UnknownColor_ReturnsNull(string? hex)
    {
        Assert.Null(UtilitiesMenuCatalog.PaletteKeyForIconColor(hex));
    }

    [Fact]
    public void ResolveIconColor_FavoriteColor_FollowsSchemeVariant()
    {
        // Оранжевый #F59E0B в схеме — FavoriteColor: светлая палитра #F59E0B,
        // тёмная — #FBBF24. То есть переключение темы перекрашивает иконку.
        Assert.Equal("#F59E0B", UtilitiesMenuCatalog.ResolveIconColor("#F59E0B", ColorScheme.CreateLight(), dark: false));
        Assert.Equal("#FBBF24", UtilitiesMenuCatalog.ResolveIconColor("#F59E0B", ColorScheme.CreateDark(), dark: true));
    }

    [Fact]
    public void ResolveIconColor_TextSecondary_FollowsSchemeVariant()
    {
        // Ожидаемый цвет читаем из палитры схемы, а не литералом: аудит UI P0-1
        // затемнил TextSecondary в светлой теме до #475569, и тест не должен
        // требовать обновления при каждой правке контраста.
        var light = ColorScheme.CreateLight();
        var dark = ColorScheme.CreateDark();
        Assert.Equal(light.PaletteValue(false, "TextSecondaryColor"),
            UtilitiesMenuCatalog.ResolveIconColor("#64748B", light, dark: false));
        Assert.Equal(dark.PaletteValue(true, "TextSecondaryColor"),
            UtilitiesMenuCatalog.ResolveIconColor("#64748B", dark, dark: true));
    }

    [Fact]
    public void ResolveIconColor_FolderColor_FollowsSchemeVariant()
    {
        Assert.Equal("#2D6CDF", UtilitiesMenuCatalog.ResolveIconColor("#3B82F6", ColorScheme.CreateLight(), dark: false));
        Assert.Equal("#3B82F6", UtilitiesMenuCatalog.ResolveIconColor("#2D6CDF", ColorScheme.CreateDark(), dark: true));
    }

    [Fact]
    public void ResolveIconColor_FavoriteFolder_FollowsSchemeVariant()
    {
        Assert.Equal("#8B5CF6", UtilitiesMenuCatalog.ResolveIconColor("#8B5CF6", ColorScheme.CreateLight(), dark: false));
        Assert.Equal("#8B5CF6", UtilitiesMenuCatalog.ResolveIconColor("#8B5CF6", ColorScheme.CreateDark(), dark: true));
    }

    [Fact]
    public void ResolveIconColor_UnknownColor_StaysAsIs()
    {
        // Цвет без роли в палитре не меняется при переключении темы и при null-схеме.
        Assert.Equal("#14B8A6", UtilitiesMenuCatalog.ResolveIconColor("#14B8A6", ColorScheme.CreateDark(), dark: true));
        Assert.Equal("#14B8A6", UtilitiesMenuCatalog.ResolveIconColor("#14B8A6", null, dark: true));
        Assert.Equal(string.Empty, UtilitiesMenuCatalog.ResolveIconColor(null, ColorScheme.CreateLight(), dark: false));
    }

    [Fact]
    public void ResolveGroupIconColor_StandardGroups_ResolveFromScheme()
    {
        // Группа «Операции» (бирюзовый #06B6D4) роли в схеме не имеет — остаётся как есть;
        // группа «Опции блокировки» (фиолетовый #8B5CF6) — FavoriteFolderColor из схемы.
        Assert.Equal("#06B6D4", UtilitiesMenuCatalog.ResolveGroupIconColor(
            UtilitiesMenuCatalog.GroupOperations, ColorScheme.CreateDark(), dark: true));
        Assert.Equal("#8B5CF6", UtilitiesMenuCatalog.ResolveGroupIconColor(
            UtilitiesMenuCatalog.GroupLockOptions, ColorScheme.CreateLight(), dark: false));
    }

    [Fact]
    public void ResolveItemIconColor_CatalogItem_ResolvesFromScheme()
    {
        // «Выполнить сценарий резервирования» — зелёный #22C55E: роли нет, цвет стабилен.
        var backupItem = UtilitiesMenuCatalog.Find("RunBackupScenario");
        Assert.NotNull(backupItem);
        Assert.Equal("#22C55E", UtilitiesMenuCatalog.ResolveItemIconColor(backupItem!, ColorScheme.CreateDark(), dark: true));

        // «Учётные записи ИТС» — бирюзовый #14B8A6: роли нет.
        var itsItem = UtilitiesMenuCatalog.Find("ItsAccounts");
        Assert.NotNull(itsItem);
        Assert.Equal("#14B8A6", UtilitiesMenuCatalog.ResolveItemIconColor(itsItem!, ColorScheme.CreateLight(), dark: false));
    }

    // ======================= Ключи иконок (WPF → Icon*) =======================

    [Theory]
    [InlineData("Magnify", "IconSearch")]
    [InlineData("CloudDownload", "IconCloudDownload")]
    [InlineData("PackageVariant", "IconCloudDownload")]
    [InlineData("ServerNetwork", "IconServer")]
    [InlineData("Lock", "IconExitToApp")]
    [InlineData("Information", "IconInfo")]
    [InlineData("Tune", "IconTune")]
    [InlineData("NewBox", "IconNewBox")]
    public void WpfIconGeometryKey_PackIconKind_MapsToGeometryKey(string wpfKey, string expectedGeometryKey)
    {
        // WPF-меню «Утилиты» переведено на единый механизм геометрии (ResolveGeometry):
        // ключ PackIcon каталога отображается на парный ключ Icon* из каталога.
        Assert.Equal(expectedGeometryKey, UtilitiesMenuCatalog.WpfIconGeometryKey(wpfKey));
    }

    [Fact]
    public void WpfIconGeometryKey_GeometryKeyAlready_ReturnsAsIs()
    {
        // Ключи геометрии (Icon*) — возможное значение пользовательской компоновки —
        // возвращаются без изменений.
        Assert.Equal("IconSearch", UtilitiesMenuCatalog.WpfIconGeometryKey("IconSearch"));
        Assert.Equal("IconCloudDownload", UtilitiesMenuCatalog.WpfIconGeometryKey("IconCloudDownload"));
    }

    [Fact]
    public void WpfIconGeometryKey_UnknownKey_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, UtilitiesMenuCatalog.WpfIconGeometryKey("NoSuchPackIcon"));
        Assert.Equal(string.Empty, UtilitiesMenuCatalog.WpfIconGeometryKey(string.Empty));
        Assert.Equal(string.Empty, UtilitiesMenuCatalog.WpfIconGeometryKey(null));
    }

    [Fact]
    public void Catalog_EveryItemWithWpfIconKind_HasPairGeometryKey()
    {
        // Целостность маппинга: для каждого пункта каталога с WPF-иконкой WpfIconGeometryKey
        // совпадает с парным AvaloniaIconKey (единый источник пар — сам каталог).
        foreach (var item in UtilitiesMenuCatalog.All.Where(i => !string.IsNullOrWhiteSpace(i.WpfIconKind)))
        {
            Assert.False(string.IsNullOrWhiteSpace(item.AvaloniaIconKey),
                $"Пункт {item.Id}: WpfIconKind есть, а AvaloniaIconKey пуст");
            Assert.Equal(item.AvaloniaIconKey, UtilitiesMenuCatalog.WpfIconGeometryKey(item.WpfIconKind));
        }

        // Стандартные группы тоже имеют пару (тот же словарь).
        Assert.Equal(UtilitiesMenuCatalog.GroupAvaloniaIconKey(UtilitiesMenuCatalog.GroupInformation),
            UtilitiesMenuCatalog.WpfIconGeometryKey(UtilitiesMenuCatalog.GroupWpfIconKind(UtilitiesMenuCatalog.GroupInformation)));
        Assert.Equal(UtilitiesMenuCatalog.GroupAvaloniaIconKey(UtilitiesMenuCatalog.GroupOperations),
            UtilitiesMenuCatalog.WpfIconGeometryKey(UtilitiesMenuCatalog.GroupWpfIconKind(UtilitiesMenuCatalog.GroupOperations)));
    }

    [Fact]
    public void Catalog_EveryItemHasGeometryKeyOrNone()
    {
        // Пункты без иконки (например «Список типовых конфигураций») не должны получать
        // случайный мэппинг: WpfIconGeometryKey пустого ключа — пустой.
        Assert.Equal(string.Empty, UtilitiesMenuCatalog.WpfIconGeometryKey(string.Empty));
    }
}