namespace Workspace1C.Themes
{
    /// <summary>
    /// Глиф чипа запуска, к которому применяется масштаб issue #125.
    /// <see cref="Enterprise"/> — треугольник Play (значок «Предприятие»),
    /// <see cref="Configurator"/> — гаечный ключ (значок «Конфигуратор»).
    /// </summary>
    public enum ChipGlyphKind
    {
        /// <summary>Глиф без масштабирования по набору (прочие значки чипов).</summary>
        None,

        /// <summary>Значок «Предприятие» (Play).</summary>
        Enterprise,

        /// <summary>Значок «Конфигуратор» (Wrench).</summary>
        Configurator
    }

    /// <summary>
    /// Масштаб значка ВНУТРИ кружков запуска (индикаторы в наименовании и чипы
    /// колонки «Действия») по активному набору иконок (issue #125, предложение
    /// 7OH): значки «Предприятие» у Fluent и Material Design и «Конфигуратор»
    /// у Material Design в родной сетке 24×24 занимают заметно меньше места,
    /// чем глифы Windows XP (эталон из обсуждения issue), и потому выглядят
    /// мелкими в кружке. Кружок (18 px) не меняется — укрупняется только
    /// отрисовка значка. Остальные наборы (Outline, macOS, Windows XP, Glass)
    /// и остальные глифы — без изменений; Конфигуратор у Fluent тоже не
    /// меняется (по договорённости в issue).
    /// </summary>
    public static class ChipGlyphScale
    {
        /// <summary>Масштаб «Предприятия» (Play) у наборов Fluent и Material Design.</summary>
        public const double EnterpriseScale = 1.4;

        /// <summary>Масштаб «Конфигуратора» (Wrench) у набора Material Design.</summary>
        public const double ConfiguratorScale = 1.3;

        /// <summary>Множитель размера значка для набора <paramref name="set"/> и глифа <paramref name="kind"/>.</summary>
        public static double Factor(IconSet set, ChipGlyphKind kind) => (set, kind) switch
        {
            // Fluent и Material: «Предприятие» крупнее (issue #125).
            (IconSet.Fluent, ChipGlyphKind.Enterprise) => EnterpriseScale,
            (IconSet.Material, ChipGlyphKind.Enterprise) => EnterpriseScale,
            // Material: «Конфигуратор» крупнее; у Fluent Конфигуратор не меняется.
            (IconSet.Material, ChipGlyphKind.Configurator) => ConfiguratorScale,
            // Windows XP — эталон («как должно быть»); остальные наборы — как раньше.
            _ => 1.0
        };

        /// <summary>Размер значка: базовый размер, умноженный на масштаб набора.</summary>
        public static double Scale(double baseSize, IconSet set, ChipGlyphKind kind)
            => baseSize * Factor(set, kind);
    }
}
