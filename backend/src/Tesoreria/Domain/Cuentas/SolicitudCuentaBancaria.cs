using System.Text.Json;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Cuentas;

/// <summary>Datos completos de una cuenta bancaria en una versión (lo que se propone y lo que queda vigente).</summary>
public sealed record DatosCuentaBancaria(
    string Banco, string NumeroCuenta, string? Clabe, string Moneda, string? CuentaContableRef, string? PerfilExtracto,
    string? Sucursal, string? Finalidad, string? Titular, string? Firmantes);

public enum TipoSolicitudCuenta : short { Alta = 1, Cambio = 2 }

public enum EstadoSolicitudCuenta : short { Pendiente = 1, Aprobada = 2, Rechazada = 3 }

/// <summary>
/// M-199 (Javier Canché, 9 al 11-nov): el alta o cambio de una cuenta bancaria propia lo captura el Contador General y
/// lo autoriza el Director de Planeación Financiera. Nada cambia en la cuenta hasta aprobar; quien solicita no aprueba.
/// Las solicitudes aprobadas de una cuenta son su historial de versiones (datos completos, quién aprobó y cuándo).
/// </summary>
public sealed class SolicitudCuentaBancaria : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid EmpresaId { get; set; }
    public TipoSolicitudCuenta Tipo { get; private set; }
    /// <summary>Nula en un alta pendiente o rechazada; al aprobar el alta toma el id de la cuenta creada.</summary>
    public Guid? CuentaBancariaId { get; private set; }
    /// <summary>Versión de concurrencia de la cuenta al solicitar el cambio: si la cuenta cambió después, la solicitud ya no aplica.</summary>
    public int? VersionCuenta { get; private set; }
    public string? DatosVigentesJson { get; private set; }
    public string DatosPropuestosJson { get; private set; } = default!;
    public EstadoSolicitudCuenta Estado { get; private set; } = EstadoSolicitudCuenta.Pendiente;
    public Guid SolicitadoPor { get; private set; }
    public DateTimeOffset SolicitadoEn { get; private set; }
    public Guid? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoEn { get; private set; }
    public string? MotivoRechazo { get; private set; }

    private SolicitudCuentaBancaria() { }

    public DatosCuentaBancaria DatosPropuestos => JsonSerializer.Deserialize<DatosCuentaBancaria>(DatosPropuestosJson, Json)!;
    public DatosCuentaBancaria? DatosVigentes => DatosVigentesJson is null ? null : JsonSerializer.Deserialize<DatosCuentaBancaria>(DatosVigentesJson, Json);

    public static DatosCuentaBancaria Datos(CuentaBancaria c) => new(c.Banco, c.NumeroCuenta, c.Clabe, c.Moneda, c.CuentaContableRef,
        c.PerfilExtracto, c.Sucursal, c.Finalidad, c.Titular, c.Firmantes);

    public static SolicitudCuentaBancaria Alta(Guid empresaId, DatosCuentaBancaria propuestos, Guid usuarioId, DateTimeOffset ahora) =>
        Nueva(empresaId, TipoSolicitudCuenta.Alta, null, null, null, propuestos, usuarioId, ahora);

    public static SolicitudCuentaBancaria Cambio(CuentaBancaria cuenta, DatosCuentaBancaria propuestos, Guid usuarioId, DateTimeOffset ahora)
    {
        var vigentes = Datos(cuenta);
        if (vigentes == propuestos)
            throw new BusinessRuleException("CTA_SOLICITUD_SIN_CAMBIOS", "La solicitud no cambia ningún dato de la cuenta.");
        return Nueva(cuenta.EmpresaId, TipoSolicitudCuenta.Cambio, cuenta.Id, cuenta.Version, vigentes, propuestos, usuarioId, ahora);
    }

    private static SolicitudCuentaBancaria Nueva(Guid empresaId, TipoSolicitudCuenta tipo, Guid? cuentaId, int? version,
        DatosCuentaBancaria? vigentes, DatosCuentaBancaria propuestos, Guid usuarioId, DateTimeOffset ahora)
    {
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("CTA_SOLICITUD_USUARIO_VACIO", "El usuario que solicita es obligatorio.");
        return new SolicitudCuentaBancaria
        {
            Id = Guid.CreateVersion7(), EmpresaId = empresaId, Tipo = tipo, CuentaBancariaId = cuentaId, VersionCuenta = version,
            DatosVigentesJson = vigentes is null ? null : JsonSerializer.Serialize(vigentes, Json),
            DatosPropuestosJson = JsonSerializer.Serialize(propuestos, Json),
            SolicitadoPor = usuarioId, SolicitadoEn = ahora,
        };
    }

    /// <summary>Aprueba. En alta crea la cuenta (la regresa para agregarla); en cambio aplica los datos a <paramref name="cuenta"/>.</summary>
    public CuentaBancaria? Aprobar(Guid usuarioId, DateTimeOffset ahora, CuentaBancaria? cuenta)
    {
        ExigirPendiente();
        if (usuarioId == Guid.Empty || usuarioId == SolicitadoPor)
            throw new BusinessRuleException("CTA_SOLICITUD_MISMO_USUARIO", "Quien solicitó el cambio no puede autorizarlo.");
        var d = DatosPropuestos;
        CuentaBancaria? nueva = null;
        if (Tipo == TipoSolicitudCuenta.Alta)
        {
            nueva = new CuentaBancaria(EmpresaId, d.Banco, d.NumeroCuenta, d.Clabe, d.Moneda, d.CuentaContableRef, d.PerfilExtracto);
            nueva.ActualizarInventario(d.Sucursal, d.Finalidad, d.Titular, d.Firmantes);
            CuentaBancariaId = nueva.Id;
        }
        else
        {
            if (cuenta is null || cuenta.Id != CuentaBancariaId)
                throw new BusinessRuleException("CTA_SOLICITUD_CUENTA_INVALIDA", "La cuenta de la solicitud no coincide.");
            if (cuenta.Version != VersionCuenta)
                throw new BusinessRuleException("CTA_SOLICITUD_DESACTUALIZADA",
                    "La cuenta cambió después de la solicitud; recházala y captura una nueva.");
            cuenta.ActualizarDatos(d.Banco, d.Moneda, d.CuentaContableRef, d.PerfilExtracto);
            cuenta.CambiarClabe(d.Clabe);
            cuenta.ActualizarInventario(d.Sucursal, d.Finalidad, d.Titular, d.Firmantes);
        }
        Estado = EstadoSolicitudCuenta.Aprobada; ResueltoPor = usuarioId; ResueltoEn = ahora;
        return nueva;
    }

    public void Rechazar(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        ExigirPendiente();
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CTA_SOLICITUD_MOTIVO_OBLIGATORIO", "El motivo del rechazo es obligatorio.");
        Estado = EstadoSolicitudCuenta.Rechazada; ResueltoPor = usuarioId; ResueltoEn = ahora; MotivoRechazo = motivo.Trim();
    }

    private void ExigirPendiente()
    {
        if (Estado != EstadoSolicitudCuenta.Pendiente)
            throw new BusinessRuleException("CTA_SOLICITUD_NO_PENDIENTE", "La solicitud ya fue resuelta.");
    }
}
