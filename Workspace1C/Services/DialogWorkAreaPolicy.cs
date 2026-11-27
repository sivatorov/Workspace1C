using System;

namespace Workspace1C.Services;

/// <summary>
/// Issue #146 (1.0.1.106): общая политика размеров диалоговых окон по рабочей области
/// монитора. Окна правки меню («Утилиты», «Меню трея», «Мои программы») имели
/// фиксированную высоту 560–580 DIP и на экранах 768 px с масштабированием не
/// помещались — поясняющий текст уходил за нижнюю границу.
/// <para>
/// Политика чистая (без WPF/Avalonia), поэтому обе оболочки считают одинаково и
/// поведение покрыто тестами без запуска окон.
/// </para>
/// </summary>
public static class DialogWorkAreaPolicy
{
    /// <summary>Отступ от краёв рабочей области по умолчанию, DIP.</summary>
    public const double DefaultMargin = 48;

    /// <summary>
    /// Issue #146 (1.0.1.118): профиль высот диалогового окна. Семантика:
    /// <c>MinHeight < DefaultHeight = ContentHeight</c>.
    /// <para>
    /// <c>DefaultHeight</c> — стартовая высота окна и одновременно высота, при которой
    /// содержимое (панель операций + пояснение) помещается без прокрутки; запас сверх
    /// содержимого не закладывается. <c>MinHeight</c> — нижняя граница, заметно ниже
    /// <c>DefaultHeight</c>: окно можно уменьшать, не упираясь в «минимальный» размер,
    /// равный стартовому (жалоба 7OH в issue #146). Прокрутка
    /// (<see cref="DialogSize.RequiresScrolling"/>) включается только после клампа по
    /// маленькой рабочей области.
    /// </para>
    /// </summary>
    /// <param name="DefaultHeight">Высота окна по умолчанию (по содержимому), DIP.</param>
    /// <param name="MinHeight">Минимальная высота окна (нижняя граница), DIP.</param>
    /// <param name="ContentHeight">Высота, при которой содержимое помещается без прокрутки, DIP.</param>
    /// <param name="MaxHeight">
    /// Issue #164 (1.0.1.121): необязательная верхняя граница высоты окна, DIP. По умолчанию
    /// <see cref="double.PositiveInfinity"/> — ограничением служит только рабочая область
    /// монитора. Задаётся для окон с авторазмером по содержимому
    /// (<c>SizeToContent="Height"</c>/<c>Height = NaN</c>), чтобы длинный список не растянул
    /// окно на весь экран.
    /// </param>
    public readonly record struct DialogProfile(
        double DefaultHeight,
        double MinHeight,
        double ContentHeight,
        double MaxHeight = double.PositiveInfinity);

    /// <summary>
    /// Issue #146 (1.0.1.118): окно «Настройка меню утилиты» — 680 DIP по содержимому,
    /// минимальная высота 440 DIP. Пояснение перенесено в конец прокручиваемого
    /// содержимого, поэтому запас перед ним не нужен.
    /// Issue #159 (1.0.1.123): верхняя граница 800 DIP — окно растягивается по
    /// содержимому (<c>SizeToContent.Height</c>), а длинный список прокручивается
    /// внутри дерева, не растягивая окно на весь экран.
    /// </summary>
    // Issue #159 (1.0.1.128): 640 оказалось ниже реальной высоты содержимого —
    // правая колонка получала прокрутку; 680 (1.0.1.118–1.0.1.126) давало лишний
    // запас. 660 — содержимое помещается без прокрутки и без заметного пустого
    // места. MinHeight снижен 440 → 340: окно свободно сжимается по высоте.
    public static readonly DialogProfile UtilitiesEditor = new(660, 340, 660, 800);

    /// <summary>
    /// Issue #146 (1.0.1.118): окно «Настройка меню трея» — 680 DIP по содержимому,
    /// минимальная высота 440 DIP (самый длинный текст пояснения).
    /// Issue #159 (1.0.1.123): верхняя граница 800 DIP для авторазмера по содержимому.
    /// </summary>
    public static readonly DialogProfile TrayEditor = new(680, 440, 680, 800);

    /// <summary>
    /// Issue #146 (1.0.1.118): окно «Контекстное меню» — панель операций короче, чем
    /// у «Утилит», поэтому высота по содержимому меньше.
    /// Issue #159 (1.0.1.123): верхняя граница 760 DIP для авторазмера по содержимому.
    /// </summary>
    // Issue #159 (1.0.1.128): 540 (1.0.1.125) давало прокрутку, 600 (1.0.1.127) —
    // лишний запас; 570 помещает панель операций и пояснение без прокрутки.
    // MinHeight снижен 400 → 320: окно можно сжать существенно сильнее.
    public static readonly DialogProfile ContextMenuEditor = new(570, 320, 570, 760);

