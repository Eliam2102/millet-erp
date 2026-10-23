using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Proformas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Proformas.GenerarProformaPedido;

/// <summary>
/// Genera la proforma de un pedido (U1.8a). Lee el pedido con
/// <see cref="PedidoFacturableDetalleQuery"/> (respeta el alcance de cajas y
/// trae los datos fiscales vivos de los masters), lo traduce al comando de
/// emisión con las reglas del formulario y arma la factura en memoria con
/// <see cref="FacturaVentaArmado"/> — el mismo armado del timbrado — SIN
/// reservar folio fiscal (<c>Compartido.Series</c>) ni guardar la factura.
/// Persiste sólo la versión nueva de <see cref="ProformaPedido"/>.
/// </summary>
public sealed class GenerarProformaPedidoHandler
    : IRequestHandler<GenerarProformaPedidoCommand, ProformaPedidoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ISender _sender;
    private readonly IEmpresaFiscalReadPort _empresasFiscal;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;

    public GenerarProformaPedidoHandler(
        FacturacionDbContext db,
        ISender sender,
        IEmpresaFiscalReadPort empresasFiscal,
        ICurrentEmpresaContext empresa,
        ICurrentUserContext user,
        IClock clock)
    {
        _db = db;
        _sender = sender;
        _empresasFiscal = empresasFiscal;
        _empresa = empresa;
        _user = user;
        _clock = clock;
    }

    public async Task<ProformaPedidoResponse> Handle(
        GenerarProformaPedidoCommand command, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "No hay empresa seleccionada en el contexto del request.");

        var pedido = await _sender.Send(new PedidoFacturableDetalleQuery(command.PedidoFacturableId), cancellationToken);
        if (pedido.Estado != nameof(EstadoPedidoFacturable.Importado))
            throw new BusinessRuleException(
                "FAC_PROFORMA_PEDIDO_NO_FACTURABLE",
                $"Solo se genera proforma de un pedido por facturar (estado actual: {pedido.Estado}).");

        var lectura = await _empresasFiscal.ObtenerAsync(empresaId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "EMPRESA_NO_ENCONTRADA",
                $"La empresa '{empresaId}' no existe o está inactiva.");
        var emisor = await EmisorSnapshot.ResolverAsync(_empresasFiscal, empresaId, cancellationToken);

        var armada = ProformaEmisionDesdePedido.Armar(pedido, lectura);
        var ahora = _clock.UtcNow;
        var factura = FacturaVentaArmado.Construir(
            armada.Comando, empresaId, ProformaEmisionDesdePedido.FolioArmado, 0, _user.UserId,
            FacturaVentaArmado.ReceptorDe(armada.Comando), emisor, ahora.Year, ahora.Month);

        var esVentaDirecta = pedido.Origen == nameof(OrigenPedido.Manual) && pedido.ClaseManual is not null;
        var contenido = ProformaCfdiMapper.Desde(factura, ahora, ProformaEmisionDesdePedido.SeriePrevista(esVentaDirecta));

        var ultimaVersion = await _db.ProformasPedido
            .Where(p => p.PedidoFacturableId == pedido.Id)
            .MaxAsync(p => (int?)p.NumeroVersion, cancellationToken) ?? 0;

        var proforma = ProformaPedido.Generar(
            empresaId, pedido.SucursalId, pedido.Id, ultimaVersion + 1, pedido.NumeroPedido,
            ProformaJson.Escribir(contenido), ProformaJson.Escribir(armada.Avisos),
            contenido.Totales.Total, contenido.Moneda, _user.UserId, _user.UserName, ahora);

        _db.ProformasPedido.Add(proforma);
        await _db.SaveChangesAsync(cancellationToken);

        return new ProformaPedidoResponse(
            proforma.Id, proforma.PedidoFacturableId, proforma.Folio, proforma.NumeroVersion, proforma.GeneradaEn,
            proforma.GeneradaPorNombre, proforma.NumeroPedido, contenido, armada.Avisos);
    }
}
