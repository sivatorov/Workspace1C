using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #128 «Действия при наведении», продолжение (v1.0.1.60, 7OH «для папок
/// подвиг повторить»): тот же «подвиг», что сделан для строк баз в v1.0.1.58–59,
/// применён к СТРОКАМ ГРУПП (папок) дерева:
/// <list type="bullet">
/// <item>при включённой настройке <c>AppSettings.ActionsHoverOnly</c> кнопки
/// колонки «Действия» строки группы («править»/«удалить») видны только когда
/// курсор над строкой группы — раньше у папок кнопки были видимы всегда
/// (7OH дважды жаловался: «у папок кнопки видимы до сих пор без наведения»);</item>
/// <item>каждая кнопка живёт в слоте постоянной ширины — появление/исчезновение
/// одной кнопки не сдвигает соседей и не дёргает раскладку ( Avalonia —
/// <c>FixedLaunchSlot</c>, WPF — <c>Visibility="Hidden"</c>);</item>
/// <item>«править» всегда первый слот, «удалить» второй; у служебных узлов
/// («Закреплённые»/«Без группы») кнопка удаления по-прежнему скрыта полностью;</item>
/// <item>у групп нет кнопок запуска и чипов — правило совпадает со служебной
/// кнопкой строки базы, для папок выделен отдельный метод чистого планировщика
/// <see cref="Workspace1C.Services.ActionsHoverPlanner.IsGroupActionVisible"/>;</item>
/// <item>при выключенной настройке — прежнее поведение, кнопки папок видны
/// всегда; при выключенной колонке «Действия» кнопок у папок нет вовсе.</item>
/// </list>
/// WPF-разметка и Avalonia-код проверяются source-гвардами (Avalonia собирается
/// только в Linux-конфигурации).
/// </summary>
public sealed class Issue128GroupRowHoverTests
{
    private static string ReadProjectFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }

    // ===================== Чистое правило видимости кнопок групп =====================

    [Theory]
    [InlineData(false, false, true)]   // опция выключена — как раньше, всегда видно
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]     // hover — видно
    [InlineData(true, false, false)]   // не hover — пусто («у папок кнопки видимы до сих пор» — исправлено)
    public void Planner_GroupActionVisibility_Matrix(
        bool hoverOnlyEnabled, bool rowHovered, bool expectedVisible)
    {
        Assert.Equal(expectedVisible,
            Workspace1C.Services.ActionsHoverPlanner.IsGroupActionVisible(hoverOnlyEnabled, rowHovered));
    }

    [Fact]
    public void Planner_GroupAction_NeverVisibleWithoutHoverWhenEnabled()
    {
        // Ключевое отличие от строк баз: у групп НЕТ чипов запуска, поэтому при
        // включённой настройке «без наведения» кнопки папки не видны никогда —
        // правило служебной кнопки, а не кнопки запуска.
        Assert.False(Workspace1C.Services.ActionsHoverPlanner.IsGroupActionVisible(
            hoverOnlyEnabled: true, rowHovered: false));
        Assert.True(Workspace1C.Services.ActionsHoverPlanner.IsGroupActionVisible(
            hoverOnlyEnabled: true, rowHovered: true));
    }

    // ===================== Avalonia (source-гварды) =====================

    [Fact]
    public void Avalonia_GroupRowButtons_BindHoverVisibility()
    {
        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        // Обе кнопки строки группы привязаны к чистому планировщику через
        // BindGroupActionsHoverVisibility (hover — по корню строки группы).
        Assert.Contains("BindGroupActionsHoverVisibility(editBtn, header)", tree, StringComparison.Ordinal);
        Assert.Contains("BindGroupActionsHoverVisibility(deleteBtn, header)", tree, StringComparison.Ordinal);
        // Метод существует и использует тот же конвертер-обёртку планировщика.
        Assert.Contains("private void BindGroupActionsHoverVisibility(Control button, Control rowRoot)", tree, StringComparison.Ordinal);
        var methodStart = tree.IndexOf("private void BindGroupActionsHoverVisibility(Control button, Control rowRoot)", StringComparison.Ordinal);
        var methodBody = tree[methodStart..];
        Assert.Contains("RowActionHoverVisibilityConverter()", methodBody, StringComparison.Ordinal);
        Assert.Contains("nameof(MainViewModel.ActionsHoverOnly)", methodBody, StringComparison.Ordinal);
        Assert.Contains("nameof(InputElement.IsPointerOver)", methodBody, StringComparison.Ordinal);
        // У групп нет запущенных режимов — третий вход конвертера всегда false.
        Assert.Contains("new Binding { Source = false }", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_GroupRowButtons_LiveInFixedSlots()
    {
        // Issue #128 (v1.0.1.59 → 1.0.1.60): каждая кнопка папки — в слоте
        // постоянной ширины, скрытая кнопка не освобождает слот и не сдвигает
        // соседей: «править» всегда первый слот, «удалить» второй.
        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("actions.Children.Add(FixedLaunchSlot(editBtn));", tree, StringComparison.Ordinal);
        Assert.Contains("actions.Children.Add(deleteSlot);", tree, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_GroupServiceNodes_DeleteHiddenViaSlot()
    {
        // Служебные узлы («Закреплённые»/«Без группы») без модели группы: кнопка
        // удаления скрыта. Проверка маркера живёт на СЛОТЕ (локальное значение
        // IsVisible), а привязка hover — на самой кнопке: привязка перекрыла бы
        // локальное значение, если бы оба правила жили на одном элементе.
        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("var deleteSlot = FixedLaunchSlot(deleteBtn);", tree, StringComparison.Ordinal);
        Assert.Contains("deleteSlot.IsVisible = group.Marker != GroupNodeViewModel.PinnedMarker", tree, StringComparison.Ordinal);
        Assert.Contains("&& group.Marker != GroupNodeViewModel.NoGroupMarker;", tree, StringComparison.Ordinal);
        // Прежний вариант «маркер на кнопке» больше не используется — иначе
        // привязка hover перекрыла бы локальное значение и кнопка всплывала бы
        // у служебных узлов при наведении.
        Assert.DoesNotContain("deleteBtn.IsVisible = group.Marker", tree, StringComparison.Ordinal);
    }

    // ===================== WPF-разметка (source-гварды) =====================

    [Fact]
    public void Wpf_GroupRowBorder_HasNameForHoverBindings()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        // Корневой Border строки группы поименован — на его IsMouseOver смотрят
        // триггеры кнопок «править»/«удалить» (аналог InfobaseRowBorder у баз).
        Assert.Contains("<Border x:Name=\"GroupRowBorder\" CornerRadius=\"6\">", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_GroupRowButtons_HaveHoverOnlyTriggersWithHidden()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // Блок команд группы в колонке «Действия» (StackPanel Grid.Column="7"
        // шаблона GroupRowGrid): обе кнопки скрываются по hover-only, и обе —
        // через Hidden (слот резервируется, иконка соседа не сдвигается).
        var groupActions = xaml.IndexOf("DataContext.EditGroupCommand, RelativeSource={RelativeSource AncestorType=Window}}\"", StringComparison.Ordinal);
        Assert.True(groupActions > 0, "кнопка «править» строки группы не найдена");
        var templateEnd = xaml.IndexOf("</HierarchicalDataTemplate>", groupActions, StringComparison.Ordinal);
        Assert.True(templateEnd > groupActions, "конец шаблона группы не найден");
        var block = xaml[groupActions..templateEnd];

        // Hover — по корневому Border строки группы (GroupRowBorder), а не по самой кнопке.
        Assert.Equal(2, CountOccurrences(block,
            "<Condition Binding=\"{Binding IsMouseOver, ElementName=GroupRowBorder}\" Value=\"False\"/>"));
        Assert.Equal(2, CountOccurrences(block,
            "<Condition Binding=\"{Binding DataContext.ActionsHoverOnly, RelativeSource={RelativeSource AncestorType=Window}}\" Value=\"True\"/>"));
        // Слоты резервируются: Hidden, Collapsed для hover-only не используется.
        Assert.Equal(2, CountOccurrences(block, "<Setter Property=\"Visibility\" Value=\"Hidden\"/>"));
    }

    [Fact]
    public void Wpf_GroupServiceNodes_DeleteStillCollapsed()
    {
        // Служебные узлы («Закреплённые»/«Без группы»): кнопка удаления скрыта
        // полностью (Collapsed). Триггеры маркеров объявлены ПОСЛЕ hover-only
        // триггера: WPF применяет последний сработавший триггер, поэтому у
        // служебных узлов побеждает Collapsed.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var deleteHover = xaml.IndexOf("DataContext.DeleteGroupCommand, RelativeSource={RelativeSource AncestorType=Window}}\"", StringComparison.Ordinal);
        Assert.True(deleteHover > 0, "кнопка «удалить» строки группы не найдена");
        var buttonEnd = xaml.IndexOf("</Button>", deleteHover, StringComparison.Ordinal);
        Assert.True(buttonEnd > deleteHover, "конец кнопки «удалить» не найден");
        var deleteBlock = xaml[deleteHover..buttonEnd];

        var multiTrigger = deleteBlock.IndexOf("<MultiDataTrigger>", StringComparison.Ordinal);
        var pinnedTrigger = deleteBlock.IndexOf("<DataTrigger Binding=\"{Binding Marker}\" Value=\"Pinned\">", StringComparison.Ordinal);
        var noGroupTrigger = deleteBlock.IndexOf("<DataTrigger Binding=\"{Binding Marker}\" Value=\"NoGroup\">", StringComparison.Ordinal);
        Assert.True(multiTrigger >= 0, "hover-only триггер кнопки «удалить» не найден");
        Assert.True(pinnedTrigger > multiTrigger, "триггер Pinned должен идти ПОСЛЕ hover-only триггера");
        Assert.True(noGroupTrigger > multiTrigger, "триггер NoGroup должен идти ПОСЛЕ hover-only триггера");
        Assert.Contains("<DataTrigger Binding=\"{Binding Marker}\" Value=\"Pinned\">", deleteBlock, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