    /// <summary>
    /// Issue #146 (1.0.1.118): окно «Мои программы»; Issue #159 (1.0.1.123): верхняя
    /// граница авторазмера; список программ прокручивается своим <c>ListBox</c>,
    /// высоту окна задаёт форма.
    /// <para>
    /// Issue #178 (1.0.1.129, замечание 7OH «поправьте высоту диалога, чтобы без скролла
    /// по умолчанию было и не завышено»): в 1.0.1.127 в форму добавлены флажок
    /// «Запускать от имени администратора», подпись и поле учётной записи. Прежние
    /// 600 DIP (рассчитанные без этого блока) перестали вмещать правую форму — при
    /// открытии появлялась вертикальная прокрутка. 700 DIP вмещает все поля формы
    /// (включая блок запуска от администратора) и пояснение без прокрутки, но без
    /// большого пустого запаса. <c>MinHeight</c> 400 DIP оставляет окно свободно
    /// сжимаемым на маленьких экранах (прокрутка — только аварийный режим после
    /// клампа по рабочей области).
    /// </para>
    /// </summary>
    public static readonly DialogProfile CustomPrograms = new(700, 400, 700, 820);

    /// <summary>
    /// Issue #165 (1.0.1.120): окно «Как купить ПРО-версию» — 720 DIP по содержимому
    /// (инструкция из пяти шагов, код компьютера, контакты и раздел «что остаётся
    /// бесплатным»), минимальная высота 520 DIP. Нижняя панель кнопок
    /// («Активировать», «Закрыть») закреплена вне прокрутки, поэтому при нехватке
    /// высоты прокручивается только текст, а кнопки остаются видимыми.
    /// </summary>
    public static readonly DialogProfile HowToBuy = new(720, 520, 720);

    /// <summary>
    /// Issue #164 (1.0.1.121): окно входа — 420 DIP по содержимому (заголовок, список
    /// профилей высотой до 280 DIP, подпись и поле пароля, кнопки), минимальная высота
    /// 220 DIP. Окно растягивается по содержимому
    /// (<c>SizeToContent="Height"</c> в WPF, <c>Height = NaN</c> в Avalonia), поэтому
    /// <see cref="DialogProfile.DefaultHeight"/> — стартовая высота, а
    /// <see cref="DialogProfile.MaxHeight"/> 560 DIP страхует от выхода за экран при
    /// длинном списке профилей (сам список ограничен 280 DIP и прокручивается).
    /// </summary>
    public static readonly DialogProfile Login = new(
        DefaultHeight: 420,
        MinHeight: 220,
        ContentHeight: 420,
        MaxHeight: 560);

    /// <summary>
    /// Issue #164 (1.0.1.121): окно блокировки — 340 DIP по содержимому (заголовок,
    /// пояснение, подпись и поле пароля, кнопки), минимальная высота 240 DIP. Окно
    /// растягивается по содержимому (<c>SizeToContent="Height"</c> в WPF,
    /// <c>Height = NaN</c> в Avalonia), поэтому <see cref="DialogProfile.MaxHeight"/>
    /// 520 DIP страхует от выхода за экран при крупном системном шрифте.
    /// </summary>
    public static readonly DialogProfile AppLock = new(
        DefaultHeight: 340,
        MinHeight: 240,
        ContentHeight: 340,
        MaxHeight: 520);

    /// <summary>
    /// Issue #164 (1.0.1.121): окно смены пароля блокировки — три поля ввода (текущий,
    /// новый, повтор), поэтому выше обычного: 430 DIP по содержимому, минимальная
    /// высота 340 DIP, верхняя граница 560 DIP. Заменяет жёсткое <c>Height = 430</c>,
    /// из-за которого под кнопками оставалось пустое место (жалоба 7OH).
    /// </summary>
    public static readonly DialogProfile AppLockChange = new(
        DefaultHeight: 430,
        MinHeight: 340,
        ContentHeight: 430,
        MaxHeight: 560);

    /// <summary>
    /// Issue #146 (1.0.1.115): рекомендуемые размеры диалогового окна с учётом
    /// рабочей области монитора. <see cref="RequiresScrolling"/> = true только
    /// тогда, когда рабочая область не позволяет окну достичь высоты содержимого
    /// (<c>contentHeight</c>), то есть на действительно маленьком экране.
    /// </summary>
    public readonly record struct DialogSize(
        double Height,
        double MinHeight,
        double MaxHeight,
        bool RequiresScrolling);

    /// <summary>
    /// Issue #146 (1.0.1.116): рекомендованные размеры окна по его профилю. Профиль
    /// избавляет окна от «магических чисел» и гарантирует, что WPF и Avalonia
    /// считают одинаково.
    /// </summary>
    /// <param name="profile">Профиль высот окна (например, <see cref="UtilitiesEditor"/>).</param>
    /// <param name="workAreaHeight">Высота рабочей области монитора, DIP.</param>
    /// <param name="margin">Отступ от краёв рабочей области, DIP.</param>
    public static DialogSize Recommend(
        DialogProfile profile,
        double workAreaHeight,
        double margin = DefaultMargin) =>
        Recommend(
            profile.DefaultHeight,
            profile.MinHeight,
            profile.ContentHeight,
            workAreaHeight,
            margin,
            profile.MaxHeight);

