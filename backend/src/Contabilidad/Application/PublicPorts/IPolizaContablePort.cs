using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// Puerto PÚBLICO de pólizas para el motor contable (C1.4/C1.5). Toda póliza automática pasa por aquí con la misma numeración por
/// tipo (K10.9) y la cancelación de un documento la anula con la misma regla que la manual (K10.3: mismas partidas en negativo).
/// </summary>
// PLATFORM-TODO(<MotorContable>): C1.5 conecta los listeners (p. ej. facturacion.comprobante.cancelado.v1, F6.5) a este puerto.
public interface IPolizaContablePort
{
    /// <summary>
    /// Registra una póliza automática: nace autorizada y numerada. Idempotente por <see cref="SolicitudPolizaAutomatica.LlaveIdempotencia"/>:
    /// la misma llave devuelve la póliza existente. Un periodo que no la admite lanza el rechazo (para la bandeja de rechazos de C1.5).
    /// </summary>
    Task<PolizaRegistrada> RegistrarAutomaticaAsync(SolicitudPolizaAutomatica solicitud, CancellationToken ct);

    /// <summary>
    /// Anula las pólizas autorizadas del documento cancelado (K10.3). Idempotente: una póliza ya anulada no se vuelve a anular y se
    /// devuelve su anulación. Si el periodo de la póliza está cerrado, lanza el rechazo (el motor no elige periodo destino).
    /// </summary>
    Task<IReadOnlyList<PolizaRegistrada>> AnularPorDocumentoOrigenAsync(string modulo, string tipo, Guid documentoId, string motivo, CancellationToken ct);
}

/// <summary>
/// <c>TipoClave</c> = tipo de operación de dos letras que define la plantilla (K10.9: TT para la factura de proveedor).
/// <c>OrigenCuentas</c> = con qué origen se validan las cuentas (las de control solo admiten su auxiliar).
/// </summary>
public sealed record SolicitudPolizaAutomatica(
    string TipoClave, DateOnly Fecha, string Concepto, DocumentoOrigenPoliza Documento, string LlaveIdempotencia,
    IReadOnlyList<PartidaRequest> Partidas, OrigenMovimiento OrigenCuentas = OrigenMovimiento.Manual, Guid? EmpresaId = null);

public sealed record PolizaRegistrada(Guid Id, string Folio, EstadoPoliza Estado, bool Idempotente);
