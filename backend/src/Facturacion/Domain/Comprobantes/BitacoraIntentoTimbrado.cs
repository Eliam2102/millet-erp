using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>Resultado de una llamada al PAC registrada en la bitácora.</summary>
public enum ResultadoIntentoTimbrado : short
{
    /// <summary>El PAC timbró: UUID + sellos.</summary>
    Timbrado = 1,

    /// <summary>Rechazo definitivo del PAC (CFDI40xxx, CSD, pre-vuelo).</summary>
    Fallido = 2,

    /// <summary>Ambiguo: enviado sin respuesta; lo resuelve el <c>TimbradoPendienteWorker</c>.</summary>
    EnProceso = 3,
}

/// <summary>
/// Bitácora de intentos de timbrado ([Decisión 01-G] G4): una fila por llamada
/// al PAC — emisión inicial o reintento — de un <see cref="Comprobante"/>.
/// Append-only; el comprobante conserva sólo el código/mensaje del último
/// intento (<c>TimbradoErrorCodigo/Mensaje</c>), aquí queda el historial
/// completo. La escribe <c>TimbradoEjecutor</c> en la misma transacción que la
/// FSM, así que un rollback total (p.ej. NC de amortización fallida) tampoco
/// deja intentos huérfanos.
/// </summary>
public sealed class BitacoraIntentoTimbrado : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public Guid ComprobanteId { get; private set; }

    /// <summary>Consecutivo 1..N dentro del comprobante.</summary>
    public int IntentoNumero { get; private set; }

    public ResultadoIntentoTimbrado Resultado { get; private set; }

    public string? ErrorCodigo { get; private set; }
    public string? ErrorMensaje { get; private set; }

    /// <summary>Momento de la respuesta del PAC (reloj del sistema, no del SAT).</summary>
    public DateTimeOffset RegistradoAt { get; private set; }

    private BitacoraIntentoTimbrado() { }

    private BitacoraIntentoTimbrado(
        Guid id,
        Guid empresaId,
        Guid comprobanteId,
        int intentoNumero,
        ResultadoIntentoTimbrado resultado,
        string? errorCodigo,
        string? errorMensaje,
        DateTimeOffset registradoAt) : base(id)
    {
        if (comprobanteId == Guid.Empty)
            throw new BusinessRuleException("INTENTO_COMPROBANTE_INVALIDO", "El comprobante es obligatorio.");
        if (intentoNumero <= 0)
            throw new BusinessRuleException("INTENTO_NUMERO_INVALIDO", "El número de intento debe ser positivo.");

        EmpresaId = empresaId;
        ComprobanteId = comprobanteId;
        IntentoNumero = intentoNumero;
        Resultado = resultado;
        ErrorCodigo = errorCodigo;
        ErrorMensaje = errorMensaje;
        RegistradoAt = registradoAt;
    }

    public static BitacoraIntentoTimbrado Registrar(
        Guid empresaId,
        Guid comprobanteId,
        int intentoNumero,
        ResultadoIntentoTimbrado resultado,
        string? errorCodigo,
        string? errorMensaje,
        DateTimeOffset registradoAt) =>
        new(Guid.CreateVersion7(), empresaId, comprobanteId, intentoNumero,
            resultado, errorCodigo, errorMensaje, registradoAt);
}
