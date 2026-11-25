# Настройка выпуска: секрет `PRO_REPO_TOKEN`

Один раз настраивается доступ GitHub Actions публичного репозитория
`sivatorov/Workspace1C` к приватному `sivatorov/Workspace1C.Pro`. После этого любая
сборка релиза (тег, Release, ручной запуск) содержит модули ПРО, а workflow падает,
если модулей в бинаре нет.

Почему это обязательно: модули 2.x лежат в приватном репозитории. Без доступа к нему
`csproj` не находит `ProModules.cs`, не определяет `PRO_MODULES`, и в бинарь не
попадает код функций 2.x — ключ `major=1` тогда ничего не открывает.

---

## 1. Создать fine-grained PAT (токен с доступом к одному репозиторию)

1. Открыть <https://github.com/settings/tokens?type=beta>
   (GitHub → аватар → **Settings** → **Developer settings** →
   **Personal access tokens** → **Fine-grained tokens**).
2. **Generate new token**.
3. Заполнить:
   - **Token name**: `Workspace1C release (PRO modules)`;
   - **Expiration**: 90 дней / 1 год (по политике; просроченный токен ломает релизы);
   - **Resource owner**: `sivatorov` (или организация-владелец `Workspace1C.Pro`);
   - **Repository access**: **Only select repositories** → `sivatorov/Workspace1C.Pro`;
   - **Permissions** → **Repository permissions**:
     - **Contents**: `Read-only` (обязательно — этим GitHub Actions делает checkout);
     - **Metadata**: `Read-only` (подставляется автоматически).
4. **Generate token** и сразу скопировать значение вида `github_pat_…`
   (показывается один раз).

Токену нужен доступ **только на чтение** и **только к `Workspace1C.Pro`** — публичный
репозиторий он не затрагивает.

## 2. Добавить секрет в публичный репозиторий

Значение секрета — это **тот самый токен из шага 1** (`github_pat_…`). Искать его
в настройках GitHub не нужно и нельзя: GitHub показывает токен один раз при создании,
потом восстановить значение невозможно — только пересоздать токен.

1. Открыть <https://github.com/sivatorov/Workspace1C/settings/secrets/actions>
   (репозиторий → **Settings** → **Secrets and variables** → **Actions**).
2. **New repository secret**.
3. **Name**: `PRO_REPO_TOKEN` — ровно так, workflow ищет именно это имя.
4. **Secret**: вставить скопированный `github_pat_…` из шага 1 (без пробелов и кавычек).
5. **Add secret**.

Если токен не сохранили при создании: вернуться в
<https://github.com/settings/tokens?type=beta>, у старого токена нажать **Revoke**,
создать новый по шагу 1 и вставить его в секрет (или нажать **Update** у секрета).

Секрет появится в списке без значения (изменить/прочитать его нельзя — только
перезаписать или удалить).

## 3. Проверить на тестовой сборке (без публикации)

1. Открыть <https://github.com/sivatorov/Workspace1C/actions/workflows/release.yml>.
2. **Run workflow**:
   - **tag**: `v1.0.1.120-test1` (или следующий тестовый тег);
   - **prerelease**: `true`;
   - **build_only**: `true` — только сборка и проверки, релиз не публикуется.
3. В прогоне убедиться, что:
   - шаг **«Модули ПРО (приватный репозиторий)»** выполнен (не пропущен);
   - шаг **«Ошибка — сборка без модулей ПРО»** не сработал;
   - шаг **«Проверка модулей ПРО в Windows-бинаре»** вывел
     `Модули ПРО в Windows-бинаре: proModules=true.`;
   - аналогичный шаг для Linux прошёл.

Если шаг проверки упал с `proModules=false` — токен есть, но приватный репозиторий не
подтянулся: см. раздел «Частые ошибки».

## 4. Выпустить версию

1. Поднять версию и подготовить заметки (`rel_notes_<версия>.md`), как обычно.
2. Создать тег и запушить: `git tag v1.0.1.120 && git push origin v1.0.1.120`
   (или опубликовать Release в веб-интерфейсе).
3. Workflow соберёт Windows/Linux, проверит `proModules=true` и опубликует релиз.
4. Проверить скачанный бинарь:

   ```powershell
   .\Workspace1C.exe --license-info --json
   ```

   Ожидается `"proModules": true`. После активации ключа `major=1`
   (`.\Workspace1C.exe --activate "W1C2.…"`) функции 2.x доступны сразу, без перезапуска.

## 5. Ротация и безопасность

- Токен хранится только в секретах GitHub; в репозиторий и логи он не попадает.
- При истечении срока действия или компрометации: создать новый PAT, перезаписать секрет
  `PRO_REPO_TOKEN`, старый токен отозвать (**Revoke** в настройках токенов).
- Если `Workspace1C.Pro` переедет в организацию с включённым SSO, после создания токена
  нужно авторизовать его для этой организации (кнопка **Configure SSO** у токена).

## Частые ошибки

| Симптом | Причина | Что делать |
|---|---|---|
| `Error: Input required and not supplied: token` или checkout приватного репозитория падает с 404/128 | Секрет не задан или имя отличается | Имя должно быть ровно `PRO_REPO_TOKEN`; проверить Settings → Secrets → Actions |
| Шаг «Ошибка — сборка без модулей ПРО»: `PRO_REPO_TOKEN не задан` | Секрет отсутствует/удалён | Добавить секрет (раздел 2) |
| Checkout приватного репозитория: `could not read Username` / 403 | Токен без прав, просрочен или без SSO | Contents: Read-only на `Workspace1C.Pro`; продлить/пересоздать токен; Configure SSO |
| Сборка прошла, но «Проверка модулей ПРО»: `proModules=false` | Приватный репозиторий склонирован не туда/пустой, либо `pro_ref` указывает на коммит без `src/ProModules.cs` | Проверить шаг checkout (`path: pro`), `ProSourceDir`, корректность `pro_ref` (полный SHA) |
| Секрет есть, но в PR из форка сборка без модулей | Секреты не передаются в PR из форков | Это ожидаемо; релизы собираются по тегу/Release в своём репозитории |

## Локальная сборка (альтернатива CI)

Если релиз нужен без GitHub Actions:

```powershell
cd Workspace1C
$env:ProSourceDir = 'G:\h\Workspace1C\Workspace1C.Pro\src\'   # если клон не рядом
.\build-windows-single-file.ps1 -Configuration Release
```

Без модулей скрипт теперь завершается ошибкой; публичная редакция — только явно
`-AllowCommunity` (Linux: `ALLOW_COMMUNITY=1`).
