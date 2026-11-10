using Millet.Contabilidad.Application.Dimensiones;

namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// H1 pasos 3 y 4 (86e3z5w25): área (Dim2) y equipo (Dim3) capturados en la pantalla de un movimiento de otro módulo cuya póliza
/// automática carga a una cuenta con la regla C-03 (conceptos bancarios que van a gasto, costo de importación). Contabilidad dice
/// qué exige la cuenta, ofrece los centros de la sucursal y valida lo capturado; el centro viaja en el evento y el motor lo pone en
/// la póliza (la ubicación, Dim1, se deriva del área o del equipo). M-450: sin el dato exigido el módulo no acepta el movimiento. Mismo criterio que el paso 1 (<see cref="IDimensionesSalidaInventarioPort"/>), con ids en vez de claves
/// porque aquí captura una persona con selector.
/// </summary>
public interface IDimensionesCapturaPort
{
    /// <summary>Qué exige la regla C-03 de la cuenta que cargará la póliza (sin plantilla, rol o cuenta: nada).</summary>
    Task<ExigenciaDimensiones> ExigenciasAsync(MovimientoCaptura movimiento, CancellationToken ct);

    /// <summary>Áreas (<paramref name="equipos"/> = false) o equipos activos de la sucursal (K10.2/V49: sus ubicaciones y los CeCo corporativos).</summary>
    Task<IReadOnlyList<CentroOpcionDto>> CentrosAsync(Guid empresaId, Guid sucursalId, bool equipos, Guid? areaId, string? q, int limite,
        CancellationToken ct);

    /// <summary>Valida el área y el equipo capturados (activos, de la sucursal, equipo del área) y dice si falta uno que la cuenta exige.</summary>
    Task<DimensionesCapturaResultado> ValidarAsync(MovimientoCaptura movimiento, Guid? areaId, Guid? equipoId, CancellationToken ct);
}

/// <param name="TipoEvento">Evento cuya plantilla vigente genera la póliza.</param>
/// <param name="Campo">Campo de importe de la línea de cargo que lleva el centro (p. ej. <c>Base</c>, <c>GastoImportacion</c>).</param>
/// <param name="Clasificador">Sufijo del rol cuando la línea es por clasificador (concepto bancario); null si no aplica.</param>
public sealed record MovimientoCaptura(Guid EmpresaId, Guid SucursalId, string TipoEvento, string Campo, string? Clasificador, DateOnly Fecha);

public sealed record ExigenciaDimensiones(bool ExigeArea, bool ExigeEquipo, string? CuentaCodigo)
{
    public static readonly ExigenciaDimensiones Ninguna = new(false, false, null);
}

/// <summary>
/// <c>Error</c>: dato inválido (no existe, inactivo, de otra sucursal, equipo de otra área): el movimiento no se acepta.
/// <c>Faltante</c>: la cuenta exige un dato que no se capturó; M-450: el módulo no acepta el movimiento hasta capturarlo.
/// <c>CentroCostoId</c>: el que viaja en el evento (el equipo si se capturó; si no, el área).
/// </summary>
public sealed record DimensionesCapturaResultado(string? Error, string? Faltante, Guid? CentroCostoId, ExigenciaDimensiones Exigencia);
