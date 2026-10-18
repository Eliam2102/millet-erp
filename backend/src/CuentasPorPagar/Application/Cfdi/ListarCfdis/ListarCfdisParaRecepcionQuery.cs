using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.ListarCfdis;

/// <summary>
/// CFDI por procesar del RFC de un proveedor, para vincularlos a una recepción por OC.
/// El RFC lo resuelve el endpoint de Almacén a partir de la OC, después de verificar el alcance.
/// </summary>
public sealed record ListarCfdisParaRecepcionQuery(string? RfcProveedor) : IRequest<CfdisParaRecepcionResponse>;

/// <summary>Respuesta del selector de CFDI. <c>Aviso</c> explica por qué no hay ítems; null cuando el RFC es válido.</summary>
public sealed record CfdisParaRecepcionResponse(string? RfcEmisor, string? Aviso, IReadOnlyList<CfdiListItemResponse> Items);

public sealed class ListarCfdisParaRecepcionHandler(CuentasPorPagarDbContext db)
    : IRequestHandler<ListarCfdisParaRecepcionQuery, CfdisParaRecepcionResponse>
{
    private const string RfcGenericoNacional = "XAXX010101000";
    private const string RfcGenericoExtranjero = "XEXX010101000";

    public async Task<CfdisParaRecepcionResponse> Handle(ListarCfdisParaRecepcionQuery query, CancellationToken cancellationToken)
    {
        var texto = query.RfcProveedor?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(texto))
            return Sin("El proveedor no tiene RFC registrado; captura el folio fiscal (UUID del impreso).");
        if (texto is RfcGenericoNacional or RfcGenericoExtranjero)
            return Sin("El proveedor tiene RFC genérico; captura el folio fiscal (UUID del impreso).");

        RfcMexicano rfc;
        try
        {
            rfc = RfcMexicano.Parse(texto);
        }
        catch (BusinessRuleException)
        {
            return Sin("El RFC del proveedor no tiene un formato válido; corrígelo en el catálogo o captura el folio fiscal (UUID del impreso).");
        }

        var items = await db.CfdisRecibidos.AsNoTracking()
            .Where(c => c.Estado == EstadoCfdiRecibido.PorProcesar && c.Tipo == TipoCfdi.Ingreso && c.RfcEmisor == rfc)
            .OrderByDescending(c => c.FechaRecepcion)
            .Take(200)
            .Select(c => new CfdiListItemResponse(
                c.Id,
                c.UuidCfdi.Valor,
                c.RfcEmisor.Valor,
                c.Tipo,
                c.Folio,
                c.Serie,
                c.FechaCfdi,
                c.Total,
                c.Subtotal,
                c.ImpuestosTrasladados,
                c.Retenciones,
                c.TipoCambio,
                c.Moneda,
                c.CanalOrigen,
                c.FechaRecepcion,
                c.Estado))
            .ToListAsync(cancellationToken);

        return new CfdisParaRecepcionResponse(rfc.Valor, null, items);
    }

    private static CfdisParaRecepcionResponse Sin(string aviso) => new(null, aviso, []);
}
