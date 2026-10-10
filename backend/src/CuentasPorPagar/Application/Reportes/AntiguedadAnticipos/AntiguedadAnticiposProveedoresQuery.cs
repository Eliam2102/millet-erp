using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Reportes.AntiguedadAnticipos;

public sealed record AntiguedadAnticiposProveedoresQuery(DateOnly? FechaCorte = null, Guid? ProveedorId = null) : IRequest<ReporteJsonResponse>, IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public string TipoDocumento => "anticipo_proveedor";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}
public sealed class AntiguedadAnticiposProveedoresValidator : AbstractValidator<AntiguedadAnticiposProveedoresQuery>
{
    public AntiguedadAnticiposProveedoresValidator()
    {
        RuleFor(q => q.FechaCorte).Must(f => f is null || f < DateOnly.MaxValue).WithMessage("Fecha de corte inválida.");
        RuleFor(q => q.ProveedorId).Must(id => id is null || id != Guid.Empty).WithMessage("Proveedor inválido.");
    }
}
public sealed class AntiguedadAnticiposProveedoresHandler(CuentasPorPagarDbContext db, IClock clock, IProveedorReadPort proveedores)
    : IRequestHandler<AntiguedadAnticiposProveedoresQuery, ReporteJsonResponse>
{
    public async Task<ReporteJsonResponse> Handle(AntiguedadAnticiposProveedoresQuery q, CancellationToken cancellationToken)
    {
        var corte = q.FechaCorte ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var fin = SaldosHistoricos.FinExclusivo(corte);
        var anticipos = db.AnticiposProveedor.AsNoTracking()
            .Where(a => q.DocumentosPermitidos == null || (q.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(a.Id)).Where(a => a.FechaCfdi < fin &&
            (a.FechaCancelacion == null || a.FechaCancelacion >= fin));
        if (q.ProveedorId is Guid p) anticipos = anticipos.Where(a => a.ProveedorId == p);
        var datos = await anticipos.ToListAsync(cancellationToken);
        var ids = datos.Select(a => a.Id).ToArray();
        var facturaIds = db.FacturasProveedor.Select(f => f.Id);
        var aplicaciones = await db.MovimientosPasivo.AsNoTracking().Where(m => m.Tipo == TipoMovimientoPasivo.Anticipo &&
            m.DocumentoId != null && ids.Contains(m.DocumentoId.Value) && facturaIds.Contains(m.FacturaProveedorId)).ToListAsync(cancellationToken);
        var maestro = new Dictionary<Guid, ProveedorDto?>();
        var filas = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var a in datos)
        {
            var movimientos = aplicaciones.Where(m => m.DocumentoId == a.Id).ToArray();
            if (movimientos.Sum(m => m.Monto) != a.MontoAmortizado)
                throw new BusinessRuleException("CXP_HISTORICO_POR_CONFIRMAR", "Por confirmar: hay amortizaciones anteriores a P8 sin fecha reconstruible.");
            var amortizado = movimientos.Where(m => m.Fecha <= corte).Sum(m => m.Monto);
            var saldo = a.MontoEntregado - amortizado;
            if (saldo <= 0) continue;
            if (!maestro.TryGetValue(a.ProveedorId, out var proveedor))
                maestro[a.ProveedorId] = proveedor = await proveedores.ObtenerAsync(a.ProveedorId, cancellationToken);
            filas.Add(new Dictionary<string, object?> { ["anticipo_id"] = a.Id,
                ["proveedor_nombre"] = proveedor?.RazonSocial ?? "[PROVEEDOR POR CONFIRMAR]", ["rfc"] = proveedor?.Rfc ?? "[RFC POR CONFIRMAR]",
                ["folio_proveedor"] = a.FolioProveedor, ["fecha_cfdi"] = a.FechaCfdi, ["moneda"] = a.Moneda,
                ["monto_entregado"] = a.MontoEntregado, ["monto_amortizado"] = amortizado,
                ["saldo_amortizable"] = saldo });
        }
        return new("Antigüedad de anticipos a proveedores", clock.UtcNow, ReporteHistoricoBuilder.Filtros(corte, q.ProveedorId, null, nombreProveedor: q.ProveedorId is Guid proveedorFiltro ? (await proveedores.ObtenerAsync(proveedorFiltro, cancellationToken))?.RazonSocial : null),
            [ReporteHistoricoBuilder.Texto("proveedor_nombre", "Proveedor"), ReporteHistoricoBuilder.Texto("rfc", "RFC"),
             ReporteHistoricoBuilder.Texto("folio_proveedor", "Folio"),
             new("fecha_cfdi", "Fecha CFDI", TipoColumnaReporte.Fecha, AlineacionColumna.Centro),
             ReporteHistoricoBuilder.Texto("moneda", "Moneda"), ReporteHistoricoBuilder.Importe("monto_entregado", "Entregado"),
             ReporteHistoricoBuilder.Importe("monto_amortizado", "Amortizado"), ReporteHistoricoBuilder.Importe("saldo_amortizable", "Saldo amortizable")],
             filas, ReporteHistoricoBuilder.Totales(filas, "monto_entregado", "monto_amortizado", "saldo_amortizable"));
    }
}
