namespace Millet.Tesoreria.Domain.Ports.DatosMaestros;

/// <summary>
/// Datos del proveedor que la bandeja de pasivos y el flujo de pago
/// necesitan (TES-PR3, [T-G1]): identificación para mostrar (ADR-0042) y
/// cuentas bancarias para ejecutar la transferencia. La CLABE viaja
/// COMPLETA dentro del server; el masking se aplica en la capa de
/// queries/DTOs salvo permiso
/// <c>tesoreria.movimientos.ver-cuenta-completa</c>.
/// </summary>
public sealed record ProveedorBancoDto(
    Guid Id,
    string Clave,
    string RazonSocial,
    IReadOnlyList<CuentaProveedorDto> Cuentas,
    bool Activo = true,
    bool EnRevision = false, string? Rfc = null,
    bool Bloqueado = false, string? MotivoBloqueo = null)
{
    /// <summary>Cuenta a la que se paga en esa moneda: la predeterminada activa, o la única activa.</summary>
    public CuentaProveedorDto? CuentaPara(string moneda)
    {
        var activas = Cuentas.Where(c => c.Activa && c.Moneda == moneda).ToList();
        return activas.FirstOrDefault(c => c.Predeterminada) ?? (activas.Count == 1 ? activas[0] : null);
    }
}

/// <summary>
/// Cuenta bancaria del proveedor (86e3pdmee). <c>Estado</c>: «Activa»,
/// «PorValidar» o «Inhabilitada» (las rechazadas no viajan). Solo se paga a
/// una activa. <c>Numero</c> viaja completo; se enmascara en los DTOs.
/// </summary>
public sealed record CuentaProveedorDto(
    Guid Id,
    string Banco,
    string Numero,
    bool EsClabe,
    string? Swift,
    string Moneda,
    string Titular,
    bool Predeterminada,
    string Estado)
{
    public bool Activa => Estado == "Activa";
}

/// <summary>
/// Puerto de lectura del master de proveedores de DatosMaestros
/// (TES-PR3). Resuelve el <c>PLATFORM-TODO(PayloadEnriquecido)</c> de CxP:
/// el evento de pasivo no trae datos bancarios; Tesorería los lee por
/// aquí al mostrar la bandeja y al ejecutar el pago. Nunca JOIN físico
/// (ADR-0042).
/// </summary>
public interface IProveedorBancoReadPort
{
    /// <summary>Datos de un proveedor; <c>null</c> si no existe.</summary>
    Task<ProveedorBancoDto?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolución en lote para una página de bandeja: devuelve solo los
    /// encontrados, indexados por id.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(
        IReadOnlyCollection<Guid> proveedorIds, CancellationToken cancellationToken);
}
