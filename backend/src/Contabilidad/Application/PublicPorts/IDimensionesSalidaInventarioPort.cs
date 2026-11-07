namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// H1 (86e3z5w25): área (Dim2) y equipo (Dim3) de una salida de inventario capturada fuera de Contabilidad (app de Almacenes).
/// Resuelve las claves dentro de la sucursal (K10.2/V49: ubicaciones de la sucursal más los CeCo corporativos) y dice si la cuenta
/// que cargará la póliza por esa causa (plantilla vigente de <c>almacen.salida_inventario.registrada.v1</c> + roles de cuenta) exige
/// área o equipo por la regla C-03. La ubicación (Dim1) no se captura: se deriva del área o del equipo, que son de la sucursal.
/// </summary>
public interface IDimensionesSalidaInventarioPort
{
    Task<DimensionesSalidaResultado> ResolverAsync(SolicitudDimensionesSalida solicitud, CancellationToken ct);
}

/// <param name="Causa">La causa del evento (consumo, rotura, daño, salida_manual, salida_no_planificada).</param>
/// <param name="AreaClave">Clave del CeCo (Dim2) capturada; null o vacía = no se capturó.</param>
/// <param name="EquipoClave">Clave del equipo (Dim3) capturada; null o vacía = no se capturó.</param>
/// <param name="TipoEvento">Evento cuya plantilla carga la póliza; null = <c>almacen.salida_inventario.registrada.v1</c>. La rotura de
/// la pantalla de Almacén usa <c>almacen.salida_rotura.registrada.v1</c> (86e3zf04y).</param>
public sealed record SolicitudDimensionesSalida(
    Guid EmpresaId, Guid SucursalId, string Causa, DateOnly Fecha, string? AreaClave, string? EquipoClave, string? TipoEvento = null);

/// <summary>
/// <c>Error</c>: clave inexistente, inactiva, de otra sucursal o equipo de otra área (dato inválido: no se debe aceptar).
/// <c>Faltante</c>: la cuenta exige un dato que no se capturó (quien llama decide si lo rechaza o deja que la póliza vaya a Rechazos).
/// <c>CentroCostoId</c>: el centro que viaja en el evento (el equipo si se capturó; si no, el área).
/// </summary>
public sealed record DimensionesSalidaResultado(
    string? Error, string? Faltante, Guid? CentroCostoId, bool ExigeArea, bool ExigeEquipo, string? CuentaCodigo);
