using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Proformas.Queries;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Proformas.CompararProformaPedido;

/// <summary>
/// Compara lo que se va a timbrar (el mismo payload de emisión) contra la
/// última proforma del pedido (U1.8a CA-c). Lectura pura: arma la factura en
/// memoria con <see cref="FacturaVentaArmado"/>, sin reservar folio ni
/// persistir. El pedido viene de la ruta; el del cuerpo se ignora.
/// </summary>
public sealed record CompararProformaPedidoQuery(Guid PedidoFacturableId, EmitirFacturaVentaCommand Emision)
    : IRequest<ComparacionProformaResponse>;

/// <summary>
/// Resultado de la comparación. <see cref="HayProforma"/> = false (sin
/// diferencias) si el pedido nunca tuvo proforma.
/// </summary>
public sealed record ComparacionProformaResponse(
    bool HayProforma,
    string? Folio,
    int? Version,
    DateTimeOffset? GeneradaEn,
    IReadOnlyList<DiferenciaProforma> Diferencias);

public sealed class CompararProformaPedidoValidator : AbstractValidator<CompararProformaPedidoQuery>
{
    public CompararProformaPedidoValidator()
    {
        RuleFor(q => q.PedidoFacturableId).NotEmpty();
        RuleFor(q => q.Emision).NotNull();
        RuleFor(q => q.Emision.Lineas).NotEmpty().When(q => q.Emision is not null);
    }
}

public sealed class CompararProformaPedidoHandler
    : IRequestHandler<CompararProformaPedidoQuery, ComparacionProformaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;
    private readonly IEmpresaFiscalReadPort _empresasFiscal;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IClock _clock;

    public CompararProformaPedidoHandler(
        FacturacionDbContext db,
        IAlcanceCajaEvaluator alcance,
        IEmpresaFiscalReadPort empresasFiscal,
        ICurrentEmpresaContext empresa,
        IClock clock)
    {
        _db = db;
        _alcance = alcance;
        _empresasFiscal = empresasFiscal;
        _empresa = empresa;
        _clock = clock;
    }

    public async Task<ComparacionProformaResponse> Handle(
        CompararProformaPedidoQuery query, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "No hay empresa seleccionada en el contexto del request.");

        var pedido = await ProformaAlcance.AsegurarPedidoVisibleAsync(_db, _alcance, query.PedidoFacturableId, cancellationToken);

        var ultima = await _db.ProformasPedido.AsNoTracking()
            .Where(p => p.PedidoFacturableId == pedido.Id)
            .OrderByDescending(p => p.NumeroVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (ultima is null)
            return new ComparacionProformaResponse(false, null, null, null, []);

        // La proforma no lleva Complemento de Comercio Exterior (anti-scope
        // U1.8a): se compara sin él para no exigir el domicilio del CCE aquí.
        var emision = query.Emision with { PedidoFacturableId = pedido.Id, Cce = null };
        var emisor = await EmisorSnapshot.ResolverAsync(_empresasFiscal, empresaId, cancellationToken);
        var ahora = _clock.UtcNow;
        var factura = FacturaVentaArmado.Construir(
            emision, empresaId, ProformaEmisionDesdePedido.FolioArmado, 0, null,
            FacturaVentaArmado.ReceptorDe(emision), emisor, ahora.Year, ahora.Month);
        var timbrado = ProformaCfdiMapper.Desde(factura, ahora,
            ProformaEmisionDesdePedido.SeriePrevista(pedido.EsVentaDirectaManual));

        var anterior = ProformaJson.LeerContenido(ultima.ContenidoJson);
        return new ComparacionProformaResponse(
            true, ultima.Folio, ultima.NumeroVersion, ultima.GeneradaEn,
            ProformaComparador.Comparar(anterior, timbrado));
    }
}