    /// <summary>
    /// Считает итоговые размеры окна: <c>Height</c> клампится в рабочую область,
    /// <c>MinHeight</c> не превышает её верхнюю границу, а <c>RequiresScrolling</c>
    /// сообщает, что содержимое не помещается и нужна прокрутка (аварийный режим).
    /// </summary>
    /// <param name="requestedHeight">Желаемая высота окна (дефолт из XAML/кода).</param>
    /// <param name="preferredMinHeight">Желаемая минимальная высота окна.</param>
    /// <param name="contentHeight">Высота, при которой содержимое помещается без прокрутки.</param>
    /// <param name="workAreaHeight">Высота рабочей области монитора, DIP.</param>
    /// <param name="margin">Отступ от краёв рабочей области, DIP.</param>
    /// <param name="maxHeightCap">
    /// Необязательная верхняя граница высоты окна (например,
    /// <see cref="DialogProfile.MaxHeight"/>), DIP. Не задана/NaN/≤ 0 — ограничение
    /// только по рабочей области.
    /// </param>
    public static DialogSize Recommend(
        double requestedHeight,
        double preferredMinHeight,
        double contentHeight,
        double workAreaHeight,
        double margin = DefaultMargin,
        double maxHeightCap = double.PositiveInfinity)
    {
        var max = MaxHeight(workAreaHeight, margin);
        // Issue #164 (1.0.1.121): профиль может ограничить высоту окна с авторазмером
        // (SizeToContent/NaN), чтобы длинный список не растянул окно на весь экран.
        if (!double.IsNaN(maxHeightCap) && maxHeightCap > 0 && maxHeightCap < max)
            max = maxHeightCap;

        var min = Math.Min(ClampMinHeight(preferredMinHeight, workAreaHeight, margin), max);
        var requested = requestedHeight;
        if (double.IsNaN(requested) || requested <= 0)
            requested = min;
        var height = Math.Min(Math.Max(requested, min), max);

        if (double.IsNaN(contentHeight) || contentHeight <= 0)
            contentHeight = height;

        // Допуск 0.5 DIP: округление DPI не должно включать прокрутку «на глаз».
        return new DialogSize(height, min, max, height + 0.5 < contentHeight);
    }

    /// <summary>
    /// Максимально допустимая высота окна в рабочей области. Если рабочая область
    /// неизвестна (0/NaN/бесконечность), ограничение не накладывается.
    /// </summary>
    public static double MaxHeight(double workAreaHeight, double margin = DefaultMargin)
    {
        if (double.IsNaN(workAreaHeight) || double.IsInfinity(workAreaHeight) || workAreaHeight <= 0)
            return double.PositiveInfinity;

        var effectiveMargin = double.IsNaN(margin) || margin < 0 ? 0 : margin;
        return Math.Max(0, workAreaHeight - effectiveMargin);
    }

    /// <summary>
    /// Клампит минимальную высоту окна верхней границей рабочей области: на очень
    /// маленьком экране <c>MinHeight</c> не должен превышать <c>MaxHeight</c>, иначе
    /// в WPF/Avalonia нижняя граница побеждает верхнюю и окно всё равно выходит за
    /// экран (issue #146). Неизвестная рабочая область (0/NaN/бесконечность)
    /// оставляет минимум как есть; отрицательный или NaN минимум считается нулевым.
    /// </summary>
    public static double ClampMinHeight(
        double minHeight,
        double workAreaHeight,
        double margin = DefaultMargin)
    {
        if (double.IsNaN(minHeight) || minHeight < 0)
            minHeight = 0;

        var max = MaxHeight(workAreaHeight, margin);
        return double.IsPositiveInfinity(max) ? minHeight : Math.Min(minHeight, max);
    }

    /// <summary>
    /// Клампит желаемую высоту окна в <c>[minHeight, maxHeight]</c>, где
    /// <c>maxHeight</c> — рабочая область минус отступ. На очень маленьком экране
    /// приоритет отдаётся тому, чтобы окно целиком помещалось: верхняя граница
    /// побеждает нижнюю.
    /// </summary>
    public static double ClampHeight(
        double requested,
        double minHeight,
        double workAreaHeight,
        double margin = DefaultMargin)
    {
        if (double.IsNaN(requested) || requested <= 0)
            requested = minHeight;
        if (double.IsNaN(minHeight) || minHeight < 0)
            minHeight = 0;

        var max = MaxHeight(workAreaHeight, margin);
        if (double.IsPositiveInfinity(max))
            return Math.Max(requested, minHeight);

        var lowerBound = Math.Min(minHeight, max);
        return Math.Min(Math.Max(requested, lowerBound), max);
    }
}
