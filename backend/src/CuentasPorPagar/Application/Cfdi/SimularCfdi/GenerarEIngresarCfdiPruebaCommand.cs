using MediatR;

namespace Millet.CuentasPorPagar.Application.Cfdi.SimularCfdi;

/// <summary>
/// Comando para generar un XML CFDI 4.0 sintético de prueba a partir de una orden de compra
/// e ingresarlo al ERP en estado <c>PorProcesar</c> vía <c>IngresarCfdiCommand</c> (canal Mailbox).
/// Solo disponible en entornos fuera de producción (!IsProduction()).
/// </summary>
public sealed record GenerarEIngresarCfdiPruebaCommand(
    Guid OrdenCompraId,
    IReadOnlyDictionary<int, decimal>? CantidadesPorPosicion = null) : IRequest<GenerarEIngresarCfdiPruebaResponse>;

/// <summary>
/// Respuesta con el resultado del CFDI de prueba generado o recuperado por idempotencia.
/// </summary>
public sealed record GenerarEIngresarCfdiPruebaResponse(
    Guid CfdiRecibidoId,
    string UuidCfdi,
    string Folio,
    decimal Total);
