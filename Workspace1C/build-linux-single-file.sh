#!/usr/bin/env bash
# ==============================================================================
# Сборка ОДНОГО исполняемого файла для Linux (Avalonia, net10.0, linux-x64).
#
# Результат: self-contained single-file исполняемый файл
#   dist/linux-x64/Workspace1C
# (без папок, без .dll рядом, только один исполняемый файл).
#
# Требования:
#   * запускать НА Linux (TFM net10.0 выбирается по ОС в csproj);
#     либо на Windows с FORCE_LINUX=1 (кросс-компиляция, нужен .NET SDK 10);
#   * установлен .NET SDK 10 (>= 10.0.400);
#   * для запуска НА Linux установлены зависимости Avalonia:
#       sudo apt install -y libice6 libsm6 libfontconfig1 libfreetype6 libx11-6 \
#           libx11-dev libxext6 libxrender1 libglib2.0-0 libgtk-3-0
#
# Использование:
#   ./build-linux-single-file.sh            # сборка Release, RID linux-x64 (с модулями ПРО)
#   ./build-linux-single-file.sh Debug      # другой конфиг
#   RID=linux-arm64 ./build-linux-single-file.sh   # другой RID
#   ALLOW_COMMUNITY=1 ./build-linux-single-file.sh # публичная сборка без ПРО
#   ProSourceDir=/path/to/Workspace1C.Pro/src/ ./build-linux-single-file.sh # явный путь к ПРО
#
# Основная программа выпускается С модулями ПРО: они вкомпилированы в бинарь, а
# доступ к функциям 2.x закрывает лицензия (активация — без перезапуска). Поэтому
# модули обязательны: без клона Workspace1C.Pro сборка падает.
#
# Кросс-сборка из Windows (без WSL):
#   FORCE_LINUX=1 ./build-linux-single-file.sh
#   (передаёт -p:ForceLinux=true в csproj и принудительно включает Linux-ветку)
#
# ВНИМАНИЕ: скрипт НЕ запускает dotnet publish, если установлена переменная
# SKIP_PUBLISH=1 (полезно для проверки синтаксиса): SKIP_PUBLISH=1 ./script.sh
#
# Порядок проверок:
#   1) restore;
#   2) HasProModules (dotnet msbuild) — ДО publish: без модулей основная сборка
#      падает сразу, чтобы не тратить время и не оставлять «полусобранный» артефакт;
#   3) publish одного файла;
#   4) proModules по самому бинарю (--license-info --json) — если бинарь запускается
#      в текущем окружении (в CI Linux-джобе запускается всегда).
# ==============================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$ROOT/Workspace1C.csproj"
CONFIG="${1:-Release}"
RID="${RID:-linux-x64}"
DIST="$ROOT/dist/$RID"
FORCE_LINUX="${FORCE_LINUX:-0}"
# Модули ПРО обязательны; публичная редакция — только явно ALLOW_COMMUNITY=1.
if [[ "${ALLOW_COMMUNITY:-0}" == "1" ]]; then
  REQUIRE_PRO=0
else
  REQUIRE_PRO=1
fi

# На Linux TFM net10.0 выбирается по ОС. Для кросс-сборки из Windows
# включаем Linux-ветку принудительно через -p:ForceLinux=true.
if [[ "$(uname -s)" != "Linux" && "$FORCE_LINUX" != "1" ]]; then
  echo "!! Скрипт предназначен для запуска НА Linux (TFM net10.0 задаётся по ОС сборки)."
  echo "   Либо запустите с FORCE_LINUX=1 для кросс-сборки Linux-версии из Windows:"
  echo "     FORCE_LINUX=1 ./build-linux-single-file.sh"
  echo "   На текущей ОС ('$(uname -s)') без FORCE_LINUX сборка даст WPF и не запустится на Linux."
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "!! .NET SDK не найден. Установите .NET SDK 10."
  exit 1
fi

# Доп. свойства: кросс-сборка из Windows (пусто на реальном Linux) и явный путь
# к исходникам ПРО (иначе csproj сам ищет клон Workspace1C.Pro).
FORCE_PROP=""
if [[ "$FORCE_LINUX" == "1" ]]; then
  FORCE_PROP="-p:ForceLinux=true"
fi
PRO_PROP=""
if [[ -n "${ProSourceDir:-}" ]]; then
  PRO_PROP="-p:ProSourceDir=${ProSourceDir}"
fi
# Публичная редакция: модули ПРО не включаются, даже если клон найден.
COMMUNITY_PROP=""
if [[ "$REQUIRE_PRO" == "0" ]]; then
  COMMUNITY_PROP="-p:CommunityEdition=true"
fi
BUILD_PROPS="$FORCE_PROP $PRO_PROP $COMMUNITY_PROP"

