using System;
using System.Collections.Generic;

namespace Workspace1C.Services;

/// <summary>
/// Построитель bash-сценария самоперезапуска после обновления (issue #89, v1.0.1.24).
/// Вынесен из <see cref="UpdateService"/> в чистый статический класс без I/O, чтобы
/// покрываться юнит-тестами без сети и процессов.
/// <para>
/// Регресс v1.0.1.23 (репорт 7OH): обновление заменяло бинарник из живого процесса и
/// запускало новый ДО выхода старого — новый экземпляр натыкался на файловый лок
/// одиночного экземпляра и/или наследовал изменённое окружение (LD_LIBRARY_PATH/DOTNET_*)
/// и падал, вывод терялся. Сценарий теперь: ожидание выхода старого процесса → бэкап
/// прежнего бинаря → замена → перезапуск с ЧИСТЫМ окружением и перехватом вывода в
/// журнал → liveness-проверка 10 с → автооткат при падении.
/// </para>
/// </summary>
internal static class UpdateRestartScriptBuilder
{
    /// <summary>Суффикс файла-бэкапа прежнего бинаря рядом с целью.</summary>
    public const string BackupSuffix = ".cm-update-backup";

    /// <summary>Сколько секунд сценарий наблюдает за перезапущенным процессом перед решением об откате.</summary>
    public const int LivenessCheckSeconds = 10;

    /// <summary>
    /// Строит путь к файлу-бэкапу для исполняемого файла: «<c><exe>.cm-update-backup</c>».
    /// Чистая функция (для тестов и удаления бэкапа при успешном старте).
    /// </summary>
    public static string GetBackupPath(string executablePath) => executablePath + BackupSuffix;

    /// <summary>
    /// Строит тело bash-сценария: ожидание выхода процесса <paramref name="currentPid"/>,
    /// замена <paramref name="target"/> файлом <paramref name="newBinary"/>, при
    /// <paramref name="restart"/> — перезапуск с чистым окружением, выводом в
    /// <paramref name="restartLogPath"/> и автооткатом из бэкапа при падении нового процесса.
    /// </summary>
    /// <param name="target">Полный путь к заменяемому исполняемому файлу.</param>
    /// <param name="newBinary">Полный путь к скачанному бинарю обновления.</param>
    /// <param name="currentPid">PID текущего (заменяемого) процесса.</param>
    /// <param name="restart">true — «Перезапустить сейчас»; false — «Обновить после закрытия».</param>
    /// <param name="logPath">Журнал самого помощника (замена/перезапуск/откат).</param>
    /// <param name="restartLogPath">Журнал, куда перенаправляются stdout/stderr нового процесса.</param>
    public static string Build(
        string target, string newBinary, int currentPid, bool restart,
        string logPath, string restartLogPath)
    {
        // Предел ожидания задаётся только режиму «перезапустить сейчас»: там
        // пользователь ждёт приложение обратно. В режиме «после закрытия» помощник
        // ждёт столько, сколько работает приложение.
        var waitCondition = restart
            ? "kill -0 \"$PID_TARGET\" 2>/dev/null && [ $i -lt 300 ]"
            : "kill -0 \"$PID_TARGET\" 2>/dev/null";

        var restartBlock = restart
            ? BuildRestartBlock(restartLogPath)
            : ": # Перезапуск не требуется, обновление применится при следующем закрытии.";

        return $@"#!/usr/bin/env bash
set -u
TARGET='{Bq(target)}'
NEW='{Bq(newBinary)}'
STAGED=""$TARGET.cm-update-$$""
BACKUP='{Bq(GetBackupPath(target))}'
PID_TARGET={currentPid}
RESTART={(restart ? 1 : 0)}
RESTART_LOG='{Bq(restartLogPath)}'
LOG='{Bq(logPath)}'

# Весь вывод уходит в журнал: приложение к этому моменту закрыто, его каналы
# закрыты вместе с ним, и без журнала неудачная замена не оставляет следов.
# Если журнал открыть не удалось, вывод уводится в никуда, и это обязательно:
# унаследованные потоки ведут в трубу закрывшегося приложения, и первая же
# запись в неё убила бы помощника сигналом PIPE до замены файла.
if ! exec >>""$LOG"" 2>&1; then
  exec >/dev/null 2>&1
else
  # Права журнала не должны зависеть от umask сборки: в нём пути пользователя.
  chmod 600 ""$LOG"" 2>/dev/null || true
fi

log() {{ echo ""[$(date '+%Y-%m-%d %H:%M:%S')] $*""; }}

log ""=== помощник обновления, pid $$, режим RESTART=$RESTART""
log ""цель: $TARGET""
log ""новый файл: $NEW, размер $(stat -c%s ""$NEW"" 2>/dev/null || echo '?') байт""
FREE_KB=$(df -Pk ""$(dirname ""$TARGET"")"" 2>/dev/null | awk 'NR==2 {{print $4}}')
log ""свободно в каталоге цели: ${{FREE_KB:-?}} КБ""

# Ожидание завершения основного процесса, чтобы не было гонки при замене файла
# (issue #89, v1.0.1.24): прежний путь «замена из живого процесса + немедленный
# StartDetached» приводил к падению нового экземпляра на локе одиночного
# запуска и на унаследованном окружении.
i=0
while {waitCondition}; do
  sleep 1
  i=$((i+1))
done
if kill -0 ""$PID_TARGET"" 2>/dev/null; then
  log ""предупреждение: процесс $PID_TARGET всё ещё работает после $i с, продолжаем замену""
else
  log ""процесс $PID_TARGET завершился, ожидание заняло $i с""
fi
sleep 1

# Бэкап прежнего бинаря ДО замены: при падении перезапущенного процесса сценарий
# восстановит рабочую версию (автооткат). Если бэкап не удался, замена продолжается,
# но откат будет невозможен — это честно фиксируется в журнале.
if [ ""$RESTART"" = ""1"" ] && [ -f ""$TARGET"" ]; then
  if cp -f ""$TARGET"" ""$BACKUP""; then
    log ""бэкап прежнего бинаря создан: $BACKUP""
  else
    log ""предупреждение: бэкап $BACKUP создать не удалось — автооткат будет невозможен""
  fi
fi

# Замена в два шага: сначала копия рядом с целью, затем атомарное переименование.
# Так недокачанный или недокопированный файл никогда не окажется на месте рабочего.
if ! cp -f ""$NEW"" ""$STAGED""; then
  log ""ошибка: не удалось скопировать новый файл в $STAGED""
  rm -f ""$STAGED""
  exit 1
fi
if ! chmod +x ""$STAGED""; then
  log ""ошибка: не удалось выставить признак исполняемого для $STAGED""
  rm -f ""$STAGED""
  exit 1
fi
if ! mv -f ""$STAGED"" ""$TARGET""; then
  log ""ошибка: не удалось переименовать $STAGED в $TARGET""
  rm -f ""$STAGED""
  exit 1
fi
log ""замена файла выполнена""

{restartBlock}

# Убираем временный бинарник, сам скрипт и опустевший каталог обновления.
WORK_DIR=""$(dirname ""$0"")""
rm -f ""$NEW""
log ""=== помощник закончил работу""
rm -f ""$0""
# Запасной журнал лежит в рабочем каталоге и после успеха не нужен: иначе
# каталог не удаляется и копится по одному на каждое обновление. Журнал
# перезапуска ($RESTART_LOG) лежит в каталоге данных и не удаляется — в него
# пишет перезапущенное приложение ещё долго после выхода помощника.
case ""$LOG"" in
  ""$WORK_DIR""/*) rm -f ""$LOG"" ;;
esac
rmdir ""$WORK_DIR"" 2>/dev/null || true
";
    }

    /// <summary>
    /// Блок перезапуска нового процесса: очистка «вредного» окружения, запуск с
    /// маркером <c>CM_UPDATE_CHILD=1</c>, перехват вывода в журнал и автооткат,
    /// если процесс умер в течение <see cref="LivenessCheckSeconds"/> секунд.
    /// </summary>
    private static string BuildRestartBlock(string restartLogPath)
    {
        _ = restartLogPath; // путь уже зашит в переменную RESTART_LOG шапки сценария
        return $@"# Перезапуск приложения (только по явному запросу пользователя).
# Чистое окружение (issue #89, v1.0.1.24): новый процесс не должен наследовать
# переменные, которыми приложение окружает ЗАПУСКАЕМЫЕ им программы 1С и которые
# может оставить себе среда запуска самого приложения (LD_LIBRARY_PATH с
# монтированиями 1С, любые DOTNET_*/COREHOST_*). Ручной запуск из окружения
# рабочего стола этих переменных не имеет — уравниваем контексты. Сессионные
# переменные (DISPLAY/WAYLAND/XDG_*) сохраняются: без них приложение не найдёт
# дисплей и молча закроется.
for VAR in $(env | cut -d= -f1); do
  case ""$VAR"" in
    DOTNET_*|COREHOST_*|LD_LIBRARY_PATH|LD_PRELOAD|LD_AUDIT) unset ""$VAR"" ;;
  esac
