using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas.Terminal;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Terminal;

public sealed record CuadreFacturaBancoResponse(
    int Anio, int Mes, int Cargos, decimal ComisionCargos, decimal IvaCargos, decimal TotalCargos,
    Guid? CfdiRecibidoId, string? UuidCfdi, string? RfcEmisor, decimal? SubtotalFactura, decimal? IvaFactura,
    decimal? TotalFactura, decimal? DiferenciaComision, decimal? DiferenciaIva, decimal? DiferenciaTotal, bool Cuadra);

public sealed record FacturaBancoTerminalResponse(
    Guid Id, Guid CfdiRecibidoId, string UuidCfdi, string RfcEmisor, int Anio, int Mes, decimal Comision,
    decimal IvaComision, decimal Total, int Cargos, DateTimeOffset LigadaEn, decimal Redondeo = 0m);

/// <summary>
/// Cuadre de los cargos del mes (liquidaciones sin factura) contra un CFDI del banco
/// (86e3xt2q8). Sin <c>CfdiRecibidoId</c> devuelve solo la suma del mes.
/// </summary>
public sealed record CuadreFacturaBancoQuery(int Anio, int Mes, Guid? CfdiRecibidoId) : IRequest<CuadreFacturaBancoResponse>;

/// <summary>CFDI recibidos candidatos a factura del banco para el mes (buzón de CxP).</summary>
public sealed record CfdisBancoCandidatosQuery(int Anio, int Mes, string? RfcEmisor) : IRequest<IReadOnlyList<CfdiRecibidoBanco>>;

/// <summary>Facturas del banco ya ligadas (más recientes primero).</summary>
public sealed record FacturasBancoTerminalQuery(int Limit = 24) : IRequest<IReadOnlyList<FacturaBancoTerminalResponse>>;

/// <summary>
/// Liga la factura mensual del banco a los cargos del mes (criterio 4) si cuadra con
/// diferencia de hasta $0.01 (86e3zcbjx; la diferencia va a redondeo en la póliza); si no,
/// responde la diferencia y no liga nada (criterio 5).
/// </summary>
public sealed record LigarFacturaBancoTerminalCommand(int Anio, int Mes, Guid CfdiRecibidoId) : IRequest<FacturaBancoTerminalResponse>;

