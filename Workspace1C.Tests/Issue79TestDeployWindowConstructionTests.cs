#if !LINUX
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Workspace1C;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #79 (v1.0.1.33, регрессия «ошибка при открытии интерфейса», diagnostics ZIP
/// 7OH: InvalidOperationException «Specified element is already the logical child of
/// another element» в TestDeployWindow..ctor на TestDeployWindow.cs:244):
/// WPF-окно «Развернуть тестовую копию» обязано строиться без исключений, поле
/// «Имя базы на сервере» (<c>_refBox</c>) входит в логическое дерево РОВНО один раз
/// (в v1.0.1.32 добавлялось дважды: в строку MakeFieldsRow и повторно в _serverPanel),
/// список «СУБД» — только 4 типа, реально поддерживаемых 1С (MSSQLServer, PostgreSQL,
/// IBMDB2, OracleDatabase — без MySQL/MariaDB), а ручной блок «Данные СУБД источника»
/// выровнен по тем же 4 типам.
///
/// Неизвестные значения СУБД из rac/профиля (в т.ч. legacy MySQL/MariaDB) не падают:
/// покрывается тестами <see cref="TestDeployDbmsOptionsTests"/> (BuildOptions добавляет
/// неизвестное значение последней опцией) — конструктор окна получает Dbms только из
/// VM по умолчанию.
/// Avalonia-версия окна собирается только под #if LINUX и покрывается source-гвардами
/// в <see cref="Issue79TestDeployWindowTests"/>.
/// </summary>
public sealed class Issue79TestDeployWindowConstructionTests
{
    private static Infobase MakeSource()
    {
        return new Infobase
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "УТ",
            PlatformVersion = "8.3.24",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv",
                DatabaseName = "ut_base"
            }
        };
    }

    /// <summary>Запускает действие в отдельном STA-потоке (требование WPF-окон).</summary>
    private static void RunInStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
    }

    private static void EnsureAppServices()
    {
        // Конструктор окна резолвит IDialogService из контейнера. В юнит-тестах контейнер
        // до этого не инициализируется (ни один тест его не читает), поэтому настройка
        // здесь безопасна и идемпотентна (повторный вызов не перезаписывает провайдер).
        if (AppServices.Services is null)
            AppServices.Configure();
    }

    /// <summary>Собирает все элементы типа T из логического дерева (LogicalTreeHelper).</summary>
    private static List<T> CollectLogical<T>(System.Windows.DependencyObject root)
        where T : System.Windows.DependencyObject
    {
        var result = new List<T>();
        Collect(root, result, new HashSet<System.Windows.DependencyObject>());
        return result;

        static void Collect(
            System.Windows.DependencyObject node, List<T> acc,
            HashSet<System.Windows.DependencyObject> visited)
        {
            if (!visited.Add(node))
                return; // защита от циклов; дубль в дереве ловится отдельной проверкой
            if (node is T typed)
                acc.Add(typed);
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node))
            {
                if (child is System.Windows.DependencyObject dep)
                    Collect(dep, acc, visited);
            }
        }
    }

    // ---------------- (а) Окно строится, _refBox в дереве один раз ----------------

    [Fact]
    public void WpfConstructor_DoesNotThrow_AndRefBoxIsInLogicalTreeExactlyOnce()
    {
        RunInStaThread(() =>
        {
            EnsureAppServices();
            var source = MakeSource();

            // Регрессия v1.0.1.32: двойное добавление _refBox бросало
            // InvalidOperationException прямо в конструкторе («ошибка при открытии
            // интерфейса» на Windows, Avalonia-версия не задвоена).
            var window = new TestDeployWindow(
                source, Array.Empty<Infobase>(), Array.Empty<Group>());

            // _refBox определяется по структуре строки MakeFieldsRow: подпись и поле
            // лежат в StackPanel-cell, cells — в Grid-строке. _refBox — TextBox в
            // СОСЕДНЕЙ ячейке строки поля «Сервер 1С» (TextBox с «srv»). По тексту
            // искать нельзя: поле «Имя базы данных СУБД» законно автозаполняется тем
            // же значением (issue #79).
            var serverBox = CollectLogical<System.Windows.Controls.TextBox>(window)
                .Single(b => b.Text == "srv");
            var serverCell = System.Windows.LogicalTreeHelper.GetParent(serverBox) as
                System.Windows.DependencyObject
                ?? throw new InvalidOperationException("Строка «Сервер 1С» не найдена в разметке");
            var row = System.Windows.LogicalTreeHelper.GetParent(serverCell) as
                System.Windows.DependencyObject
                ?? throw new InvalidOperationException("Строка «Сервер 1С» не найдена в разметке");
            var refBox = System.Windows.LogicalTreeHelper.GetChildren(row)
                .OfType<System.Windows.DependencyObject>()
                .SelectMany(cell => System.Windows.LogicalTreeHelper.GetChildren(cell)
                    .OfType<System.Windows.Controls.TextBox>())
                .Single(b => !ReferenceEquals(b, serverBox));

            // Полный обход дерева БЕЗ фильтра visited: считаем вхождения ИМЕННО этого
            // экземпляра (по ссылке) — их должно быть ровно 1. В v1.0.1.32 элемент
            // был добавлен в дерево дважды (MakeFieldsRow + повторно в _serverPanel).
            var occurrences = 0;
            Count(window, refBox, ref occurrences);
            Assert.Equal(1, occurrences);
            Assert.Equal("ut_base_t", refBox.Text);
        });
    }

    /// <summary>Считает вхождения заданного экземпляра в логическое дерево (по ссылке).</summary>
    private static void Count(
        System.Windows.DependencyObject node,
        System.Windows.DependencyObject target, ref int count)
    {
        if (ReferenceEquals(node, target))
            count++;
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node))
        {
            if (child is System.Windows.DependencyObject dep)
                Count(dep, target, ref count);
        }
    }

    // ---------------- (б) Состав списка СУБД в окне ----------------

    [Fact]
    public void WpfConstructor_DbmsComboContainsExactlyFourSupportedTypes()
    {
        RunInStaThread(() =>
        {
            EnsureAppServices();
            var source = MakeSource();
            var window = new TestDeployWindow(
                source, Array.Empty<Infobase>(), Array.Empty<Group>());

            var combo = CollectLogical<System.Windows.Controls.ComboBox>(window)
                .Single(c => c.ItemsSource is IEnumerable<TestDeployDbmsOption>);

            var values = ((IEnumerable<TestDeployDbmsOption>)combo.ItemsSource)
                .Select(o => o.Value).ToArray();

            // issue #79 (v1.0.1.33): только типы, реально поддерживаемые 1С для ИБ;
            // MySQL/MariaDB убраны (1С использует их только как веб-серверы публикации).
            Assert.Equal(
                new[] { "MSSQLServer", "PostgreSQL", "IBMDB2", "OracleDatabase" },
                values);
            Assert.DoesNotContain("MySQL", values);
            Assert.DoesNotContain("MariaDB", values);
        });
    }

    [Fact]
    public void WpfConstructor_SourceDbmsOverrideComboContainsExactlyFourTypes()
    {
        RunInStaThread(() =>
        {
            EnsureAppServices();
            var source = MakeSource();
            var window = new TestDeployWindow(
                source, Array.Empty<Infobase>(), Array.Empty<Group>());

            // Ручной блок «Данные СУБД источника»: строковый ComboBox выровнен по тем
            // же 4 типам (в v1.0.1.32 было только 2 — «MSSQLServer, PostgreSQL»).
            var stringCombos = CollectLogical<System.Windows.Controls.ComboBox>(window)
                .Where(c => c.ItemsSource is IEnumerable<TestDeployDbmsOption> == false
                            && c.ItemsSource is IEnumerable nonGeneric
                            && nonGeneric.OfType<string>().Any())
                .ToList();

            Assert.Contains(stringCombos, c =>
            {
                var values = ((IEnumerable)c.ItemsSource).OfType<string>().ToArray();
                return values.SequenceEqual(
                    new[] { "MSSQLServer", "PostgreSQL", "IBMDB2", "OracleDatabase" },
                    StringComparer.OrdinalIgnoreCase);
            });
        });
    }
}
#endif