done

# Вывод перезапущенного приложения (включая стек сбоя старта) дописывается в
# журнал перезапуска: раньше он терялся вместе с каналом закрывшегося родителя,
# и диагностика «FileNotFoundException на старте» была невозможна.
nohup env CM_UPDATE_CHILD=1 ""$TARGET"" >>""$RESTART_LOG"" 2>&1 &
NEW_PID=$!

# Liveness-проверка (issue #89, v1.0.1.24): процесс, упавший на старте (например,
# на загрузке сборки), должен вернуть РАБОЧУЮ версию до того, как пользователь
# сам полезет запускать приложение вручную.
ALIVE=0
i=0
while [ $i -lt {LivenessCheckSeconds} ]; do
  sleep 1
  i=$((i+1))
  if kill -0 ""$NEW_PID"" 2>/dev/null; then
    ALIVE=1
    break
  fi
done
if [ ""$ALIVE"" = ""1"" ]; then
  log ""перезапуск: процесс $NEW_PID работает (проверка после ${{i}} с)""
else
  log ""ошибка: перезапущенный процесс завершился в течение {LivenessCheckSeconds} с — откат""
  if [ -f ""$BACKUP"" ]; then
    if mv -f ""$BACKUP"" ""$TARGET""; then
      log ""ROLLBACK: прежний бинарь восстановлен, запускаю старую версию""
      nohup ""$TARGET"" >>""$RESTART_LOG"" 2>&1 &
      log ""старая версия запущена (pid $!)""
    else
      log ""ROLLBACK не удался: не удалось восстановить $BACKUP""
    fi
  else
    log ""ROLLBACK невозможен: бэкапа $BACKUP нет""
  fi
fi";
    }

    /// <summary>Экранирует строку для одинарных кавычек bash: ' → '\''.</summary>
    private static string Bq(string value) => value.Replace("'", "'\\''");
}
