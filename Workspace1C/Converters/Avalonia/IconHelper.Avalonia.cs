#if LINUX
using System.Collections.Generic;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Workspace1C
{
    /// <summary>
    /// Вспомогательный класс для построения векторных иконок (Path + StreamGeometry)
    /// из ресурсов Icons.axaml. Используется вместо эмодзи в UI, собираемом в коде.
    /// Цвет иконки подписывается на ресурс-кисть темы, поэтому иконки автоматически
    /// перекрашиваются при переключении светлой/тёмной схемы.
    /// </summary>
    public static class IconHelper
    {
        /// <summary>
        /// Разрешает StreamGeometry по ключу из Icons.axaml. Резолв учитывает активный
        /// набор иконок (Задача 5): для Outline ищет ключ с суффиксом ".Outline"
        /// (IconsOutline.axaml), при отсутствии — бесшовный fallback на Fluent-геометрию
        /// (Icons.axaml), затем на запасную «папку» (как в IconKeyToGeometryConverter).
        /// </summary>
        public static Geometry? Geometry(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                key = "IconFolder";

            var set = Themes.ThemeFeatureMap.ParseIconSet(
                Application.Current is { } app0
                    && app0.TryGetResource(Themes.ThemeFeatureMap.IconSetNameKey, null, out var setName)
                        ? setName as string
                        : null);

            foreach (var candidate in Themes.ThemeFeatureMap.IconResolutionCandidates(key, set))
            {
                if (Application.Current is { } app &&
                    app.TryGetResource(candidate, null, out var res) && res is Geometry g)
                    return g;
            }

            return null;
        }

        /// <summary>
        /// Создаёт Path-иконку по ключу геометрии. Кисть Fill подписывается на ресурс
        /// <paramref name="brushKey"/> (например "TextPrimaryColorBrush"), поэтому цвет
        /// следует теме. По умолчанию используется кисть основного текста.
        /// </summary>
        /// <param name="subscriptions">
        /// Необязательный приёмник подписки на ресурс-кисть. Нужен там, где иконка
        /// живёт меньше приложения и пересоздаётся: без освобождения подписки
        /// накапливались бы на каждую пересборку.
        /// </param>
        public static Control MakeIcon(string key, double size = 16,
            string brushKey = "TextPrimaryColorBrush")
        {
            var icon = BuildIcon(key, size, out var path);

            if (Application.Current is not null)
            {
                // Динамический ресурс вместо ручной подписки: его отслеживает сам
                // элемент, и при выходе из дерева отслеживание снимается. Прежняя
                // подписка жила у приложения и держала элемент сильной ссылкой,
                // то есть укореняла всё окно, пока её не освободят вручную.
                Themes.ThemeBrushes.Bind(path, Avalonia.Controls.Shapes.Path.FillProperty, brushKey);
            }
            else
            {
                path.Fill = Brushes.Black;
            }

            return icon;
        }

        /// <summary>
        /// То же, но с готовой кистью: нужно там, где цвет задан числом
        /// (в разметке WPF такие значки покрашены явным Foreground).
        /// </summary>
        public static Control MakeIcon(string key, double size, IBrush brush)
        {
            var icon = BuildIcon(key, size, out var path);
            path.Fill = brush;
            return icon;
        }

        /// <summary>
        /// Issue #147: значок для кнопки — Fill привязан к Foreground ближайшей
        /// родительской кнопки, поэтому цвет совпадает с подписью и остаётся
        /// контрастным на акцентной заливке (AccentBrush) и при наведении
        /// (AccentHoverBrush) в любой теме и любом наборе иконок. Фоновый слой
        /// идёт за основным через общий observable Fill, поэтому тоже получает
        /// цвет подписи.
        /// </summary>
        public static Control MakeIconForButton(string key, double size = 16)
        {
            var icon = BuildIcon(key, size, out var path);
            BindFillToButtonForeground(path);
            return icon;
        }

        /// <summary>
        /// Issue #147: содержимое кнопки «значок + подпись», где Fill значка
        /// привязан к Foreground самой кнопки (единый цвет с подписью).
        /// </summary>
        public static Control IconAndTextForButton(string iconKey, string text, double iconSize = 16)
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    MakeIconForButton(iconKey, iconSize),
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                }
            };
        }

        /// <summary>
        /// Issue #147: подписывает основной слой значка на Foreground кнопки-предка.
        /// Вне кнопки привязка не разрешается — оставляем чёрный фолбэк, чтобы
        /// значок не пропадал вовсе.
        /// </summary>
        private static void BindFillToButtonForeground(Avalonia.Controls.Shapes.Path path)
        {
            path.Fill = Brushes.Black;
            if (Application.Current is not null)
            {
                path.Bind(
                    Avalonia.Controls.Shapes.Shape.FillProperty,
                    new Avalonia.Data.Binding(nameof(Button.Foreground))
                    {
                        RelativeSource = new Avalonia.Data.RelativeSource(
                            Avalonia.Data.RelativeSourceMode.FindAncestor)
                        {
                            AncestorType = typeof(Button)
                        }
                    });
            }
        }

        /// <summary>
        /// Контур кладётся в неизменный холст 24 на 24 и масштабируется целиком,
        /// как это делает PackIcon в версии для Windows. Без холста Stretch
        /// растягивал каждый значок по его собственным границам: значки с мелким
        /// контуром выглядели крупнее соседних и вставали по вертикали иначе.
        /// </summary>
        public static Control MakeIcon(string key, double size, out Avalonia.Controls.Shapes.Path path)
            => BuildIcon(key, size, out path);

        private static Control BuildIcon(string key, double size, out Avalonia.Controls.Shapes.Path path)
        {
            path = new Avalonia.Controls.Shapes.Path { Data = Geometry(key) };

            // Слои иконки (0.3.12.60): фоновый полупрозрачный (Duotone-набор Glass) — той же
            // кистью, что и основной; основной; «блик» (IconGlossBrush, у Windows XP — объёмный
            // градиент) — по контуру основного. Кисть и контур фона/блика следуют за основным
            // путём, поэтому код, который потом красит path.Fill, менять не нужно.
            var back = new Avalonia.Controls.Shapes.Path
            {
                Data = BackGeometry(key),
                Opacity = Themes.ThemeFeatureMap.BackLayerOpacity,
                IsHitTestVisible = false
            };
            back.Bind(Avalonia.Controls.Shapes.Shape.FillProperty,
                path.GetObservable(Avalonia.Controls.Shapes.Shape.FillProperty));
            var gloss = new Avalonia.Controls.Shapes.Path { IsHitTestVisible = false };
            gloss.Bind(Avalonia.Controls.Shapes.Path.DataProperty,
                path.GetObservable(Avalonia.Controls.Shapes.Path.DataProperty));
            if (Application.Current is not null)
                Themes.ThemeBrushes.Bind(gloss, Avalonia.Controls.Shapes.Shape.FillProperty,
                    Themes.ThemeFeatureMap.IconGlossBrushKey);

            var canvas = new Canvas { Width = IconViewportSize, Height = IconViewportSize };
            canvas.Children.Add(back);
            canvas.Children.Add(path);
            canvas.Children.Add(gloss);
            Track(path, back, key);
            return new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = canvas
            };
        }

        /// <summary>
        /// Фоновый слой иконки активного набора (ключ &lt;Ключ&gt;.Back.&lt;Набор&gt;) или null,
        /// если у набора слоя нет. В отличие от <see cref="Geometry"/> не откатывается на
        /// Fluent и «папку»: у базового набора фонового слоя нет.
        /// </summary>
        public static Geometry? BackGeometry(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || Application.Current is not { } app)
                return null;
            var set = Themes.IconSetState.Instance.Current;
            if (set == Themes.IconSet.Fluent)
                return null;
            var backKey = Themes.ThemeFeatureMap.IconKeyForSet(Themes.ThemeFeatureMap.BackLayerKey(key), set);
            return app.TryGetResource(backKey, null, out var res) && res is Geometry g ? g : null;
        }

        /// <summary>
        /// Меняет иконку уже построенного значка (например, статус базы): запоминает новый
        /// ключ, чтобы при смене темы значок перечитался из нового набора, и обновляет
        /// основной и фоновый слои. Используйте вместо прямого path.Data = Geometry(key).
        /// </summary>
        public static void SetIcon(Avalonia.Controls.Shapes.Path path, string key)
        {
            if (path is null)
                return;
            path.Data = Geometry(key);
            if (Tracked.TryGetValue(path, out var state))
            {
                state.Key = key;
                state.Back.Data = BackGeometry(key);
            }
        }

        /// <summary>
        /// Отключает автоматическое обновление значка при смене набора иконок — для путей,
        /// чей Data задан привязкой (её перекрыло бы обновление из IconHelper).
        /// </summary>
        public static void Untrack(Avalonia.Controls.Shapes.Path path)
        {
            if (path is not null && Tracked.TryGetValue(path, out var state))
                state.Key = null;
        }

        /// <summary>Состояние отслеживаемого значка: текущий ключ и фоновый слой.</summary>
        private sealed class TrackedIcon
        {
            public string? Key;
            public Avalonia.Controls.Shapes.Path Back = null!;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            Avalonia.Controls.Shapes.Path, TrackedIcon> Tracked = new();

        /// <summary>
        /// Подписывает значок на смену темы (0.3.12.60), пока он в визуальном дереве:
        /// при смене набора иконок контур перечитывается по запомненному ключу. Подписка
        /// снимается при откреплении, поэтому значки пересобираемых строк не укореняются.
        /// </summary>
        private static void Track(Avalonia.Controls.Shapes.Path path, Avalonia.Controls.Shapes.Path back, string key)
        {
            var state = new TrackedIcon { Key = key, Back = back };
            Tracked.AddOrUpdate(path, state);

            void OnThemeChanged(object? sender, EventArgs e)
            {
                if (state.Key is null)
                    return;
                path.Data = Geometry(state.Key);
                back.Data = BackGeometry(state.Key);
            }

            path.AttachedToVisualTree += (_, _) =>
            {
                Themes.ThemeManager.ThemeChanged -= OnThemeChanged;
                Themes.ThemeManager.ThemeChanged += OnThemeChanged;
                // Набор мог смениться, пока значок был вне дерева.
                OnThemeChanged(null, EventArgs.Empty);
            };
            path.DetachedFromVisualTree += (_, _) => Themes.ThemeManager.ThemeChanged -= OnThemeChanged;
        }

        /// <summary>Координатное поле контуров Icons.axaml, оно же viewport PackIcon.</summary>
        private const double IconViewportSize = 24;

        /// <summary>
        /// Строит содержимое кнопки «иконка + подпись» (горизонтальная панель).
        /// </summary>
        public static Control IconAndText(string iconKey, string text, double iconSize = 16,
            string brushKey = "TextPrimaryColorBrush")
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    MakeIcon(iconKey, iconSize, brushKey),
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                }
            };
        }

        /// <summary>
        /// Ключ иконки колонки списка баз. Единый источник истины для заголовков
        /// колонок и списка колонок на вкладке «Отображение», чтобы иконки в обоих
        /// местах совпадали. Неизвестные ключи (например пользовательские в настройках)
        /// получают иконку списка по умолчанию.
        /// </summary>
        public static string ColumnIconKey(string key) => key switch
        {
            "Version" => "IconInfo",
            "Configuration" => "IconCubeOutline",
            "LaunchMode" => "IconPlay",
            // В разметке у этой колонки значок Server, а не шестерёнка
            // (MainWindow.xaml:670): шестерёнка стоит у «Действий».
            "ServerBase" => "IconServer",
            "LastLaunch" => "IconRecent",
            "Size" => "IconDatabase",
            // «Последняя копия» (0.3.9.86) — значок резервной копии со стрелкой.
            "LastBackup" => "IconBackupRestore",
            "Actions" => "IconSettings",
            _ => "IconFormatTitle"
        };

    }
}
#endif