echo "==> Конфигурация: $CONFIG"
echo "==> RID:          $RID"
if [[ "$FORCE_LINUX" == "1" ]]; then
  echo "==> Кросс-сборка Linux из Windows (ForceLinux=true)"
fi
echo "==> Цель:         $DIST"
if [[ "$REQUIRE_PRO" == "1" ]]; then
  echo "==> Редакция:     ПРО (модули обязательны)"
else
  echo "==> Редакция:     публичная (ALLOW_COMMUNITY=1)"
fi
if [[ -n "$PRO_PROP" ]]; then
  echo "==> ProSourceDir: $ProSourceDir"
fi

echo "==> Restore"
dotnet restore "$PROJECT" $BUILD_PROPS

# Редакция: модули ПРО ищем ДО publish (как в Windows-скрипте). Без них основная
# программа не выпускается: функции 2.x не войдут в бинарь, и ключ их не откроет.
HAS_PRO="$(dotnet msbuild "$PROJECT" -nologo -getProperty:HasProModules $BUILD_PROPS | tr -d '[:space:]')"
echo "==> HasProModules: ${HAS_PRO:-unknown}"
if [[ "$HAS_PRO" == "true" ]]; then
  echo "==> Модули ПРО: найдены (PRO_MODULES) — функции 2.x откроются лицензией (major=1)."
else
  echo "==> Модули ПРО: НЕ найдены — функции 2.x в бинарь не войдут."
  echo "    Укажите клон: ProSourceDir=<путь к Workspace1C.Pro/src/>"
  echo "    или положите клон рядом с репозиторием: ../Workspace1C.Pro/src/"
  if [[ "$REQUIRE_PRO" == "1" ]]; then
    echo "!! Основная программа собирается с модулями ПРО: сборка прервана до publish."
    echo "   Публичная редакция без ПРО — только явно: ALLOW_COMMUNITY=1"
    exit 1
  fi
fi
if [[ "$REQUIRE_PRO" == "0" ]]; then
  echo "!! ВНИМАНИЕ: ALLOW_COMMUNITY=1 — собирается публичная редакция без функций 2.x."
fi

if [[ "${SKIP_PUBLISH:-0}" == "1" ]]; then
  echo "==> SKIP_PUBLISH=1 — публикация пропущена."
  exit 0
fi

echo "==> Publish (self-contained single-file)"
rm -rf "$DIST"
dotnet publish "$PROJECT" \
  -c "$CONFIG" \
  -r "$RID" \
  --self-contained true \
  $BUILD_PROPS \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:PublishReadyToRun=false \
  -o "$DIST"

# Оставляем ТОЛЬКО исполняемый файл: удаляем .pdb, .json, .xml и любые файлы
# и папки (например, Localization/Languages), кроме самого бинарника.
find "$DIST" -type f \( -name "*.pdb" -o -name "*.json" -o -name "*.xml" \) -delete 2>/dev/null || true
for f in "$DIST"/*; do
  if [[ -e "$f" && "$(basename "$f")" != "Workspace1C" ]]; then
    echo "    (удаляем лишнее: $(basename "$f"))"
    rm -rf "$f"
  fi
done

echo "==> Собран один исполняемый файл:"
ls -lh "$DIST/Workspace1C"
chmod +x "$DIST/Workspace1C"

# Проверка по самому бинарю: proModules=true — код 2.x действительно вошёл в
# сборку, и лицензия откроет функции сразу после активации. На Linux CLI-команда
# --license-info работает без графической сессии; если бинарь в текущем окружении
# не запускается (нет библиотек Avalonia) — это предупреждение, а не ошибка.
PRO_MODULES_IN_BINARY=""
INFO_OUT="$(timeout 60 "$DIST/Workspace1C" --license-info --json 2>/dev/null || true)"
if [[ -n "$INFO_OUT" ]]; then
  if echo "$INFO_OUT" | grep -q '"proModules":[[:space:]]*true'; then
    PRO_MODULES_IN_BINARY="true"
  else
    PRO_MODULES_IN_BINARY="false"
  fi
fi
if [[ "$PRO_MODULES_IN_BINARY" == "true" ]]; then
  echo "==> proModules=true: функции 2.x в бинаре и открываются лицензией (major=1)."
elif [[ "$PRO_MODULES_IN_BINARY" == "false" ]]; then
  echo "==> proModules=false: это публичная сборка, функции 2.x в ней отсутствуют."
  if [[ "$REQUIRE_PRO" == "1" ]]; then
    echo "!! Бинарь без модулей ПРО — основная программа так выпускаться не должна."
    exit 1
  fi
else
  echo "==> proModules: проверить по бинарю не удалось (не запускается в этом окружении)."
  echo "    Ожидаемая редакция по csproj: HasProModules=$HAS_PRO"
fi

echo "==> Готово. Запуск: $DIST/Workspace1C"
