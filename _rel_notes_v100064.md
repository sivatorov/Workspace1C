### Исправлено

- **Цвет подчёркивания полей и ссылок теперь берётся из темы (issue #51, Windows и
  Linux)** — ссылка каталога релизов в окне обновлений (WPF) больше не использует
  жёсткий синий fallback (убран `Brushes.Blue` в
  [`UpdateCheckWindow.xaml.cs`](Workspace1C/Views/UpdateCheckWindow.xaml.cs));
  ссылки в окнах «Что нового»/«Доступно обновление» (рендер markdown, WPF и Avalonia)
  привязаны к акцентной кисти темы `AccentBrush` через `TryFindResource`/`ThemeBrushes`
  вместо жёсткого синего `#0B6BCB` и системного синего WPF
  ([`MarkdownRenderer.cs`](Workspace1C/Services/MarkdownRenderer.cs)). Подчёркивание
  в тёмных и светлых темах больше не «выпадает» из палитры. Добавлен тест
  `UnderlineThemeBindingTests`.
