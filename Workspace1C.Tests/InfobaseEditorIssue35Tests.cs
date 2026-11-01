using System;
using System.Collections.Generic;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты исправления issue #35 «Не обновляет данные строки при отборах»:
/// (1) <see cref="Infobase.NotifyAllPropertiesChanged"/> поднимает
/// <c>PropertyChanged</c> с <c>PropertyName == null</c> (означает «все свойства») —
/// необходимо, потому что при активном отборе контейнер строки не пересоздаётся
/// и привязки не перечитывают значения без явного уведомления;
/// (2) <see cref="InfobaseEditor.ApplyEdits"/> переносит редактируемые поля диалога
/// на существующий объект (ссылка та же), поднимает уведомление и не трогает
/// нередактируемые поля (IsRunning, история запусков).
/// </summary>
public sealed class InfobaseEditorIssue35Tests
{
    [Fact]
    public void Infobase_NotifyAllPropertiesChanged_RaisesAllProperties_Issue35()
    {
        var ib = new Infobase();
        string? received = "событие не поднималось";
        ib.PropertyChanged += (_, e) => received = e.PropertyName;

        ib.NotifyAllPropertiesChanged();

        // PropertyChangedEventArgs(null) — каноническое значение PropertyName,
        // означающее «изменились все свойства»: привязки перечитывают значения заново.
        Assert.Null(received);
    }

    [Fact]
    public void ApplyEdits_CopiesEditableFieldsAndNotifies_Issue35()
    {
        var target = new Infobase
        {
            Name = "Старое имя",
            Group = "Старая группа",
            Description = "Старое описание",
            PlatformVersion = "8.3.20",
            IsFavorite = true,
            IsEnterpriseRunning = true, // нередактируемое служебное поле — должно сохраниться
        };
        var historyRef = target.LaunchHistory;
        target.AddLaunchHistory("Enterprise", maxHistory: 5);

        var result = new Infobase
        {
            Id = "ib-1",
            Name = "Новое имя",
            Group = "Новая группа",
            Description = "Новое описание",
            PlatformVersion = "8.3.27.1644",
            ConfigurationName = "Управление торговлей",
            ConfigurationVersion = "11.5.15.74",
            Architecture = "64",
            LaunchMode = "Тонкий клиент",
            LaunchParameters = "/L ru",
            DefaultLaunchMode = "Enterprise",
            DoubleClickAction = "Enterprise",
            ExternalProcessingPath = string.Empty,
            ExternalProcessingData = string.Empty,
            ClientType = "Тонкий",
            IsFavorite = false,
            IsPinned = true,
            LastLaunchDate = new DateTime(2026, 10, 20, 10, 0, 0),
            Tags = new List<string> { "dev", "web" },
            ManualSizeBytes = 123L,
            MetadataRoot = null,
            AdditionalFields = new List<string> { "A=1", "B=2" },
            IsEnterpriseRunning = false, // в диалоге не редактируется — не должно примениться
        };

        var originalRef = (object)target;
        var notifications = new List<string?>();
        target.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        InfobaseEditor.ApplyEdits(target, result);

        // Объект не заменён новым из диалога: правка применена к существующей ссылке.
        Assert.Same(originalRef, target);
        Assert.NotSame(result, target);

        // Редактируемые поля скопированы.
        Assert.Equal(result.Id, target.Id);
        Assert.Equal(result.Name, target.Name);
        Assert.Equal(result.Group, target.Group);
        Assert.Equal(result.Description, target.Description);
        Assert.Equal(result.PlatformVersion, target.PlatformVersion);
        Assert.Equal(result.ConfigurationName, target.ConfigurationName);
        Assert.Equal(result.ConfigurationVersion, target.ConfigurationVersion);
        Assert.Equal(result.Architecture, target.Architecture);
        Assert.Equal(result.LaunchMode, target.LaunchMode);
        Assert.Equal(result.LaunchParameters, target.LaunchParameters);
        Assert.Equal(result.DefaultLaunchMode, target.DefaultLaunchMode);
        Assert.Equal(result.DoubleClickAction, target.DoubleClickAction);
        Assert.Equal(result.ClientType, target.ClientType);
        Assert.Equal(result.IsFavorite, target.IsFavorite);
        Assert.Equal(result.IsPinned, target.IsPinned);
        Assert.Equal(result.LastLaunchDate, target.LastLaunchDate);
        Assert.Equal(result.ManualSizeBytes, target.ManualSizeBytes);
        Assert.Equal(result.Connection, target.Connection);
        Assert.Equal(result.EnterpriseAuth, target.EnterpriseAuth);
        Assert.Equal(result.ConfiguratorAuth, target.ConfiguratorAuth);
        Assert.Equal(result.Repository, target.Repository);

        // Tags переносятся ссылкой, значения совпадают.
        Assert.Equal(result.Tags, target.Tags);
        // AdditionalFields переносятся копией списка (issue #6) — не общий экземпляр. 
        Assert.NotSame(result.AdditionalFields, target.AdditionalFields);
        Assert.Equal(result.AdditionalFields, target.AdditionalFields);

        // Нередактируемые поля не затронуты: служебный флаг и история запусков.
        Assert.True(target.IsRunning);
        Assert.Same(historyRef, target.LaunchHistory);
        Assert.Single(target.LaunchHistory);

        // Поднято уведомление «все свойства» (PropertyName == null) — строка при активном
        // отборе должна перечитать значения, даже если контейнер не пересоздавался.
        Assert.Contains(null, notifications);
    }
}