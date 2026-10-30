using System;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Юнит-тесты хранилища/расчёта ширины колонок вкладок окна «Серверы 1С» (issue #75):
/// минимальная ширина по содержимому заголовка (худший случай ru/en + стрелка сортировки),
/// сохранение/восстановление по ключам вкладок (изоляция друг от друга), нормализация
/// сохранённых значений (NaN/0/меньше минимума → дефолт/минимум).
/// </summary>
public sealed class ServerMonitorColumnWidthsTests
{
    // ===================== Расчёт минимальной ширины заголовка =====================

    [Fact]
    public void HeaderMinWidth_EmptyHeaders_ReturnsAbsoluteMin()
    {
        // Служебные колонки-глифы («●», «PID» без локализованного заголовка).
        Assert.Equal(ServerMonitorColumnWidthsStore.AbsoluteMinWidth, ServerMonitorColumnWidthsStore.HeaderMinWidth("", ""));
        Assert.Equal(ServerMonitorColumnWidthsStore.AbsoluteMinWidth, ServerMonitorColumnWidthsStore.HeaderMinWidth(null, null));
    }

    [Fact]
    public void HeaderMinWidth_UsesWorstCaseLocaleRuEn()
    {
        // «Информационная база» (19 симв.) против «Infobase» (8): берётся РУССКИЙ вариант.
        var width = ServerMonitorColumnWidthsStore.HeaderMinWidth("Информационная база", "Infobase");
        var expected = Math.Ceiling(19 * ServerMonitorColumnWidthsStore.CharWidthFactor)
                       + ServerMonitorColumnWidthsStore.HeaderReserve;
        Assert.Equal(expected, width);

        // «Application» (11 симв.) против «Приложение» (10): берётся АНГЛИЙСКИЙ вариант.
        var width2 = ServerMonitorColumnWidthsStore.HeaderMinWidth("Приложение", "Application");
        var expected2 = Math.Ceiling(11 * ServerMonitorColumnWidthsStore.CharWidthFactor)
                        + ServerMonitorColumnWidthsStore.HeaderReserve;
        Assert.Equal(expected2, width2);
    }

    [Fact]
    public void HeaderMinWidth_NeverBelowAbsoluteMin()
    {
        var width = ServerMonitorColumnWidthsStore.HeaderMinWidth("ИБ", "IBs");
        Assert.True(width >= ServerMonitorColumnWidthsStore.AbsoluteMinWidth);
        Assert.True(width > 0);
    }

    // ===================== Сохранение/восстановление через AppSettings =====================

    [Fact]
    public void Save_Then_TryLoad_RoundTripsWidths()
    {
        var settings = new AppSettings();
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.SessionsKey,
            new[] { 123.0, 175.0, 250.5 });

        var loaded = ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.SessionsKey);
        Assert.NotNull(loaded);
        Assert.Equal(new[] { 123.0, 175.0, 250.5 }, loaded);
    }

    [Fact]
    public void TryLoad_MissingTabKey_ReturnsNull()
    {
        var settings = new AppSettings();
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.SessionsKey, new double[] { 100 });

        Assert.Null(ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.ConnectionsKey));
        Assert.Null(ServerMonitorColumnWidthsStore.TryLoad(null, ServerMonitorColumnWidthsStore.SessionsKey));
    }

    [Fact]
    public void Save_IsolatesTabs_ByTabKey()
    {
        // issue #75: ширина колонок сохраняется по вкладкам независимо — изменение
        // колонок «Сеансы» не влияет на «Соединения» и наоборот.
        var settings = new AppSettings();
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.SessionsKey,
            new[] { 123.0, 175.0 });
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.ConnectionsKey,
            new[] { 200.0 });

        var sessions = ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.SessionsKey);
        var connections = ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.ConnectionsKey);
        Assert.Equal(new[] { 123.0, 175.0 }, sessions);
        Assert.Equal(new[] { 200.0 }, connections);
    }

    [Fact]
    public void Save_NormalizesInvalidValues_ToZero()
    {
        // Некорректные значения (NaN/0/отрицательные для «не сохранено») записываются нулём.
        var settings = new AppSettings();
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.ProcessesKey,
            new[] { double.NaN, 0.0, -5.0, 120.0 });

        var loaded = ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.ProcessesKey);
        Assert.Equal(new[] { 0.0, 0.0, 0.0, 120.0 }, loaded);
    }

    [Fact]
    public void Save_OverwritesExistingEntry_ForSameTabKey()
    {
        var settings = new AppSettings();
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.LocksKey,
            new[] { 100.0, 200.0 });
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.LocksKey,
            new[] { 150.0 });

        var loaded = ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.LocksKey);
        Assert.Equal(new[] { 150.0 }, loaded);
    }

    [Fact]
    public void NormalizeForLoad_InitializesMissingDictionary()
    {
        // Легаси-конфиг без нового поля: NormalizeForLoad не допускает NullReference
        // при обращении к ServerMonitorColumnWidths (issue #64 / issue #75).
        var settings = new AppSettings { ServerMonitorColumnWidths = null! };
        settings.NormalizeForLoad();
        Assert.NotNull(settings.ServerMonitorColumnWidths);
    }

    // ===================== Применение сохранённых/дефолтных ширин =====================

    [Fact]
    public void ResolveWidths_NoSavedEntry_ReturnsDefaults()
    {
        var defaults = new[] { 110.0, 150.0, 123.0 };
        var mins = new[] { 110.0, 130.0, 123.0 };

        var result = ServerMonitorColumnWidthsStore.ResolveWidths(null, defaults, mins);
        Assert.Equal(new[] { 110.0, 150.0, 123.0 }, result);
    }

    [Fact]
    public void ResolveWidths_SavedBelowMin_IsRaisedToMin()
    {
        // issue #75: заголовок и стрелка сортировки не должны обрезаться даже
        // сохранённой (зауженной) шириной.
        var defaults = new[] { 110.0 };
        var mins = new[] { 123.0 };
        var result = ServerMonitorColumnWidthsStore.ResolveWidths(new[] { 50.0 }, defaults, mins);
        Assert.Equal(new[] { 123.0 }, result);
    }

    [Fact]
    public void ResolveWidths_SavedNan_IsReplacedByDefault()
    {
        var defaults = new[] { 110.0, 150.0 };
        var mins = new[] { 110.0, 130.0 };
        var result = ServerMonitorColumnWidthsStore.ResolveWidths(new[] { double.NaN, 200.0 }, defaults, mins);
        Assert.Equal(new[] { 110.0, 200.0 }, result);
    }

    [Fact]
    public void ResolveWidths_SavedShorterThanColumns_FallsBackForMissing()
    {
        var defaults = new[] { 110.0, 150.0, 123.0 };
        var mins = new[] { 110.0, 130.0, 123.0 };
        var result = ServerMonitorColumnWidthsStore.ResolveWidths(new[] { 300.0 }, defaults, mins);
        Assert.Equal(new[] { 300.0, 150.0, 123.0 }, result);
    }

    [Fact]
    public void AllTabKeys_Present()
    {
        Assert.Contains(ServerMonitorColumnWidthsStore.SessionsKey, ServerMonitorColumnWidthsStore.TabKeys);
        Assert.Contains(ServerMonitorColumnWidthsStore.ConnectionsKey, ServerMonitorColumnWidthsStore.TabKeys);
        Assert.Contains(ServerMonitorColumnWidthsStore.LocksKey, ServerMonitorColumnWidthsStore.TabKeys);
        Assert.Contains(ServerMonitorColumnWidthsStore.ProcessesKey, ServerMonitorColumnWidthsStore.TabKeys);
    }

    // ===================== Перетаскивание границы как в 1С (issue #75, 1.0.0.96) =====================

    [Fact]
    public void MovedLeftColumnWidth_FollowsDelta_AndNeverBelowMin()
    {
        // Обычное перетаскивание: граница двигается — колонки справа едут как блок,
        // левой колонке достаётся изменение Δ ширины (минимум соблюдён).
        Assert.Equal(220.0, ServerMonitorColumnWidthsStore.MovedLeftColumnWidth(200.0, 20.0, 100.0));
        Assert.Equal(180.0, ServerMonitorColumnWidthsStore.MovedLeftColumnWidth(200.0, -20.0, 100.0));
        // Упор в минимум при сжатии: дальше граница не двигается.
        Assert.Equal(100.0, ServerMonitorColumnWidthsStore.MovedLeftColumnWidth(200.0, -250.0, 100.0));
        Assert.Equal(100.0, ServerMonitorColumnWidthsStore.MovedLeftColumnWidth(100.0, 0.0, 100.0));
    }

    [Fact]
    public void ResizedTwoAdjacentWidths_KeepsPairTotal_AndMins()
    {
        // Ctrl: меняется только граница двух смежных колонок, сумма пары постоянна.
        var (left, right) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(200.0, 300.0, 50.0, 100.0, 100.0);
        Assert.Equal(250.0, left);
        Assert.Equal(250.0, right);
        Assert.Equal(500.0, left + right);

        // Влево: левая ужимается, правая растёт.
        var (left2, right2) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(200.0, 300.0, -80.0, 100.0, 100.0);
        Assert.Equal(120.0, left2);
        Assert.Equal(380.0, right2);
        Assert.Equal(500.0, left2 + right2);
    }

    [Fact]
    public void ResizedTwoAdjacentWidths_StopsAtMins_OfBothColumns()
    {
        // Левая упёрлась в минимум: правая больше не растёт (сумма пары постоянна).
        var (left, right) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(150.0, 200.0, -1000.0, 120.0, 90.0);
        Assert.Equal(120.0, left);
        Assert.Equal(230.0, right);
        Assert.Equal(350.0, left + right);

        // Правая упёрлась в минимум: левая больше не растёт.
        var (left2, right2) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(150.0, 200.0, 1000.0, 120.0, 90.0);
        Assert.Equal(260.0, left2);
        Assert.Equal(90.0, right2);
        Assert.Equal(350.0, left2 + right2);
    }

    [Fact]
    public void ResizedTwoAdjacentWidths_MinimalPair_NeverBelowMins()
    {
        // Обе колонки уже на минимуме: граница не двигается ни в одну сторону.
        var (left, right) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(100.0, 90.0, 25.0, 100.0, 90.0);
        Assert.Equal(100.0, left);
        Assert.Equal(90.0, right);
        var (left2, right2) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(100.0, 90.0, -25.0, 100.0, 90.0);
        Assert.Equal(100.0, left2);
        Assert.Equal(90.0, right2);
    }

    // ===================== Эмуляция перетаскивания границы (issue #75) =====================
    // Тесты «реального сценария»: обычный drag сдвигает ВСЕ колонки справа как блок
    // (их ширины не меняются, меняется только позиция; при переполнении области
    // появляется горизонтальная прокрутка), Ctrl-drag меняет только ДВЕ смежные колонки.

    [Fact]
    public void ApplyDragMoveBlock_DragRight_OnlyLeftColumnGrows_AllRightColumnsUnchanged()
    {
        // Обычное перетаскивание границы колонки 1 вправо на 40 px (issue #75):
        // левая колонка растёт на Δ, ВСЕ колонки справа сохраняют ширины (двигаются
        // как блок — при переполнении области появится горизонтальная прокрутка).
        var start = new[] { 100.0, 150.0, 200.0, 120.0 };
        var mins = new[] { 80.0, 120.0, 130.0, 90.0 };

        var result = ServerMonitorColumnWidthsStore.ApplyDragMoveBlock(start, index: 1, delta: 40.0, mins);

        Assert.Equal(new[] { 100.0, 190.0, 200.0, 120.0 }, result);
        // Сдвиг «как блок»: у колонок справа от границы ширины НЕ изменились.
        Assert.Equal(new[] { 200.0, 120.0 }, result.Skip(2));
        // Суммарная ширина таблицы выросла — появляется место для горизонтальной прокрутки.
        Assert.True(result.Sum() > start.Sum());
    }

    [Fact]
    public void ApplyDragMoveBlock_DragLeft_StopsAtMinWidth()
    {
        // Перетаскивание влево: колонка 1 ужимается, но НЕ ниже минимума «заголовок +
        // стрелка сортировки» (120) — граница дальше не идёт, остальные ширины не меняются.
        var start = new[] { 100.0, 150.0, 200.0, 120.0 };
        var mins = new[] { 80.0, 120.0, 130.0, 90.0 };

        var result = ServerMonitorColumnWidthsStore.ApplyDragMoveBlock(start, index: 1, delta: -1000.0, mins);

        Assert.Equal(new[] { 100.0, 120.0, 200.0, 120.0 }, result);
    }

    [Fact]
    public void ApplyDragResizePair_Ctrl_OnlyTwoAdjacentColumnsChange_PairTotalKept()
    {
        // Ctrl-перетаскивание границы колонки 1 вправо на 50 px (issue #75): меняются
        // ТОЛЬКО колонки 1 (растёт) и 2 (ужимается зеркально); колонки 0 и 3 — на месте;
        // сумма ширины пары постоянна.
        var start = new[] { 100.0, 150.0, 200.0, 120.0 };
        var mins = new[] { 80.0, 120.0, 130.0, 90.0 };

        var result = ServerMonitorColumnWidthsStore.ApplyDragResizePair(start, index: 1, delta: 50.0, mins);

        Assert.Equal(new[] { 100.0, 200.0, 150.0, 120.0 }, result);
        Assert.Equal(150.0 + 200.0, result[1] + result[2]);
        Assert.Equal(start[0], result[0]);
        Assert.Equal(start[3], result[3]);
    }

    [Fact]
    public void ApplyDragResizePair_Ctrl_StopsAtMins_OfBothAdjacentColumns()
    {
        // Правая колонка 2 упёрлась в минимум 130: левая колонка 1 больше не растёт.
        var start = new[] { 100.0, 150.0, 200.0, 120.0 };
        var mins = new[] { 80.0, 120.0, 130.0, 90.0 };

        var result = ServerMonitorColumnWidthsStore.ApplyDragResizePair(start, index: 1, delta: 1000.0, mins);

        Assert.Equal(220.0, result[1]); // 150 + (200 - 130) = 220
        Assert.Equal(130.0, result[2]);
        Assert.Equal(new[] { 100.0, 220.0, 130.0, 120.0 }, result);

        // Левая колонка 1 упёрлась в минимум 120: правая больше не растёт.
        var result2 = ServerMonitorColumnWidthsStore.ApplyDragResizePair(start, index: 1, delta: -1000.0, mins);
        Assert.Equal(new[] { 100.0, 120.0, 230.0, 120.0 }, result2);
    }

    [Fact]
    public void ApplyDragMoveBlock_WithMinimalWidths_KeepsColumnsAfterDeltaFixed()
    {
        // Эмуляция «перетаскивание границы двигает ВСЕ колонки справа как блок»:
        // при любом Δ (и вправо, и влево) колонки справа от границы не меняются,
        // а при переполнении области появляется горизонтальная прокрутка.
        var start = new[] { 90.0, 110.0, 130.0, 95.0 };
        var mins = new[] { 60.0, 100.0, 120.0, 60.0 };

        var right = ServerMonitorColumnWidthsStore.ApplyDragMoveBlock(start, index: 0, delta: 75.0, mins);
        var left = ServerMonitorColumnWidthsStore.ApplyDragMoveBlock(start, index: 0, delta: -20.0, mins);

        Assert.Equal(new[] { 165.0, 110.0, 130.0, 95.0 }, right);
        Assert.Equal(new[] { 70.0, 110.0, 130.0, 95.0 }, left);
    }

    // ===================== Определение границы по координате X (issue #75) =====================

    [Fact]
    public void ResolveDragBoundary_ClickAtJointOfHeaders_ResolvesLeftColumn_NotOffByOne()
    {
        // issue #75 (репорт 7OH): ширины A=120, B=150, C=130. Граница A|B — x=120,
        // граница B|C — x=270. Курсор «двойная стрелка» на стыке (где именно — на
        // правой кромке A или на левой кромке B/выступе ручки) ДОЛЖЕН давать
        // границу A|B (index=0) в ОБОИХ случаях — раньше левая кромка B давала
        // off-by-one (index=1, двигалась граница B|C).
        var widths = new[] { 120.0, 150.0, 130.0 };

        Assert.Equal(0, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 120.0, tolerance: 12));
        Assert.Equal(0, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 126.0, tolerance: 12)); // левая кромка B
        Assert.Equal(0, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 114.0, tolerance: 12)); // правая зона A
        Assert.Equal(1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 270.0, tolerance: 12));
        Assert.Equal(1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 276.0, tolerance: 12)); // левая кромка C
        // Выступ ручки за правый край последней колонки — граница B|C (index=1).
        Assert.Equal(2, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 400.0, tolerance: 12));
    }

    [Fact]
    public void ResolveDragBoundary_ClickInHeaderMiddle_DoesNotStartDrag()
    {
        // Поведение «палец» (issue #75): клик в центре заголовка — не у границы,
        // драг не стартует (сортировка/без действия).
        var widths = new[] { 120.0, 150.0, 130.0 };

        Assert.Equal(-1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 60.0, tolerance: 12));
        Assert.Equal(-1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 195.0, tolerance: 12));
        Assert.Equal(-1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 340.0, tolerance: 12));
    }

    [Fact]
    public void ResolveDragBoundary_NearestBoundaryWins_WhenBetweenTwo()
    {
        // Между границами ближе та, к которой клик; при равном расстоянии — левая
        // (первая найденная). Нулевые ширины не участвуют в накоплении.
        var widths = new[] { 100.0, 100.0, 100.0 };

        // x=155: до границы 0 (x=100) — 55, до границы 1 (x=200) — 45 → граница 1.
        Assert.Equal(1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 155.0, tolerance: 60));
        // x=145: до границы 0 — 45, до границы 1 — 55 → граница 0.
        Assert.Equal(0, ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x: 145.0, tolerance: 60));
    }

    [Fact]
    public void ResolveDragBoundary_InvalidInput_ReturnsMinusOne()
    {
        // Ширины ещё не измерены (NaN/0) — определение по координате невозможно;
        // пустой список и NaN-координата — тоже промах.
        Assert.Equal(-1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(Array.Empty<double>(), 100.0, 12));
        Assert.Equal(-1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(new[] { 120.0, double.NaN, 130.0 }, 100.0, 12));
        Assert.Equal(-1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(new[] { 120.0, 0.0, 130.0 }, 100.0, 12));
        Assert.Equal(-1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(new[] { 120.0, 150.0 }, double.NaN, 12));
        Assert.Equal(-1, ServerMonitorColumnWidthsStore.ResolveDragBoundary(new[] { 120.0, 150.0 }, double.PositiveInfinity, 12));
    }
}
