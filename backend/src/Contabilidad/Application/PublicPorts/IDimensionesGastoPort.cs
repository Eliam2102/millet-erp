namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// H1 paso 2 (86e3z5w25): área (Dim2) y equipo (Dim3) de un gasto de caja chica, viáticos o tarjeta capturado en CxP
/// (<c>cuentas_por_pagar.gasto.registrado.v1</c>). Dice si la cuenta del gasto (plantilla vigente, línea de cargo «Gasto», rol
/// <c>GASTO</c> de la sucursal o el genérico) exige área o equipo por la regla C-03, ofrece los centros seleccionables de la sucursal
/// (sus ubicaciones más los CeCo corporativos, V49) y valida el centro capturado. La ubicación (Dim1) no se captura: se deriva.
/// Corre sin el alcance de sucursal de Contabilidad: CxP autoriza la captura con sus propios permisos.
/// </summary>
public interface IDimensionesGastoPort
{
    Task<ExigenciaDimensionesGasto> ExigenciasAsync(Guid empresaId, Guid sucursalId, DateOnly fecha, CancellationToken ct);

    /// <param name="nivel">2 = área, 3 = equipo.</param>
    /// <param name="dim2Id">Con nivel 3, restringe los equipos al área elegida.</param>
    Task<IReadOnlyList<CentroGastoOpcion>> CentrosAsync(
        Guid empresaId, Guid sucursalId, int nivel, Guid? dim2Id, string? q, int limite, CancellationToken ct);

    /// <summary>Null si el centro es un área o equipo activo de la sucursal; si no, el motivo en español.</summary>
    Task<string?> ValidarCentroAsync(Guid empresaId, Guid sucursalId, Guid centroCostoId, CancellationToken ct);
}

/// <param name="CuentaCodigo">Cuenta que cargará el gasto; null si aún no hay plantilla o rol mapeado (entonces no se exige nada).</param>
public sealed record ExigenciaDimensionesGasto(bool ExigeArea, bool ExigeEquipo, string? CuentaCodigo);

/// <param name="Nivel">2 = área, 3 = equipo.</param>
public sealed record CentroGastoOpcion(Guid Id, int Nivel, string Clave, string Nombre, Guid? Dim2Id, string? Dim2Clave);
