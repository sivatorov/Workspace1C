Исправлено в версии 1.0.0.69 (Windows и Linux): окно «Сменить платформу» (массовые действия для выделенных баз) теперь закрывается по Esc, как остальные диалоги.

Для новых диалоговых окон WPF добавлено общее правило — базовый класс [`EscClosableWindow`](Workspace1C/Views/EscClosableWindow.cs): закрытие по Esc без модификаторов в `PreviewKeyDown`; инвариант «сначала подсказка, потом окно» сохраняет глобальный перехватчик [`ToolTipCloser`](Workspace1C/Views/ToolTipCloser.cs). Окно переведено на этот класс ([`PlatformChangeWindow.xaml.cs`](Workspace1C/Views/PlatformChangeWindow.xaml.cs)), кнопке «Отмена» добавлен штатный `IsCancel` ([`PlatformChangeWindow.xaml`](Workspace1C/Views/PlatformChangeWindow.xaml)).

На Linux (Avalonia) это правило уже даёт общий [`ModalWindowBase`](Workspace1C/Views/ModalWindowBase.cs), от которого окно наследуется ([`PlatformChangeWindow.Avalonia.cs`](Workspace1C/Views/PlatformChangeWindow.Avalonia.cs)) — правка не требовалась.

Добавлены юнит-тесты `Issue54EscapeCloseTests` (наследование WPF, разметка `IsCancel`, Avalonia-наследование, подписка `PreviewKeyDown`).

Реализовано на обеих платформах.