public sealed class FacturaBancoTerminalHandlers(
    FacturacionDbContext db, ICfdiRecibidoBancoReadPort cfdis, ICurrentEmpresaContext empresa, ICurrentUserContext user,
    IClock clock, IIntegrationEventPublisher eventos) :
    IRequestHandler<CuadreFacturaBancoQuery, CuadreFacturaBancoResponse>,
    IRequestHandler<CfdisBancoCandidatosQuery, IReadOnlyList<CfdiRecibidoBanco>>,
    IRequestHandler<FacturasBancoTerminalQuery, IReadOnlyList<FacturaBancoTerminalResponse>>,
    IRequestHandler<LigarFacturaBancoTerminalCommand, FacturaBancoTerminalResponse>
{
    public async Task<CuadreFacturaBancoResponse> Handle(CuadreFacturaBancoQuery query, CancellationToken cancellationToken)
    {
        ValidarMes(query.Anio, query.Mes);
        var cargos = await CargosSinFacturaAsync(query.Anio, query.Mes, rastrear: false, cancellationToken);
        if (query.CfdiRecibidoId is not Guid cfdiId)
        {
            var c = CuadreFacturaBanco.Calcular(query.Anio, query.Mes, cargos, 0m, 0m, 0m);
            return new(c.Anio, c.Mes, c.Cargos, c.ComisionCargos, c.IvaCargos, c.TotalCargos,
                null, null, null, null, null, null, null, null, null, false);
        }
        var cfdi = await CfdiAsync(cfdiId, cancellationToken);
        return Respuesta(CuadreFacturaBanco.Calcular(query.Anio, query.Mes, cargos, cfdi.Subtotal, cfdi.Iva, cfdi.Total), cfdi);
    }

    public async Task<IReadOnlyList<CfdiRecibidoBanco>> Handle(CfdisBancoCandidatosQuery query, CancellationToken cancellationToken)
    {
        ValidarMes(query.Anio, query.Mes);
        var ligados = (await db.FacturasBancoTerminal.AsNoTracking().Select(f => f.CfdiRecibidoId).ToListAsync(cancellationToken)).ToHashSet();
        var rfc = string.IsNullOrWhiteSpace(query.RfcEmisor) ? null : query.RfcEmisor.Trim().ToUpperInvariant();
        return (await cfdis.ListarParaMesAsync(query.Anio, query.Mes, rfc, cancellationToken))
            .Where(c => !ligados.Contains(c.Id)).ToList();
    }

    public async Task<IReadOnlyList<FacturaBancoTerminalResponse>> Handle(FacturasBancoTerminalQuery query, CancellationToken cancellationToken) =>
        await db.FacturasBancoTerminal.AsNoTracking()
            .OrderByDescending(f => f.Anio).ThenByDescending(f => f.Mes).ThenByDescending(f => f.LigadaEn)
            .Take(Math.Clamp(query.Limit, 1, 120))
            .Select(f => new FacturaBancoTerminalResponse(f.Id, f.CfdiRecibidoId, f.UuidCfdi, f.RfcEmisor, f.Anio, f.Mes,
                f.Comision, f.IvaComision, f.Total, f.Cargos, f.LigadaEn, f.Redondeo))
            .ToListAsync(cancellationToken);

    public async Task<FacturaBancoTerminalResponse> Handle(LigarFacturaBancoTerminalCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (empresaId, usuarioId) = ContextoRequerido.De(empresa, user);
        ValidarMes(command.Anio, command.Mes);
        var cfdi = await CfdiAsync(command.CfdiRecibidoId, ct);
        if (!string.Equals(cfdi.Tipo, "Ingreso", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("TERMINAL_FACTURA_NO_INGRESO", "La factura del banco debe ser un CFDI de ingreso.");
        if (!string.Equals(cfdi.Moneda, "MXN", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("TERMINAL_FACTURA_MONEDA", "La factura del banco por comisiones de la terminal debe estar en MXN.");
        var uuid = cfdi.Uuid.Trim().ToUpperInvariant();
        if (await db.FacturasBancoTerminal.AnyAsync(f => f.CfdiRecibidoId == cfdi.Id || f.UuidCfdi == uuid, ct))
            throw new BusinessRuleException("TERMINAL_FACTURA_YA_LIGADA", "Esa factura del banco ya está ligada a los cargos de un mes.");

        var cargos = await CargosSinFacturaAsync(command.Anio, command.Mes, rastrear: true, ct);
        var cuadre = CuadreFacturaBanco.Calcular(command.Anio, command.Mes, cargos, cfdi.Subtotal, cfdi.Iva, cfdi.Total);
        var factura = FacturaBancoTerminal.Ligar(empresaId, cuadre, cfdi.Id, cfdi.Uuid, cfdi.RfcEmisor, cargos, usuarioId, clock.UtcNow);
        db.FacturasBancoTerminal.Add(factura);
        var sucursalId = await SucursalDeLaComisionAsync(cargos, ct);
        await eventos.PublishAsync(new TerminalFacturaBancoLigadaIntegrationEvent(
            empresaId, clock.UtcNow, factura.Id, factura.CfdiRecibidoId, factura.UuidCfdi, factura.RfcEmisor, factura.Anio,
            factura.Mes, factura.Comision, factura.IvaComision, factura.Total,
            cargos.OrderBy(c => c.Fecha).Select(c => new CargoTerminalFacturado(c.Id, c.Folio, c.Fecha, c.Comision, c.IvaComision)).ToList(),
            factura.ComisionFactura, factura.IvaFactura, factura.Redondeo, sucursalId),
            ct);
        await db.SaveChangesAsync(ct);
        return new(factura.Id, factura.CfdiRecibidoId, factura.UuidCfdi, factura.RfcEmisor, factura.Anio, factura.Mes,
            factura.Comision, factura.IvaComision, factura.Total, factura.Cargos, factura.LigadaEn, factura.Redondeo);
    }

    /// <summary>
    /// Sucursal de la póliza de la factura del banco (86e3zcbjx): la de las ventas con más comisión del mes. Supuesto a validar:
    /// la póliza lleva una sola sucursal y la terminal no trae la suya; con empate gana la de menor id para que sea estable.
    /// </summary>
    private async Task<Guid?> SucursalDeLaComisionAsync(IReadOnlyCollection<LiquidacionTerminal> cargos, CancellationToken ct)
    {
        var ids = cargos.Select(c => c.Id).ToList();
        var ventas = await db.TransaccionesTerminal.AsNoTracking()
            .Where(t => t.LiquidacionId != null && ids.Contains(t.LiquidacionId.Value) && t.SucursalId != null)
            .Select(t => new { t.SucursalId, t.Comision })
            .ToListAsync(ct);
        return ventas.GroupBy(v => v.SucursalId!.Value)
            .Select(g => (Sucursal: g.Key, Comision: g.Sum(v => v.Comision ?? 0m)))
            .OrderByDescending(g => g.Comision).ThenBy(g => g.Sucursal)
            .Select(g => (Guid?)g.Sucursal).FirstOrDefault();
    }

    /// <summary>Cargos del mes (por fecha de liquidación, supuesto a validar) que aún no tienen factura del banco.</summary>
    private async Task<List<LiquidacionTerminal>> CargosSinFacturaAsync(int anio, int mes, bool rastrear, CancellationToken ct)
    {
        var desde = new DateOnly(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        var q = db.LiquidacionesTerminal.Where(l => l.FacturaBancoId == null && l.Fecha >= desde && l.Fecha < hasta);
        return await (rastrear ? q : q.AsNoTracking()).ToListAsync(ct);
    }

    private async Task<CfdiRecibidoBanco> CfdiAsync(Guid id, CancellationToken ct) =>
        await cfdis.ObtenerAsync(id, ct)
        ?? throw new EntityNotFoundException("CFDI_RECIBIDO_NO_ENCONTRADO", $"No existe el CFDI recibido {id}.");

    private static void ValidarMes(int anio, int mes)
    {
        if (anio is < 2020 or > 2100 || mes is < 1 or > 12)
            throw new BusinessRuleException("TERMINAL_MES_INVALIDO", "Indica un año y un mes válidos.");
    }

    private static CuadreFacturaBancoResponse Respuesta(CuadreFacturaBanco c, CfdiRecibidoBanco cfdi) => new(
        c.Anio, c.Mes, c.Cargos, c.ComisionCargos, c.IvaCargos, c.TotalCargos, cfdi.Id, cfdi.Uuid, cfdi.RfcEmisor,
        c.SubtotalFactura, c.IvaFactura, c.TotalFactura, c.DiferenciaComision, c.DiferenciaIva, c.DiferenciaTotal, c.Cuadra);
}
