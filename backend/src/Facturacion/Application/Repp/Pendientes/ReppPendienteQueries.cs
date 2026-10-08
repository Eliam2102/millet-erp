using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Repp.Pendientes;

public sealed record ReppPendientesQuery(EstadoReppPendiente? Estado = EstadoReppPendiente.Pendiente,
    string? Indicador = null, int Offset = 0, int Limit = 10) : IRequest<ReppPendientesResponse>;
public sealed record ReppPendienteQuery(Guid Id) : IRequest<ReppPendienteDto>;
public sealed record ReppPendientesResponse(IReadOnlyList<ReppPendienteDto> Items, int Total, ReppPendientesKpis Kpis);
public sealed record ReppPendientesKpis(int Pendientes, int CercaDelPlazo, int Vencidos, int ConError);
public sealed record ReppPendienteFacturaDto(Guid FacturaVentaId, string? Folio, decimal Importe);
public sealed record ReppPendienteDto(Guid Id, Guid ClienteId, string ClienteNombre, string? ClienteRfc,
    Guid MovimientoBancarioId, Guid CuentaBancariaId, Guid? PropuestaId, decimal Monto, string Moneda,
    DateOnly FechaValor, DateOnly FechaLimite, string? Referencia, string FormaPago, bool Revisado,
    string Estado, string Alerta, decimal? TipoCambio, bool TcPorRegistrar,
    Guid? ReciboPagoId, Guid? IntentoReciboPagoId, string? UltimoErrorCodigo, string? UltimoErrorMensaje,
    string? MotivoDescarte, IReadOnlyList<ReppPendienteFacturaDto> Facturas, IReadOnlyList<string> Bloqueos);

public sealed class ReppPendienteQueryHandler(FacturacionDbContext db, ReppPendienteServicio servicio,
    IClientesReadPort clientes, IReppBancarioReadPort bancario, IClock clock)
    : IRequestHandler<ReppPendienteQuery, ReppPendienteDto>, IRequestHandler<ReppPendientesQuery, ReppPendientesResponse>
{
    public async Task<ReppPendienteDto> Handle(ReppPendienteQuery q, CancellationToken cancellationToken) =>
        await MapearAsync(await servicio.ObtenerAsync(q.Id, false, cancellationToken), true, cancellationToken);

    public async Task<ReppPendientesResponse> Handle(ReppPendientesQuery q, CancellationToken cancellationToken)
    {
        var pendientes = db.ReppPendientes.AsNoTracking().Where(p => p.Estado == EstadoReppPendiente.Pendiente);
        var hoy = PlazoRepp.Hoy(clock.UtcNow);
        var cerca = hoy.AddDays(3);
        var kpis = new ReppPendientesKpis(await pendientes.CountAsync(cancellationToken),
            await pendientes.CountAsync(p => p.FechaLimite >= hoy && p.FechaLimite <= cerca, cancellationToken),
            await pendientes.CountAsync(p => p.FechaLimite < hoy, cancellationToken),
            await pendientes.CountAsync(p => p.UltimoErrorCodigo != null, cancellationToken));
        var lista = db.ReppPendientes.AsNoTracking();
        if (q.Estado.HasValue) lista = lista.Where(p => p.Estado == q.Estado);
        lista = q.Indicador switch
        {
            "cerca" => lista.Where(p => p.Estado == EstadoReppPendiente.Pendiente && p.FechaLimite >= hoy && p.FechaLimite <= cerca),
            "vencidos" => lista.Where(p => p.Estado == EstadoReppPendiente.Pendiente && p.FechaLimite < hoy),
            "error" => lista.Where(p => p.Estado == EstadoReppPendiente.Pendiente && p.UltimoErrorCodigo != null),
            _ => lista,
        };
        var total = await lista.CountAsync(cancellationToken);
        var items = await lista.Include(p => p.Facturas).OrderBy(p => p.FechaLimite).ThenBy(p => p.Id)
            .Skip(Math.Max(0, q.Offset)).Take(Math.Clamp(q.Limit, 1, 50)).ToListAsync(cancellationToken);
        var resultado = new List<ReppPendienteDto>();
        foreach (var p in items) resultado.Add(await MapearAsync(p, false, cancellationToken));
        return new(resultado, total, kpis);
    }

    private async Task<ReppPendienteDto> MapearAsync(ReppPendiente p, bool validar, CancellationToken cancellationToken)
    {
        var cliente = await clientes.ObtenerAsync(p.ClienteId, cancellationToken);
        var tc = p.Moneda == "MXN" ? null : await bancario.TipoCambioAsync(p.Moneda, p.FechaValor, cancellationToken);
        var ids = p.Facturas.Select(f => f.FacturaVentaId).ToArray();
        var folios = await db.FacturasVenta.AsNoTracking().Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Folio, cancellationToken);
        var bloqueos = new List<string>();
        if (p.Estado == EstadoReppPendiente.Pendiente)
        {
            if (p.Moneda != "MXN" && tc is null) bloqueos.Add("TC por registrar: registra el tipo de cambio del día del pago en Catálogos.");
            if (validar)
            {
                try
                {
                    await servicio.ValidarAsync(p, p.FormaPago, p.Facturas.Select(f => new RelacionRepp(f.FacturaVentaId, f.Importe)).ToArray(), cancellationToken);
                    await bancario.SucursalEmisoraAsync(cancellationToken);
                }
                catch (BusinessRuleException ex) { bloqueos.Add(ex.Message); }
            }
        }
        return new(p.Id, p.ClienteId, cliente?.RazonSocial ?? "[CLIENTE POR IDENTIFICAR]", cliente?.Rfc,
            p.MovimientoBancarioId, p.CuentaBancariaId, p.PropuestaId, p.Monto, p.Moneda,
            p.FechaValor, p.FechaLimite, p.Referencia, p.FormaPago, p.Revisado, p.Estado.ToString(),
            PlazoRepp.Alerta(p.FechaLimite, clock.UtcNow), tc, p.Moneda != "MXN" && tc is null,
            p.ReciboPagoId, p.IntentoReciboPagoId, p.UltimoErrorCodigo, p.UltimoErrorMensaje, p.MotivoDescarte,
            p.Facturas.Select(f => new ReppPendienteFacturaDto(f.FacturaVentaId, folios.GetValueOrDefault(f.FacturaVentaId), f.Importe)).ToArray(), bloqueos);
    }
}
