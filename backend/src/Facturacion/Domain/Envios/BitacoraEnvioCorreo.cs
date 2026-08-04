using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Envios;

/// <summary>Estado de un intento de envío de CFDI por correo (§4.1 levantamiento).</summary>
public enum EstadoEnvioCorreo : short
{
    /// <summary>Encolado; el worker lo procesará.</summary>
    Pendiente = 1,

    /// <summary>Entregado al servicio de notificación.</summary>
    Enviado = 2,

    /// <summary>Falló; el worker reintenta hasta el máximo configurado.</summary>
    Fallido = 3,
}

/// <summary>
/// Bitácora de envío de un CFDI (factura/NC/REPP) al correo del cliente
/// (§4.1, §12.5 levantamiento). Una fila por intento-lógico de envío de un
/// comprobante a un destinatario; el <see cref="EnvioCfdiCorreoWorker"/> la
/// drena y reintenta.
/// </summary>
public sealed class BitacoraEnvioCorreo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid ComprobanteId { get; private set; }
    public string Destinatario { get; private set; } = string.Empty;

    public EstadoEnvioCorreo Estado { get; private set; }
    public int Intentos { get; private set; }
    public DateTimeOffset? EnviadoAt { get; private set; }
    public string? UltimoError { get; private set; }

    private BitacoraEnvioCorreo() { }

    private BitacoraEnvioCorreo(Guid id, Guid empresaId, Guid comprobanteId, string destinatario) : base(id)
    {
        if (comprobanteId == Guid.Empty)
            throw new BusinessRuleException("ENVIO_COMPROBANTE_INVALIDO", "El comprobante es obligatorio.");
        if (string.IsNullOrWhiteSpace(destinatario) || !destinatario.Contains('@'))
            throw new BusinessRuleException("ENVIO_DESTINATARIO_INVALIDO", "El destinatario debe ser un correo válido.");

        EmpresaId = empresaId;
        ComprobanteId = comprobanteId;
        Destinatario = destinatario.Trim();
        Estado = EstadoEnvioCorreo.Pendiente;
        Intentos = 0;
    }

    public static BitacoraEnvioCorreo Encolar(Guid empresaId, Guid comprobanteId, string destinatario) =>
        new(Guid.CreateVersion7(), empresaId, comprobanteId, destinatario);

    /// <summary>Registra un intento exitoso.</summary>
    public void MarcarEnviado(DateTimeOffset ahora)
    {
        Intentos++;
        Estado = EstadoEnvioCorreo.Enviado;
        EnviadoAt = ahora;
        UltimoError = null;
    }

    /// <summary>Registra un intento fallido (el worker reintenta hasta el máximo).</summary>
    public void MarcarFallido(string error)
    {
        Intentos++;
        Estado = EstadoEnvioCorreo.Fallido;
        UltimoError = error;
    }
}
