#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Workspace1C.Themes
{
    /// <summary>
    /// Привязка кистей-ресурсов темы к свойствам элементов, собираемых в коде
    /// (без XAML). Сами ресурсы кладёт ThemeManager.ApplyScheme в
    /// Application.Resources под ключами вида <c>TextPrimaryColorBrush</c>
    /// и <c>CardBackgroundColorBrush</c>.
    /// </summary>
    public static class ThemeBrushes
    {
        /// <summary>
        /// Привязывает свойство-кисть элемента к ресурсу темы. При смене темы или
        /// цветовой схемы значение обновляется само.
        /// </summary>
        /// <param name="target">Элемент, у которого меняется кисть.</param>
        /// <param name="property">Свойство типа IBrush (Background, Foreground, BorderBrush).</param>
        /// <param name="brushKey">Ключ ресурса-кисти темы, например "CardBackgroundColorBrush".</param>
        /// <remarks>
        /// Привязку отслеживает сам элемент, и освобождать её обычно не нужно:
        /// повторная привязка того же свойства заменяет прежнюю, а её освобождение
        /// снимает и окраску, и подписку на смену ресурса. Ресурс ищется по цепочке
        /// логических родителей до окна, поэтому элемент, который так и не попал
        /// в содержимое окна, останется неокрашенным молча. Ловушки такого рода:
        /// ToolTip.Tip и MenuItem.Icon у неоткрытого меню.
        /// </remarks>
        /// <returns>
        /// Подписка на ресурс. Освобождать её нужно только там, где то же свойство
        /// потом задаётся обычным присваиванием: живая привязка переживает такую
        /// запись и вернёт своё значение при следующей смене темы или схемы.
        /// </returns>
        public static System.IDisposable Bind(StyledElement target, AvaloniaProperty property, string brushKey)
            => target.Bind(property, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brushKey));

        /// <summary>
        /// Возвращает полупрозрачную версию сплошной кисти темы: сохраняет цвет,
        /// но ставит заданную альфу. Используется для «стеклянного» фона главного
        /// окна, чтобы прозрачность/размытие проступали сквозь цвет темы. Если
        /// кисть не сплошная (градиент и т.п.) — возвращается как есть.
        /// </summary>
        /// <param name="brush">Исходная кисть, обычно из ресурсов темы.</param>
        /// <param name="alpha">Новая альфа (0–255): 0 полностью прозрачно, 255 непрозрачно.</param>
        /// <summary>
        /// Отдаёт кисть темы по ключу; при отсутствии — запасной HEX (v100112,
        /// тематизация остатков хардкод-цветов). Разовое чтение: для свойств
        /// долгоживущих элементов предпочтительнее <see cref="Bind"/>, но для
        /// разовых параметров (иконки, Setter'ы, выбор кисти по состоянию) — так.
        /// </summary>
        /// <param name="brushKey">Ключ ресурса-кисти темы, например "ErrorBrush".</param>
        /// <param name="fallbackHex">Запасной HEX вида "#DC2626", если ресурса нет.</param>
        public static Avalonia.Media.IBrush Get(string brushKey, string fallbackHex)
            => Avalonia.Application.Current?.TryGetResource(brushKey, Avalonia.Styling.ThemeVariant.Default, out var value) == true
                && value is Avalonia.Media.IBrush brush
                    ? brush
                    : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(fallbackHex));

        /// <summary>
        /// Кисть из строковой спецификации: строка с префиксом «@» — кисть ресурса
        /// темы (обновляется вместе со схемой), иначе — HEX вида «#RRGGBB».
        /// Нужно хелперам, которые до v100112 принимали только HEX-литералы
        /// (иконки меню, цветные глифы кнопок) — v100112, тематизация остатков.
        /// </summary>
        /// <param name="spec">Спецификация цвета: "@SuccessBrush" или "#16A34A".</param>
        public static Avalonia.Media.IBrush FromSpec(string spec)
        {
            if (!string.IsNullOrWhiteSpace(spec) && spec[0] == '@'
                && Avalonia.Application.Current?.TryGetResource(spec[1..],
                    Avalonia.Styling.ThemeVariant.Default, out var resource) == true
                && resource is Avalonia.Media.IBrush brush)
                return brush;
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(spec));
        }

        public static Avalonia.Media.IBrush WithAlpha(Avalonia.Media.IBrush brush, byte alpha)
        {
            if (brush is Avalonia.Media.ISolidColorBrush solid)
            {
                var c = solid.Color;
                return new Avalonia.Media.SolidColorBrush(new Avalonia.Media.Color(alpha, c.R, c.G, c.B));
            }
            return brush;
        }

        /// <summary>
        /// Отдаёт кисть темы в код и обновляет её при смене темы или схемы.
        /// Нужно там, где кисть не ложится в свойство напрямую: цвет зависит
        /// от состояния элемента (наведение, фокус) и пересчитывается вручную.
        /// </summary>
        /// <param name="target">Элемент, к жизни которого привязана подписка.</param>
        /// <param name="brushKey">Ключ ресурса-кисти темы.</param>
        /// <param name="apply">Что делать с полученной кистью.</param>
        /// <remarks>
        /// Подписка живёт ровно столько, сколько элемент находится в визуальном
        /// дереве: создаётся при присоединении, снимается при откреплении и
        /// создаётся заново, если элемент вернули. Иначе наблюдатель жил бы
        /// у приложения и держал элемент сильной ссылкой, а пересборка
        /// содержимого окна оставляла бы прежнее дерево укоренённым навсегда.
        /// Элемент, который так и не попал в дерево, подписки не создаёт вовсе.
        /// </remarks>
        public static void Observe(Avalonia.Controls.Control target, string brushKey, System.Action<Avalonia.Media.IBrush> apply)
        {
            System.IDisposable? subscription = null;

            void Start()
            {
                subscription?.Dispose();
                subscription = Avalonia.Application.Current?
                    .GetResourceObservable(brushKey)
                    .Subscribe(new BrushCallback(apply));
            }

            void Stop()
            {
                subscription?.Dispose();
                subscription = null;
            }

            target.AttachedToVisualTree += (_, _) => Start();
            target.DetachedFromVisualTree += (_, _) => Stop();

            // Элемент может быть уже в дереве: тогда события присоединения
            // не будет, а кисть нужна сразу.
            if (target.GetVisualRoot() is not null)
                Start();
        }

        private sealed class BrushCallback : System.IObserver<object?>
        {
            private readonly System.Action<Avalonia.Media.IBrush> _apply;

            public BrushCallback(System.Action<Avalonia.Media.IBrush> apply) => _apply = apply;

            public void OnCompleted() { }
            public void OnError(System.Exception error) { }
            public void OnNext(object? value)
            {
                if (value is Avalonia.Media.IBrush brush)
                    _apply(brush);
            }
        }
    }
}
#endif
