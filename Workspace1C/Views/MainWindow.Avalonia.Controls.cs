#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Workspace1C.Controls;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Вспомогательные вложенные UI-типы главного окна (Avalonia/Linux): кнопки
    /// с состояниями из темы, наблюдатели ресурсов и собственные кнопки управления
    /// окном. Вынесены из монолита MainWindow.Avalonia.cs по ответственности.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Панель кнопок колонки «Действия»: раскладывает по горизонтали столько
        /// кнопок, сколько помещается в колонку, остальные не показывает вовсе.
        /// Обычная панель с обрезкой оставляла бы половину значка у самой границы
        /// колонки «Сервер/База», а в версии для Windows лишние значки пропадают.
        /// </summary>
        private sealed class ActionsPanel : Panel
        {
            /// <summary>Зазор между кнопками.</summary>
            public double Spacing { get; init; }

            protected override Size MeasureOverride(Size availableSize)
            {
                double width = 0, height = 0;
                foreach (var child in Children)
                {
                    child.Measure(Size.Infinity);
                    width += child.DesiredSize.Width + Spacing;
                    height = Math.Max(height, child.DesiredSize.Height);
                }
                return new Size(Math.Min(width, availableSize.Width), height);
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                double total = 0;
                var fit = 0;
                foreach (var child in Children)
                {
                    var next = total + child.DesiredSize.Width + (fit > 0 ? Spacing : 0);
                    if (next > finalSize.Width)
                        break;
                    total = next;
                    fit++;
                }

                var x = Math.Max(0, (finalSize.Width - total) / 2);
                for (var i = 0; i < Children.Count; i++)
                {
                    var child = Children[i];
                    if (i >= fit)
                    {
                        child.Arrange(new Rect(0, 0, 0, 0));
                        continue;
                    }
                    if (i > 0)
                        x += Spacing;
                    var size = child.DesiredSize;
                    child.Arrange(new Rect(x, Math.Max(0, (finalSize.Height - size.Height) / 2), size.Width, size.Height));
                    x += size.Width;
                }
                return finalSize;
            }
        }

        /// <summary>Освобождение по вызову действия: снятие подписки на событие модели.</summary>
        private sealed class ActionDisposable : IDisposable
        {
            private Action? _dispose;

            public ActionDisposable(Action dispose) => _dispose = dispose;

            public void Dispose()
            {
                var action = _dispose;
                _dispose = null;
                action?.Invoke();
            }
        }

        /// <summary>
        /// Кнопка-панель со скруглением и состояниями «обычное / hover / pressed»,
        /// кисти которых берутся из ресурсов темы (перекрашиваются при смене схемы).
        /// Используется для primary- и secondary-кнопок правой панели.
        /// </summary>
        private sealed class PanelButton : Button
        {
            private IBrush _baseBg = Brushes.Transparent;
            private IBrush _hoverBg = Brushes.Transparent;
            private IBrush _pressedBg = Brushes.Transparent;
            private IBrush _border = Brushes.Transparent;
            private IBrush _accent = Brushes.Transparent;
            private CornerRadius _radius;
            private bool _hovered;
            private bool _pressed;
            private bool _focused;

            public PanelButton(string baseBgKey, string hoverBgKey, string pressedBgKey, string borderKey, CornerRadius? cornerRadius = null)
            {
                _radius = cornerRadius ?? new CornerRadius(UiMetrics.RadiusLg);
                HorizontalContentAlignment = HorizontalAlignment.Center;
                VerticalContentAlignment = VerticalAlignment.Center;
                Padding = new Thickness(UiMetrics.ButtonPadH, UiMetrics.ButtonPadV);
                BorderThickness = new Thickness(1);
                Cursor = new Cursor(StandardCursorType.Hand);

                // Кастомный шаблон: скруглённый Border + ContentPresenter (без Fluent-хрома).
                Theme = new ControlTheme(typeof(Button))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<PanelButton>((_, _) =>
                    {
                        var border = new Border { CornerRadius = _radius, BorderThickness = new Thickness(1) };
                        border[!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
                        border[!Border.BorderBrushProperty] = new TemplateBinding(TemplatedControl.BorderBrushProperty);
                        border[!Border.BorderThicknessProperty] = new TemplateBinding(TemplatedControl.BorderThicknessProperty);
                        border[!Border.PaddingProperty] = new TemplateBinding(TemplatedControl.PaddingProperty);
                        UiMetrics.AddBrushTransition(border);

                        var presenter = new ContentPresenter();
                        presenter[!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty);
                        presenter[!ContentPresenter.HorizontalContentAlignmentProperty] = new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty);
                        presenter[!ContentPresenter.VerticalContentAlignmentProperty] = new TemplateBinding(ContentControl.VerticalContentAlignmentProperty);
                        border.Child = presenter;
                        return border;
                    }))
                    }
                };

                Subscribe(baseBgKey, v => _baseBg = v);
                Subscribe(hoverBgKey, v => _hoverBg = v);
                Subscribe(pressedBgKey, v => _pressedBg = v);
                Subscribe(borderKey, v => _border = v);
                Subscribe("AccentBrush", v => _accent = v);

                PointerEntered += (_, _) => { _hovered = true; ApplyState(); };
                PointerExited += (_, _) => { _hovered = false; _pressed = false; ApplyState(); };
                PointerPressed += (_, _) => { _pressed = true; ApplyState(); };
                PointerReleased += (_, _) => { _pressed = false; ApplyState(); };
                PointerCaptureLost += (_, _) => { _pressed = false; ApplyState(); };

                this.GetObservable(IsEnabledProperty).Subscribe(new BoolObserver(_ => ApplyState()));
                this.GetObservable(IsKeyboardFocusWithinProperty).Subscribe(new BoolObserver(v => { _focused = v; ApplyState(); }));
                ApplyState();
            }

            private void Subscribe(string key, Action<IBrush> setter)
            {
                // Пустой ключ означает «прозрачно»: у автора значковые кнопки
                // верхней панели без подложки и без рамки, подсветка только
                // при наведении.
                if (string.IsNullOrEmpty(key))
                    return;
                // Подписка снимается вместе с уходом кнопки из дерева: список
                // _subs не освобождался нигде, и каждая пересборка правой панели
                // оставляла кнопку и всё её дерево укоренёнными.
                ThemeBrushes.Observe(this, key, brush => { setter(brush); ApplyState(); });
            }

            /// <summary>Применяет состояние к фону/границе/прозрачности кнопки.</summary>
            private void ApplyState()
            {
                if (!IsEnabled)
                {
                    Opacity = 0.4;
                    Background = _baseBg;
                    BorderBrush = _border;
                    BorderThickness = new Thickness(1);
                    return;
                }

                Opacity = 1.0;
                Background = _pressed ? _pressedBg : (_hovered ? _hoverBg : _baseBg);
                if (_focused)
                {
                    // Видимый focus-ринг акцентным цветом темы для клавиатурной навигации.
                    BorderBrush = _accent;
                    BorderThickness = new Thickness(2);
                }
                else
                {
                    BorderBrush = _border;
                    BorderThickness = new Thickness(1);
                }
            }
        }

        /// <summary>
        /// Простой наблюдатель ресурса-кисти темы: передаёт текущее значение в setter и
        /// при изменении (в т.ч. при смене схемы) вызывает onChanged.
        /// </summary>
        private sealed class BrushObserver : IObserver<object?>
        {
            private readonly Action<IBrush> _setter;
            private readonly Action _onChanged;

            public BrushObserver(Action<IBrush> setter, Action onChanged)
            {
                _setter = setter;
                _onChanged = onChanged;
            }

            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(object? value)
            {
                if (value is IBrush brush)
                    _setter(brush);
                _onChanged();
            }
        }

        /// <summary>Простой наблюдатель bool (для IsEnabled / клавиатурного фокуса).</summary>
        private sealed class BoolObserver : IObserver<bool>
        {
            private readonly Action<bool> _onNext;
            public BoolObserver(Action<bool> onNext) => _onNext = onNext;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(bool value) => _onNext(value);
        }

        /// <summary>Простой наблюдатель WindowState (для значка разворота кнопки окна).</summary>
        private sealed class WindowStateObserver : IObserver<WindowState>
        {
            private readonly Action _onNext;
            public WindowStateObserver(Action onNext) => _onNext = onNext;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(WindowState value) => _onNext();
        }

        /// <summary>
        /// Сегментная кнопка переключателя (для сегментированного контроля): у выбранного
        /// сегмента акцентная заливка, а иконка/текст — цветом «на акценте»; у невыбранных —
        /// прозрачный фон с приглушённым текстом и hover/pressed-состояниями. Все кисти
        /// берутся из ресурсов темы (перекрашиваются при смене схемы). Если lockOn == true,
        /// активный сегмент нельзя «снять» кликом (поведение как у RadioButton).
        /// </summary>
        private sealed class SegmentButton : ToggleButton
        {
            private readonly string _iconKey;
            private readonly string _text;
            private readonly double _iconSize;
            private readonly double _textSize;
            private readonly bool _lockOn;

            private IBrush _hoverBg = Brushes.Transparent;
            private IBrush _pressedBg = Brushes.Transparent;
            private IBrush _accent = Brushes.Transparent;
            private IBrush _accentHover = Brushes.Transparent;
            private IBrush _accentPressed = Brushes.Transparent;

            private bool _hovered;
            private bool _pressed;
            private bool _focused;

            private Thickness _borderThickness = new(2);
            private IBrush? _restingBorder;
            private IBrush? _hoverBorder;
            private IBrush? _restingBg;

            /// <summary>Постоянная рамка в покое: нужна тегам панели фильтра.</summary>
            public void ShowRestingBorder(string brushKey)
                => ThemeBrushes.Observe(this, brushKey, brush => { _restingBorder = brush; ApplyState(); });

            /// <summary>Рамка при наведении: у чипов фильтра меняется только она.</summary>
            public void ShowHoverBorder(string brushKey)
                => ThemeBrushes.Observe(this, brushKey, brush => { _hoverBorder = brush; ApplyState(); });

            /// <summary>Толщина рамки: у чипов фильтра единица, как в разметке.</summary>
            public void SetBorderThickness(double thickness)
            {
                _borderThickness = new Thickness(thickness);
                ApplyState();
            }

            /// <summary>Заливка в покое: у чипов фильтра это фон карточки, а не пустота.</summary>
            public void ShowRestingBackground(string brushKey)
                => ThemeBrushes.Observe(this, brushKey, brush => { _restingBg = brush; ApplyState(); });

            public SegmentButton(string iconKey, string text, string hoverBgKey, string pressedBgKey, bool lockOn = true,
                double iconSize = 15, double cornerRadius = -1, double fontSize = 13)
            {
                _iconKey = iconKey;
                _text = text;
                _iconSize = iconSize;
                _textSize = fontSize;
                _lockOn = lockOn;
                var corner = cornerRadius >= 0 ? cornerRadius : UiMetrics.RadiusSm;

                HorizontalContentAlignment = HorizontalAlignment.Center;
                VerticalContentAlignment = VerticalAlignment.Center;
                Cursor = new Cursor(StandardCursorType.Hand);
                MinHeight = 30;
                Padding = new Thickness(12, 5);
                BorderThickness = new Thickness(2);
                BorderBrush = Brushes.Transparent;

                // Кастомный шаблон: скруглённый Border + ContentPresenter (без Fluent-хрома).
                Theme = new ControlTheme(typeof(ToggleButton))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<SegmentButton>((_, _) =>
                    {
                        // Толщину локально не задаём: в Avalonia локальное значение
                        // старше значения шаблона, и TemplateBinding ниже не сработал бы.
                        // Рамка приходила кистью, но рисовать её было нечем.
                        var border = new Border { CornerRadius = new CornerRadius(corner) };
                        border[!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
                        border[!Border.BorderBrushProperty] = new TemplateBinding(TemplatedControl.BorderBrushProperty);
                        border[!Border.BorderThicknessProperty] = new TemplateBinding(TemplatedControl.BorderThicknessProperty);
                        // Без этого фон измеряется ровно по содержимому и обрезает текст.
                        border[!Border.PaddingProperty] = new TemplateBinding(TemplatedControl.PaddingProperty);
                        UiMetrics.AddBrushTransition(border);
                        var presenter = new ContentPresenter();
                        presenter[!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty);
                        presenter[!ContentPresenter.HorizontalContentAlignmentProperty] = new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty);
                        presenter[!ContentPresenter.VerticalContentAlignmentProperty] = new TemplateBinding(ContentControl.VerticalContentAlignmentProperty);
                        border.Child = presenter;
                        return border;
                    }))
                    }
                };

                Subscribe(hoverBgKey, v => _hoverBg = v);
                Subscribe(pressedBgKey, v => _pressedBg = v);
                Subscribe("AccentBrush", v => _accent = v);
                Subscribe("AccentHoverBrush", v => _accentHover = v);
                Subscribe("AccentPressedBrush", v => _accentPressed = v);

                PointerEntered += (_, _) => { _hovered = true; ApplyState(); };
                PointerExited += (_, _) => { _hovered = false; _pressed = false; ApplyState(); };
                PointerPressed += (_, _) => { _pressed = true; ApplyState(); };
                PointerReleased += (_, _) => { _pressed = false; ApplyState(); };
                PointerCaptureLost += (_, _) => { _pressed = false; ApplyState(); };

                this.GetObservable(IsCheckedProperty, v => v == true).Subscribe(new BoolObserver(_ => { UpdateContent(); ApplyState(); }));
                this.GetObservable(IsEnabledProperty).Subscribe(new BoolObserver(_ => ApplyState()));
                this.GetObservable(IsKeyboardFocusWithinProperty).Subscribe(new BoolObserver(v => { _focused = v; ApplyState(); }));

                UpdateContent();
                ApplyState();
            }

            /// <summary>Не даём снимать уже активный сегмент (как RadioButton), когда это требуется.</summary>
            protected override void Toggle()
            {
                if (_lockOn && IsChecked == true)
                    return;
                base.Toggle();
            }

            private void Subscribe(string key, Action<IBrush> setter)
                // Подписка снимается вместе с уходом кнопки из дерева: список
                // освобождался только у кнопок тегов, поэтому пять сегментов
                // верхней панели держали прежнее дерево окна после каждой
                // пересборки содержимого.
                => ThemeBrushes.Observe(this, key, brush => { setter(brush); ApplyState(); });

            /// <summary>Собирает содержимое «иконка + текст» с цветом по состоянию выбора.</summary>
            private void UpdateContent()
            {
                // Невыбранное состояние — приглушённый текст (как в WPF SegmentRadioButton),
                // выбранное — контрастный текст на акцентной заливке.
                var brushKey = IsChecked == true ? "TextOnAccentBrush" : "TextSecondaryBrush";
                var sp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center
                };
                // Пустой ключ означает кнопку без иконки: IconHelper на пустой ключ
                // подставляет запасную папку, и она выглядела бы как настоящая иконка.
                if (!string.IsNullOrEmpty(_iconKey))
                    sp.Children.Add(IconHelper.MakeIcon(_iconKey, _iconSize, brushKey));
                if (!string.IsNullOrEmpty(_text))
                {
                    // Кегль берётся с самой кнопки: локальные 13 перебивали
                    // значение, заданное снаружи, и теги панели фильтра
                    // не становились мельче.
                    var tb = new TextBlock
                    {
                        Text = _text,
                        FontSize = _textSize,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, brushKey);
                    sp.Children.Add(tb);
                }
                Content = sp;
            }

            private void ApplyState()
            {
                if (!IsEnabled)
                {
                    Opacity = 0.55;
                    Background = Brushes.Transparent;
                    BorderBrush = Brushes.Transparent;
                    BorderThickness = new Thickness(0);
                    return;
                }

                Opacity = 1.0;
                // Толщину надо вернуть: при отключении она обнулялась и обратно
                // не восстанавливалась.
                BorderThickness = _borderThickness;
                var idle = _restingBg ?? Brushes.Transparent;
                if (IsChecked == true)
                    Background = _pressed ? _accentPressed : (_hovered ? _accentHover : _accent);
                else if (_restingBg is not null && _hoverBorder is not null)
                    // Разметка при наведении меняет у чипа только рамку.
                    Background = _pressed ? _pressedBg : idle;
                else
                    Background = _pressed ? _pressedBg : (_hovered ? _hoverBg : idle);

                // Толщина постоянна, меняется только цвет: иначе фокус
                // расширял бы кнопку на четыре пикселя, а по её правому краю
                // выравнивается подпись «Название» в шапке списка.
                // Рамка в покое видна только там, где её просили: у тегов
                // панели фильтра. У остальных сегментов она прозрачная.
                BorderBrush = _focused
                    ? _accent
                    : (_hovered && _hoverBorder is not null
                        ? _hoverBorder
                        : (IsChecked == true && _restingBorder is not null ? _accent : _restingBorder ?? Brushes.Transparent));
            }
        }

        /// <summary>Тип собственной кнопки управления окном.</summary>
        private enum WindowControlKind
        {
            Minimize,
            Maximize,
            Close
        }

        /// <summary>
        /// Собственная кнопка управления окном (свернуть/развернуть/закрыть) в духе
        /// macOS/VSCode: скруглённая подложка при наведении/нажатии, плавные переходы
        /// цвета и масштаба (UiMetrics.AddBrushTransition / AddScaleTransition),
        /// выразительные значки Segoe Fluent-стиля. Цвета значка следуют теме через
        /// ThemeBrushes (как у PanelButton/SegmentButton), а параметры подложки
        /// (кисть наведения, скругление, размер) приходят из темы через
        /// WindowButtonThemeMetrics (Задача 4). Иконка разворота переключается
        /// между «квадрат» и «два квадрата» по состоянию окна.
        /// </summary>
        private sealed class WindowControlButton : Button
        {
            // Запасной контур (черта в поле 24×24) — только если резолв активного набора
            // почему-либо вернул null. Ключи IconMinimize/IconMaximize/IconRestore/IconClose
            // есть в базовом словаре Icons.axaml, поэтому это недостижимо в норме (Задача 2).
            private const string MinimizeData = "M6,12 L18,12";

            /// <summary>Толщина обводки квадратов (черта — 2.0, крест — 1.7).</summary>
            private const double GlyphStrokeThickness = 1.6;
            private const double MinimizeStrokeThickness = 2.0;
            private const double CloseStrokeThickness = 1.7;

            /// <summary>Диаметр подложки кнопки (без учёта скругления).</summary>
            private const double CircleDiameter = 30;

            private readonly MainWindow _window;
            private readonly WindowControlKind _kind;
            private readonly Avalonia.Controls.Shapes.Path _glyph;
            private readonly IDisposable? _stateSub;

            private IBrush _hoverBg = Brushes.Transparent;

            /// <summary>Подложка наведения при НЕАКТИВНОМ окне (issue #26): тёмная в тёмной
            /// теме, чтобы кнопки окна не «светились белым» на тёмной шапке карточки.</summary>
            private IBrush _inactiveHoverBg = Brushes.Transparent;

            private IBrush _closeHoverBg = Brushes.Transparent;

            /// <summary>Кисть подложки «свернуть» (Задача 3): у macOS-«светофора» жёлтый круг.</summary>
            private IBrush _minimizeHoverBg = Brushes.Transparent;

            /// <summary>Кисть подложки «развернуть» (Задача 3): у macOS-«светофора» зелёный круг.</summary>
            private IBrush _maximizeHoverBg = Brushes.Transparent;

            private IBrush _pressedBg = Brushes.Transparent;
            private IBrush _restGlyphBrush = Brushes.Transparent;
            private IBrush _accentGlyphBrush = Brushes.Transparent;
            private IBrush _accentBrush = Brushes.Transparent;
            private bool _hovered;
            private bool _pressed;
            private bool _focused;
            private bool _onAccent;

            /// <summary>Скругление подложки из темы: Fluent — круг, Minimal — мягкий квадрат, Classic — прямоугольник.</summary>
            private double _cornerRadius = CircleDiameter / 2;

            // Части шаблона: задаются при его применении, до этого null (ApplyState
            // вызывается и из конструктора, поэтому все обращения защищены проверкой).
            private Border? _circle;
            private ScaleTransform? _circleScale;
            private ScaleTransform? _glyphScale;
            private Border? _focusRing;

            // Красная подложка кнопки «закрыть» при нажатии: алый при наведении приходит
            // из темы (WindowButtonCloseHoverBrush), нажатие — темнее. Значок на ней белый.
            private static readonly IBrush ClosePressedBrush = new SolidColorBrush(Color.Parse("#B91C1C"));

            // Тёмный значок «светофора» macOS (Задача 3): почти чёрный под курсором,
            // полупрозрачный в покое — как у настоящих кнопок macOS.
            private static readonly IBrush MacOsGlyphBrush = new SolidColorBrush(Color.Parse("#1F1F1F"));
            private static readonly IBrush MacOsGlyphRestBrush = new SolidColorBrush(Color.Parse("#661F1F1F"));

            /// <summary>Затемняет кисть «светофора» при нажатии (как у кнопок macOS).</summary>
            private static IBrush Darken(IBrush brush)
            {
                if (brush is SolidColorBrush scb)
                {
                    var c = scb.Color;
                    return new SolidColorBrush(Color.FromArgb(c.A,
                        (byte)(c.R * 0.8), (byte)(c.G * 0.8), (byte)(c.B * 0.8)));
                }
                return brush;
            }

            public WindowControlButton(MainWindow window, WindowControlKind kind)
            {
                _window = window;
                _kind = kind;

                // Ширина и высота далее переопределяются метриками темы
                // (ApplyWindowButtonTheme); здесь — стартовый размер Fluent.
                Width = UiMetrics.Scaled(46);
                Height = UiMetrics.Scaled(34);
                Padding = new Thickness(0);
                HorizontalContentAlignment = HorizontalAlignment.Center;
                VerticalContentAlignment = VerticalAlignment.Center;
                Cursor = new Cursor(StandardCursorType.Hand);

                // Кастомный шаблон: круглая подложка + ContentPresenter (без Fluent-хрома).
                // Цвет подложки меняется плавно через BrushTransition, а тактильные
                // отклики масштаба — через DoubleTransition на ScaleTransform.
                Theme = new ControlTheme(typeof(Button))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<WindowControlButton>((_, _) =>
                        {
                            var diameter = UiMetrics.Scaled(CircleDiameter);
                            // Скругление из темы: Fluent — круг, Minimal — мягкий квадрат,
                            // Classic — прямоугольник. Радиус ограничен половиной диаметра,
                            // чтобы подложка не выходила за свои границы.
                            var radius = UiMetrics.Scaled(Math.Min(_cornerRadius, CircleDiameter / 2));

                            _circle = new Border
                            {
                                Width = diameter,
                                Height = diameter,
                                CornerRadius = new CornerRadius(radius),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            _circle[!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
                            UiMetrics.AddBrushTransition(_circle, background: true, border: false);

                            _circleScale = new ScaleTransform();
                            _circle.RenderTransform = _circleScale;
                            UiMetrics.AddScaleTransition(_circleScale, 90);

                            // Кольцо фокуса: тонкая акцентная обводка вокруг подложки
                            // для клавиатурной навигации (как у PanelButton).
                            _focusRing = new Border
                            {
                                Width = diameter,
                                Height = diameter,
                                CornerRadius = new CornerRadius(radius),
                                BorderThickness = new Thickness(1.5),
                                BorderBrush = Brushes.Transparent,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                IsHitTestVisible = false
                            };
                            UiMetrics.AddBrushTransition(_focusRing, background: false, border: true);

                            var presenter = new ContentPresenter();
                            presenter[!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty);
                            presenter[!ContentPresenter.HorizontalContentAlignmentProperty] = new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty);
                            presenter[!ContentPresenter.VerticalContentAlignmentProperty] = new TemplateBinding(ContentControl.VerticalContentAlignmentProperty);
                            presenter.HorizontalAlignment = HorizontalAlignment.Center;
                            presenter.VerticalAlignment = VerticalAlignment.Center;

                            _glyphScale = new ScaleTransform();
                            presenter.RenderTransform = _glyphScale;
                            UiMetrics.AddScaleTransition(_glyphScale, 110);

                            var root = new Grid();
                            root.Children.Add(_circle);
                            root.Children.Add(_focusRing);
                            root.Children.Add(presenter);
                            return root;
                        }))
                    }
                };

                // Значок в координатном поле 14 на 14 (как в MainWindow.xaml:205-231):
                // черта толще квадратов, крест чуть толще, концы скруглены.
                _glyph = new Avalonia.Controls.Shapes.Path
                {
                    Width = UiMetrics.Scaled(14),
                    Height = UiMetrics.Scaled(14),
                    Stretch = Stretch.Uniform,
                    StrokeThickness = _kind switch
                    {
                        WindowControlKind.Minimize => MinimizeStrokeThickness,
                        WindowControlKind.Close => CloseStrokeThickness,
                        _ => GlyphStrokeThickness
                    },
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round,
                    Data = BuildGeometry()
                };
                Content = _glyph;

                // Цвет значка и hover-подложка следуют теме. Кисть значка не привязывается
                // напрямую: он перекрашивается по состоянию (белый на красной подложке
                // «закрыть», цвет подписи на акцентной шапке), см. ApplyState.
                // Состояний три, как у автора: приглушённый в покое (App.xaml:79),
                // цвет подписи под курсором (App.xaml:94-97) и ButtonTextBrush
                // на акцентной шапке (App.xaml:116-118).
                ThemeBrushes.Observe(this, "TextSecondaryColorBrush", b => { _restGlyphBrush = b; ApplyState(); });
                // issue #26 (7OH, 2026-10-29): при неактивном окне глиф под курсором
                // остаётся вторичным (не становится основным/белым — «белые элементы»).
                // Только на акцентной шапке глиф красится контрастным акценту цветом.
                // Глиф «на акценте» контрастен фактическому акценту (issue #26):
                // при тёмном пользовательском акценте белый, при светлом — чёрный.
                ThemeBrushes.Observe(this, "AccentTextBrush", b => { _accentGlyphBrush = b; ApplyState(); });
                // Подложка наведения берётся не из схемы, а из стиля кнопок окна:
                // WindowButtonHoverBrush кладёт ThemeManager по WindowButtonStyle
                // (Задача 4), поэтому наблюдение ItemHoverBrush здесь не нужно —
                // кисть приходит из метрик темы (ApplyWindowButtonTheme).
                ThemeBrushes.Observe(this, "AccentPressedBrush", b => { _pressedBg = b; ApplyState(); });
                ThemeBrushes.Observe(this, "AccentBrush", b => { _accentBrush = b; ApplyState(); });

                PointerEntered += (_, _) => { _hovered = true; ApplyState(); };
                PointerExited += (_, _) => { _hovered = false; _pressed = false; ApplyState(); };
                PointerPressed += (_, _) => { _pressed = true; ApplyState(); };
                PointerReleased += (_, _) => { _pressed = false; ApplyState(); };
                PointerCaptureLost += (_, _) => { _pressed = false; ApplyState(); };
                this.GetObservable(IsEnabledProperty).Subscribe(new BoolObserver(_ => ApplyState()));
                this.GetObservable(IsKeyboardFocusWithinProperty).Subscribe(new BoolObserver(v => { _focused = v; ApplyState(); }));

                // Иконка разворота зависит от состояния окна: квадрат / два квадрата.
                if (_kind == WindowControlKind.Maximize)
                    _stateSub = window.GetObservable(Window.WindowStateProperty).Subscribe(new WindowStateObserver(UpdateGlyph));

                // Метрики стиля кнопок (кисти/радиус/размер) читаются из ресурсов темы
                // и обновляются при смене полноценной темы (ThemeManager.ApplyFullTheme).
                ApplyWindowButtonTheme();
                ThemeManager.ThemeChanged += OnThemeChanged;
            }

            /// <summary>
            /// Применяет метрики стиля кнопок окна (<see cref="WindowButtonThemeMetrics"/>):
            /// кисти наведения, размер кнопки и скругление подложки. Вызывается при создании
            /// кнопки и при смене полноценной темы.
            /// </summary>
            private void ApplyWindowButtonTheme()
            {
                var metrics = WindowButtonThemeMetrics.Current();
                _hoverBg = metrics.Hover;
                // issue #26 (7OH, 2026-10-29): при неактивном окне подложка наведения
                // берётся из отдельной метрики (тёмная в тёмной теме) — белая подсветка
                // кнопок управления окном на тёмной шапке карточки убрана.
                _inactiveHoverBg = metrics.InactiveHover;
                _closeHoverBg = metrics.CloseHover;
                _minimizeHoverBg = metrics.MinimizeHover;
                _maximizeHoverBg = metrics.MaximizeHover;
                _cornerRadius = metrics.CornerRadius;
                Width = UiMetrics.Scaled(metrics.Size);
                Height = UiMetrics.Scaled(WindowButtonThemeMetrics.ButtonHeight);
                var radius = UiMetrics.Scaled(Math.Min(_cornerRadius, CircleDiameter / 2));
                if (_circle is not null)
                    _circle.CornerRadius = new CornerRadius(radius);
                if (_focusRing is not null)
                    _focusRing.CornerRadius = new CornerRadius(radius);
                ApplyState();
            }

            /// <summary>
            /// Смена полноценной темы: перечитываем метрики кнопок окна и пересобираем
            /// глиф по активному набору иконок (Задача 2).
            /// </summary>
            private void OnThemeChanged(object? sender, EventArgs e)
            {
                ApplyWindowButtonTheme();
                UpdateGlyph();
            }

            private Geometry BuildGeometry()
            {
                var key = _kind switch
                {
                    // Штриховые глифы кнопок окна (0.3.12.61) — отдельно от залитых иконок.
                    WindowControlKind.Minimize => "GlyphWindowMinimize",
                    WindowControlKind.Close => "GlyphWindowClose",
                    WindowControlKind.Maximize => _window.WindowState == WindowState.Maximized
                        ? "GlyphWindowRestore"
                        : "GlyphWindowMaximize",
                    _ => "GlyphWindowMaximize"
                };
                // Глифы кнопок окна следуют активному набору иконок (Задача 2):
                // IconHelper резолвит суффиксный ключ набора с fallback на Fluent.
                return IconHelper.Geometry(key) ?? StreamGeometry.Parse(MinimizeData);
            }

            private void UpdateGlyph() => _glyph.Data = BuildGeometry();

            private void ApplyState()
            {
                if (!IsEnabled)
                {
                    Opacity = 0.55;
                    Background = Brushes.Transparent;
                    BorderBrush = Brushes.Transparent;
                    ApplyFeedback(disabled: true);
                    return;
                }

                Opacity = 1.0;
                BorderBrush = Brushes.Transparent;

                // В покое значок приглушён, на акцентной шапке красится цветом подписи
                // на акценте, а под курсором и то и другое уступает основному цвету
                // текста, как триггер IsMouseOver в шаблоне автора (App.xaml:94-97).
                // На акцентной шапке hover-цвет остаётся светлым (ButtonTextBrush),
                // иначе тёмный значок потерялся бы на акцентной заливке.
                var restBrush = _onAccent ? _accentGlyphBrush : _restGlyphBrush;
                // issue #26: на акцентной шапке глиф под курсором остаётся контрастным
                // акценту; при неактивном окне — вторичным (не белым, как TextPrimary).
                var hoverBrush = _onAccent ? _accentGlyphBrush : _restGlyphBrush;

                if (_kind == WindowControlKind.Close)
                {
                    // Кнопка «закрыть»: красная подложка при наведении/нажатии (алый из темы),
                    // значок — белый.
                    var redActive = _pressed || _hovered;
                    Background = _pressed ? ClosePressedBrush : (_hovered ? _closeHoverBg : Brushes.Transparent);
                    SetGlyphBrush(redActive ? Brushes.White : restBrush);
                }
                else if (ThemeManager.CurrentWindowButtonStyle == WindowButtonStyle.MacOS)
                {
                    // «Светофор» macOS (Задача 3): круг всегда цветной — жёлтый у «свернуть»,
                    // зелёный у «развернуть»; значок тёмный (как у настоящих кнопок macOS),
                    // при нажатии круг темнеет.
                    var traffic = _kind == WindowControlKind.Minimize ? _minimizeHoverBg : _maximizeHoverBg;
                    Background = _pressed ? Darken(traffic) : traffic;
                    SetGlyphBrush(_hovered || _pressed ? MacOsGlyphBrush : MacOsGlyphRestBrush);
                }
                else
                {
                    // issue #26 (7OH, 2026-10-29): при неактивном окне подложка наведения
                    // берётся из InactiveHover (тёмная в тёмной теме), чтобы кнопки окна
                    // не «светились белым» на тёмной шапке карточки.
                    var hoverBg = _onAccent ? _hoverBg : _inactiveHoverBg;
                    Background = _pressed ? _pressedBg : (_hovered ? hoverBg : Brushes.Transparent);
                    SetGlyphBrush(_hovered || _pressed ? hoverBrush : restBrush);
                }

                // Кольцо фокуса: акцентная обводка на обычных темах, светлая — на акцентной шапке.
                if (_focusRing is not null)
                    _focusRing.BorderBrush = _focused ? (_onAccent ? _accentGlyphBrush : _accentBrush) : Brushes.Transparent;

                ApplyFeedback(disabled: false);
            }

            /// <summary>
            /// Тактильные отклики масштабом: лёгкое увеличение значка при наведении,
            /// «пружинное» сжатие круга и значка при нажатии. Плавность обеспечивают
            /// Transitions (AddScaleTransition); при выключенных анимациях значения
            /// применяются сразу.
            /// </summary>
            private void ApplyFeedback(bool disabled)
            {
                if (_circleScale is not null)
                {
                    var c = disabled || _pressed ? 0.9 : 1.0;
                    _circleScale.ScaleX = c;
                    _circleScale.ScaleY = c;
                }
                if (_glyphScale is not null)
                {
                    var g = disabled ? 1.0 : _pressed ? 0.92 : _hovered ? 1.08 : 1.0;
                    _glyphScale.ScaleX = g;
                    _glyphScale.ScaleY = g;
                }
            }

            /// <summary>Все значки окон рисуются обводкой с округлёнными концами.</summary>
            private void SetGlyphBrush(IBrush brush) => _glyph.Stroke = brush;

            /// <summary>
            /// Шапка активного окна залита акцентом, и значок на ней читается только
            /// цветом ButtonTextBrush (MainWindow.xaml.cs:588-615).
            /// </summary>
            public void SetOnAccent(bool onAccent)
            {
                if (_onAccent == onAccent)
                    return;
                _onAccent = onAccent;
                ApplyState();
            }

            /// <summary>Подписки живут, пока кнопка в дереве.</summary>
            protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
            {
                base.OnDetachedFromVisualTree(e);
                _stateSub?.Dispose();
                ThemeManager.ThemeChanged -= OnThemeChanged;
            }
        }

        /// <summary>Наблюдатель за значением свойства.</summary>
        private sealed class PropertyObserver<T> : IObserver<T>
        {
            private readonly Action<T> _apply;
            public PropertyObserver(Action<T> apply) => _apply = apply;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(T value) => _apply(value);
        }
    }
}
#endif