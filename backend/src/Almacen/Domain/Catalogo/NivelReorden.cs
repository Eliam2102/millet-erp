namespace Millet.Almacen.Domain.Catalogo;

/// <summary>
/// Nivel jerárquico donde vive una <see cref="ConfiguracionReorden"/>
/// (ADR-0047, enmienda 2026-07-06). La política de reorden que <b>dispara</b>
/// el reabasto vive en Nivel 1 (Sucursal) o Nivel 2 (Almacén) — nunca en ambos
/// para el mismo artículo dentro de una misma sucursal (exclusión N1⊕N2,
/// validada en el handler). Nivel 3 y Nivel 4 conservan min/máx pero solo
/// informativo (no disparan).
/// </summary>
public enum NivelReorden : short
{
    /// <summary>Nivel 1 — la configuración aplica a toda una Sucursal.</summary>
    Sucursal = 0,

    /// <summary>Nivel 2 — la configuración aplica a un Almacén específico.</summary>
    Almacen = 1,
}
