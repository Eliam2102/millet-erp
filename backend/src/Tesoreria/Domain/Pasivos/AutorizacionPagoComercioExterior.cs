using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Pasivos;

public enum TipoOperacionComercioExterior { Importacion = 1, Exportacion = 2 }

/// <summary>
/// I13.3 (86e3ke4cx): autorización en dos niveles del pasivo de comercio exterior. Nivel 1 = Compras de MP
/// (importación) o Documentación de Exportación (exportación); nivel 2 = cualquiera de los dos directores
/// (Administración y Finanzas o Comercialización: «basta con uno», Millet 26-oct). Sin las dos firmas el pasivo
/// no es pagable (<c>PasivosPagables</c>). Una fila por factura, creada al proyectar un pasivo marcado como de
/// comercio exterior; la ausencia de fila significa que el pasivo no es de comercio exterior.
/// </summary>
public sealed class AutorizacionPagoComercioExterior : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid FacturaProveedorId { get; private set; }
    public TipoOperacionComercioExterior Tipo { get; private set; }

    public Guid? Nivel1UsuarioId { get; private set; }
    public DateTimeOffset? Nivel1En { get; private set; }
    public Guid? Nivel2UsuarioId { get; private set; }
    public DateTimeOffset? Nivel2En { get; private set; }

    public Guid? RechazadoPorId { get; private set; }
    public DateTimeOffset? RechazadoEn { get; private set; }
    public string? MotivoRechazo { get; private set; }

    private AutorizacionPagoComercioExterior() { }

    public AutorizacionPagoComercioExterior(Guid empresaId, Guid facturaProveedorId, TipoOperacionComercioExterior tipo)
        : base(Guid.CreateVersion7())
    {
        EmpresaId = empresaId; FacturaProveedorId = facturaProveedorId; Tipo = tipo;
    }

    public bool Autorizada => Nivel1UsuarioId is not null && Nivel2UsuarioId is not null && RechazadoEn is null;
    public bool Rechazada => RechazadoEn is not null;

    /// <summary>Nivel que falta firmar (1 o 2); null si ya está autorizada o rechazada.</summary>
    public int? NivelPendiente => Rechazada || Autorizada ? null : Nivel1UsuarioId is null ? 1 : 2;

    /// <summary>Firma de nivel 1. Tras un rechazo, volver a firmar el nivel 1 reabre el trámite y limpia el nivel 2.</summary>
    public void FirmarNivel1(Guid usuarioId, DateTimeOffset ahora)
    {
        if (Autorizada || (Nivel1UsuarioId is not null && !Rechazada))
            throw new BusinessRuleException("CE_PAGO_NIVEL1_YA_FIRMADO", "El nivel 1 de este pago ya está firmado.");
        Nivel1UsuarioId = usuarioId; Nivel1En = ahora;
        Nivel2UsuarioId = null; Nivel2En = null;
        RechazadoPorId = null; RechazadoEn = null; MotivoRechazo = null;
    }

    public void FirmarNivel2(Guid usuarioId, DateTimeOffset ahora)
    {
        if (Rechazada) throw new BusinessRuleException("CE_PAGO_RECHAZADO", "El pago fue rechazado; el nivel 1 debe volver a firmarlo.");
        if (Nivel1UsuarioId is null) throw new BusinessRuleException("CE_PAGO_FALTA_NIVEL1", "Falta la firma de nivel 1.");
        if (Nivel2UsuarioId is not null) throw new BusinessRuleException("CE_PAGO_NIVEL2_YA_FIRMADO", "El nivel 2 de este pago ya está firmado.");
        if (Nivel1UsuarioId == usuarioId)
            throw new BusinessRuleException("CE_PAGO_MISMA_PERSONA", "Las dos firmas no pueden ser de la misma persona: el nivel 2 lo firma otra persona.");
        Nivel2UsuarioId = usuarioId; Nivel2En = ahora;
    }

    public void Rechazar(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(motivo)) throw new BusinessRuleException("CE_PAGO_MOTIVO", "Indica el motivo del rechazo.");
        if (Autorizada) throw new BusinessRuleException("CE_PAGO_YA_AUTORIZADO", "El pago ya tiene las dos firmas.");
        if (Rechazada) throw new BusinessRuleException("CE_PAGO_RECHAZADO", "El pago ya está rechazado.");
        RechazadoPorId = usuarioId; RechazadoEn = ahora; MotivoRechazo = motivo.Trim();
    }
}
