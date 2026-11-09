using MediatR;

namespace Millet.CuentasPorPagar.Application.Cfdi.SimularCfdi;

/// <summary>
/// Comando para generar un XML CFDI 4.0 sintético de prueba a partir de una orden de compra
/// e ingresarlo al ERP en estado <c>PorProcesar</c> vía <c>IngresarCfdiCommand</c> (canal Mailbox).
/// Solo disponible en entornos fuera de producción (!IsProduction()).
/// </summary>
public sealed record GenerarEIngresarCfdiPruebaCommand : IRequest<GenerarEIngresarCfdiPruebaResponse>
{
    public Guid OrdenCompraId { get; init; }
    public IReadOnlyDictionary<int, decimal>? CantidadesPorPosicion { get; init; }
    public IReadOnlyDictionary<Guid, decimal>? CantidadesPorLineaId { get; init; }

    public GenerarEIngresarCfdiPruebaCommand(
        Guid ordenCompraId,
        IReadOnlyDictionary<int, decimal>? cantidadesPorPosicion = null,
        IReadOnlyDictionary<Guid, decimal>? cantidadesPorLineaId = null)
    {
        OrdenCompraId = ordenCompraId;
        CantidadesPorPosicion = cantidadesPorPosicion;
        CantidadesPorLineaId = cantidadesPorLineaId;
    }

    public GenerarEIngresarCfdiPruebaCommand(
        Guid ordenCompraId,
        IReadOnlyDictionary<Guid, decimal>? cantidadesPorLineaId)
        : this(ordenCompraId, null, cantidadesPorLineaId)
    {
    }
}

/// <summary>
/// Respuesta con el resultado del CFDI de prueba generado o recuperado por idempotencia.
/// </summary>
public sealed record GenerarEIngresarCfdiPruebaResponse(
    Guid CfdiRecibidoId,
    string UuidCfdi,
    string Folio,
    decimal Total)
{
    public Guid Id => CfdiRecibidoId;
    public string Uuid => UuidCfdi;
}
