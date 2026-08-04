namespace Millet.SharedKernel.Application.Settings;

/// <summary>
/// Tipo de dato del setting expuesto por un módulo vía
/// <see cref="ISettingsSchemaProvider"/>. Permite al frontend renderizar
/// el control correcto (Switch, Input numérico, Select, DatePicker) y al
/// endpoint genérico de PATCH validar el valor entrante.
/// Ver ADR-0034.
/// </summary>
public enum TipoSetting
{
    Booleano = 0,
    Entero = 1,
    Numerico = 2,
    Texto = 3,
    Lista = 4,
    Fecha = 5,
}
