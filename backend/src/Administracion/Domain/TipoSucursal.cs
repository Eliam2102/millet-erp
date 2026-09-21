namespace Millet.Administracion.Domain;

/// <summary>
/// Clasificación operativa de una <see cref="Sucursal"/> (F1-ADM-01).
/// Placeholder ficticio — Millet validará los valores reales y su
/// semántica en Fase 2/UI. <b>Almacén NO es un valor de este enum</b>:
/// es una entidad propia (<c>Millet.Almacen</c>, fuera de alcance de
/// F1-ADM-01) en relación 1:N con <see cref="Sucursal"/> — una sucursal
/// puede tener uno o varios almacenes; no son la misma clasificación.
/// </summary>
public enum TipoSucursal : short
{
    Matriz = 0,
    Sucursal = 1,
    Planta = 2,
}
