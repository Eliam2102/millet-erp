using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.EstadoCuenta;

/// <summary>
/// Estado del envío del estado de cuenta (C7.8). Persistido como
/// <c>short</c> con check constraint; valor nuevo = constraint + migración + espejo FE.
/// </summary>
public enum EstadoEnvioEstadoCuenta : short
{
    /// <summary>Registrado sin transporte de correo disponible (NoOp en producción).</summary>
    Registrado = 1,

    /// <summary>El transporte de correo aceptó el mensaje.</summary>
    Enviado = 2,

    /// <summary>El transporte de correo falló; el registro se conserva para auditoría.</summary>
    Fallido = 3,
}

/// <summary>
/// Envío del estado de cuenta al cliente (F1-CXC-09 · C7.8). Rastro
/// auditable de qué se envió, a quién, quién lo hizo y con qué totales;
/// el PDF lo genera el frontend (ADR-0036) y aquí solo se guarda su huella
/// SHA-256. Solo se crea cuando no hay pagos sin REP ni REP sin reflejar
/// (CA7.14).
/// </summary>
public sealed class EstadoCuentaEnvio : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid ClienteId { get; private set; }
    public string Destinatario { get; private set; } = default!;
    public string? Mensaje { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string? UsuarioNombre { get; private set; }
    public DateTimeOffset Fecha { get; private set; }

    /// <summary>Totales por moneda al momento del envío (JSON), nunca convertidos.</summary>
    public string TotalesJson { get; private set; } = "[]";

    public int NumeroFilas { get; private set; }
    public string PdfSha256 { get; private set; } = default!;
    public string NombreArchivo { get; private set; } = default!;
    public long TamanoBytes { get; private set; }
    public EstadoEnvioEstadoCuenta Estado { get; private set; }
    public string? DetalleError { get; private set; }

    private EstadoCuentaEnvio() { }

    public static EstadoCuentaEnvio Registrar(
        Guid empresaId,
        Guid clienteId,
        string destinatario,
        string? mensaje,
        Guid usuarioId,
        string? usuarioNombre,
        DateTimeOffset fecha,
        string totalesJson,
        int numeroFilas,
        string pdfSha256,
        string nombreArchivo,
        long tamanoBytes)
    {
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("ECE_CLIENTE_VACIO", "El cliente es obligatorio.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("ECE_USUARIO_VACIO", "El usuario que envía es obligatorio.");
        if (string.IsNullOrWhiteSpace(destinatario))
            throw new BusinessRuleException("ECE_DESTINATARIO_VACIO", "El destinatario es obligatorio.");
        if (string.IsNullOrWhiteSpace(pdfSha256))
            throw new BusinessRuleException("ECE_PDF_VACIO", "El PDF del estado de cuenta es obligatorio.");

        return new EstadoCuentaEnvio
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            ClienteId = clienteId,
            Destinatario = destinatario.Trim(),
            Mensaje = string.IsNullOrWhiteSpace(mensaje) ? null : mensaje.Trim(),
            UsuarioId = usuarioId,
            UsuarioNombre = usuarioNombre,
            Fecha = fecha.ToUniversalTime(),
            TotalesJson = totalesJson,
            NumeroFilas = numeroFilas,
            PdfSha256 = pdfSha256,
            NombreArchivo = nombreArchivo.Trim(),
            TamanoBytes = tamanoBytes,
            Estado = EstadoEnvioEstadoCuenta.Registrado,
        };
    }

    public void MarcarEnviado()
    {
        Estado = EstadoEnvioEstadoCuenta.Enviado;
        DetalleError = null;
    }

    public void MarcarFallido(string detalle)
    {
        Estado = EstadoEnvioEstadoCuenta.Fallido;
        DetalleError = detalle.Length > 2000 ? detalle[..2000] : detalle;
    }
}
