# T13 — комментарии в issues по релизу v1.0.1.125

- **Релиз:** [v1.0.1.125](https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.125)
- **Репозиторий:** `sivatorov/Workspace1C`
- **Дата публикации комментариев (UTC):** 2026-11-26
- **Issues не закрывались** — только комментарии.
- **Telegram повторно не отправлялся.**

## Сводка

| Issue | Тема | URL комментария | Статус issue | Версия в комментарии |
|---|---|---|---|---|
| #143 | Резервные копии средствами СУБД | https://github.com/sivatorov/Workspace1C/issues/143#issuecomment-7200771581 | OPEN | 1.0.1.125 |
| #159 | Linux — правый клик на Утилиты | https://github.com/sivatorov/Workspace1C/issues/159#issuecomment-7200772181 | OPEN | 1.0.1.125 |
| #167 | Скачивание платформы — разделы | https://github.com/sivatorov/Workspace1C/issues/167#issuecomment-7200772870 | OPEN | 1.0.1.125 |
| #170 | Видеоартефакты при отображении списка баз | https://github.com/sivatorov/Workspace1C/issues/170#issuecomment-7200773418 | OPEN | 1.0.1.125 |
| #171 | Не восстанавливается WindowState | https://github.com/sivatorov/Workspace1C/issues/171#issuecomment-7200774028 | OPEN | 1.0.1.125 |
| #172 | Прозрачная рамка вокруг максимизированного окна | https://github.com/sivatorov/Workspace1C/issues/172#issuecomment-7200774724 | OPEN | 1.0.1.125 |
| #163 | Хранение паролей от баз | https://github.com/sivatorov/Workspace1C/issues/163#issuecomment-7200775317 | OPEN | нет (ответ-подтверждение, изменений кода нет) |

## Проверка

Проверено через `gh issue view <n> --json state,comments`:

- все 7 issues — `OPEN`;
- последний комментарий в каждом — автор `sivatorov` (мой);
- в #143, #159, #167, #170, #171, #172 присутствует версия `1.0.1.125`;
- в #163 версии нет — это ответ-подтверждение по T7 (изменений кода и версии нет).

## Тексты комментариев

- `docs/_comment_143_v101125.md`
- `docs/_comment_159_v101125.md`
- `docs/_comment_167_v101125.md`
- `docs/_comment_170_v101125.md`
- `docs/_comment_171_v101125.md`
- `docs/_comment_172_v101125.md`
- `docs/_comment_163_v101125.md` (черновик T7)

## Публикация

Выполнено командой `gh issue comment <номер> --repo sivatorov/Workspace1C --body-file docs/_comment_<номер>_v101125.md`
для #143, #159, #167, #170, #171, #172, #163. Все команды завершились с кодом 0, повторов и дублей нет.
