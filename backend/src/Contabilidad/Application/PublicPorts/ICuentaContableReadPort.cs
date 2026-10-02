using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// Puerto de lectura PÚBLICO de Contabilidad para los consumidores (Facturación, CxP, Almacén,
/// Tesorería): Contabilidad es el servidor y publica el contrato (patrón <c>IDim3ReadPort</c>).
/// La empresa sale de <c>ICurrentEmpresaContext</c>: una cuenta de otra empresa responde
/// <see cref="MotivoRechazoCuenta.NoExiste"/>. Solo lectura: el consumidor nunca escribe.
/// </summary>
// PLATFORM-TODO(<ContabilidadCuentasConsumidores>): cada consumidor define su puerto propio y un adaptador delgado que delegue aquí.
public interface ICuentaContableReadPort
{
    Task<CuentaContableValidacion> ValidarParaMovimientoAsync(string codigoCuenta, OrigenMovimiento origen, CancellationToken ct);
    Task<CuentaContableValidacion> ValidarParaMovimientoAsync(Guid cuentaId, OrigenMovimiento origen, CancellationToken ct);
    Task<CuentaContableLectura?> ObtenerAsync(Guid cuentaId, CancellationToken ct);
}

/// <summary>Mapeo 1:1 a los códigos CONTAB_CUENTA_* (NoExiste, Inactiva, PendienteValidacion, Titulo, ControlSoloAuxiliar).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum MotivoRechazoCuenta { NoExiste, Titulo, Inactiva, ControlSoloAuxiliar, PendienteValidacion }

public sealed record CuentaContableValidacion(bool Valida, MotivoRechazoCuenta? Motivo, CuentaContableLectura? Cuenta);

public sealed record CuentaContableLectura(
    Guid Id, string Codigo, string Nombre, NaturalezaCuenta? Naturaleza, TipoCuenta? Tipo, bool Activa,
    CuentaControl CuentaControl, bool PendienteValidacion);
