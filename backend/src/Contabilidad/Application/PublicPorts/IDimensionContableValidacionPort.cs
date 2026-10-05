using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// Puerto público de Contabilidad (F1-CON-02): valida las dimensiones de un movimiento contra las reglas vigentes a su
/// fecha contable, los centros de costo (existencia, estado, jerarquía) y la sucursal del centro. Devuelve TODOS los errores.
/// No verifica que el usuario pueda operar la sucursal: eso es autorización de quien llama (ADR-0051).
/// </summary>
// PLATFORM-TODO(<DimensionesConsumidores>): Pólizas, CxP y Compras definen su puerto propio y un adaptador delgado que delegue aquí.
public interface IDimensionContableValidacionPort
{
    Task<ValidacionDimensiones> ValidarAsync(MovimientoDimensionado movimiento, CancellationToken ct);
}

public sealed record MovimientoDimensionado(
    Guid CuentaId, Guid TipoDocumentoId, DateOnly FechaContable, Guid SucursalId,
    Guid? Dim1Id, Guid? Dim2Id, Guid? Dim3Id, OrigenMovimiento Origen = OrigenMovimiento.Manual);

/// <summary>
/// <c>Campo</c> = nombre del campo de captura que causa el error (cuentaId, tipoDocumentoId, sucursalId, dim1Id, dim2Id, dim3Id),
/// para que la pantalla lo muestre junto a él.
/// </summary>
public sealed record ErrorDimension(string Codigo, string Mensaje, string Campo, DimensionContable? Dimension = null);

/// <summary>
/// Requerimiento efectivo de una dimensión. <c>ReglaId</c> null = sin regla (se aplica <c>SinReglaEs</c>).
/// <c>Heredada</c> = la regla está en una cuenta ancestro; <c>ParaTodosLosTipos</c> = la regla no indica tipo de documento.
/// </summary>
public sealed record RequerimientoEfectivo(
    DimensionContable Dimension, string NombreDimension, RequerimientoDimension Requerimiento, Guid? ReglaId,
    string? CuentaOrigenCodigo, bool Heredada, bool ParaTodosLosTipos, DateOnly? VigenteDesde, DateOnly? VigenteHasta, bool EsPrueba);

/// <summary>Centros efectivos del movimiento (los derivados por jerarquía incluidos).</summary>
public sealed record CentrosEfectivos(Guid? Dim1Id, Guid? Dim2Id, Guid? Dim3Id);

public sealed record ValidacionDimensiones(
    bool Valido, IReadOnlyList<ErrorDimension> Errores, IReadOnlyList<RequerimientoEfectivo> Requerimientos, CentrosEfectivos Centros);
