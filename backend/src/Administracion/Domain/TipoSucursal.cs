namespace Millet.Administracion.Domain;

/// <summary>
/// Clasificación operativa de una <see cref="Sucursal"/>.
/// Define el tipo de instalación para el procesamiento y asignación de pedidos (Fase 2 / Proceso 3):
/// <list type="bullet">
///   <item><description><see cref="Taller"/> (1): Taller de sucursal con corte local (mesas CNC/manual, Estatus 15).</description></item>
///   <item><description><see cref="Planta"/> (2): Planta de maquila centralizada (ej. Planta Conkal, Estatus 40/42/67/69/70).</description></item>
/// </list>
/// </summary>
public enum TipoSucursal : short
{
    Taller = 1,
    Planta = 2,
}
