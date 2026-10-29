Исправлено в версии **1.0.1.3** (Windows и Linux):

Иконка группы «Новинки программы» в реальном меню «Утилиты» больше не подменяется на «звезду»:

- в [`UtilitiesMenuCatalog.IsLegacyFallbackGroupIcon`](Workspace1C/Services/UtilitiesMenuCatalog.cs) распознаётся легаси-«звезда» (Star/IconStar) — меню «Утилиты» с таким ключом корректно получает иконку NewBox;
- назначенная пользователем иконка группы сохраняется.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.3