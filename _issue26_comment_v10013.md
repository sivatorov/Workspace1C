Исправлено в версии **1.0.1.3** (Windows и Linux):

Тёмная тема — «зебра» строк больше не выглядит светлой и не выбивается из темы:

- в [`WpfUi.Grid`](Workspace1C/Views/WpfUi.cs) чередование строк переведено на тематические кисти (`RowBackground=CardBackgroundBrush`, `AlternatingRowBackground=ZebraRowBrush` из темы) — фон берётся из активной темы, а не жёсткий;
- дополнительно проверен контраст кнопок и тумблеров в тёмной теме.

Спасибо за внимательную проверку оформления! Проверьте, пожалуйста, в новом релизе.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.3