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

/// <summary>
/// Motivo del rechazo. <c>Titulo</c> = la cuenta acumula (P19); <c>ControlSoloAuxiliar</c> = cuenta colectiva que solo se afecta
/// desde su módulo (P23); <c>Rubro</c> = agrupación de reporte, nunca recibe movimientos (P24).
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum MotivoRechazoCuenta { NoExiste, Titulo, Inactiva, ControlSoloAuxiliar, PendienteValidacion, Rubro, NoAfectableManual }

public sealed record CuentaContableValidacion(bool Valida, MotivoRechazoCuenta? Motivo, CuentaContableLectura? Cuenta)
{
    public string? Codigo => Motivo == MotivoRechazoCuenta.NoAfectableManual ? "CUENTA_NO_AFECTABLE_MANUAL" : null;
    public string? Mensaje => Motivo == MotivoRechazoCuenta.NoAfectableManual
        ? "La cuenta no admite asientos manuales. Utilice el movimiento del módulo correspondiente." : null;
}

public sealed record CuentaContableLectura(
    Guid Id, string Codigo, string Nombre, NaturalezaCuenta? Naturaleza, TipoCuenta? Tipo, bool Activa,
    CuentaControl CuentaControl, bool PendienteValidacion, bool NoAfectableManual = false);
