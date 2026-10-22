using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.PublicPorts;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Administracion;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Domain.Trazabilidad;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Inicio;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Compras.Application.Oc.Analisis;

public sealed record ObtenerAnalisisComprasQuery(DateOnly Desde, DateOnly Hasta, Guid? ProveedorId = null,
    Guid? ArticuloId = null, Guid? DepartamentoId = null, Guid? SucursalId = null)
    : IRequest<AnalisisComprasResultado>, ISucursalSeleccionadaQuery
{
    public string PermisoTodasSucursales => "compras.ordenes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; } = [];
}
public sealed class ObtenerAnalisisComprasValidator : AbstractValidator<ObtenerAnalisisComprasQuery>
{
    public ObtenerAnalisisComprasValidator()
    {
        RuleFor(q => q.Desde).NotEqual(DateOnly.MinValue);
        RuleFor(q => q.Hasta).GreaterThanOrEqualTo(q => q.Desde).LessThan(DateOnly.MaxValue);
        RuleFor(q => q).Must(q => q.Hasta.DayNumber - q.Desde.DayNumber <= 366)
            .WithMessage("Selecciona un periodo de hasta un año.");
        RuleFor(q => q.ProveedorId).Must(id => id == null || id != Guid.Empty);
        RuleFor(q => q.ArticuloId).Must(id => id == null || id != Guid.Empty);
        RuleFor(q => q.DepartamentoId).Must(id => id == null || id != Guid.Empty);
        RuleFor(q => q.SucursalId).Must(id => id == null || id != Guid.Empty);
    }
}
public sealed class ObtenerAnalisisComprasHandler(ComprasDbContext db, ICurrentEmpresaContext empresa,
    IRecepcionesCostoReadPort almacen, ICxpFacturasTrazabilidadReadPort cxp, IProveedorReadPort proveedores,
    IArticuloReadPort articulos, IDepartamentoReadPort departamentos, IClock clock)
    : IRequestHandler<ObtenerAnalisisComprasQuery, AnalisisComprasResultado>
{
    public async Task<AnalisisComprasResultado> Handle(ObtenerAnalisisComprasQuery q, CancellationToken cancellationToken)
    {
        var empresaId = empresa.Current ?? throw new BusinessRuleException("EMPRESA_REQUERIDA", "Selecciona una empresa.");
        var fin = FechaContable.InicioDelDiaUtc(q.Hasta.AddDays(1));
        var ordenes = await db.OrdenesCompra.AsNoTracking().AsSplitQuery()
            .Include(o => o.SolicitudesCancelacion).Include(o => o.Lineas).Include(o => o.Incidencias).ThenInclude(i => i.Compromisos)
            .Include(o => o.RespuestasProveedor).ThenInclude(r => r.Compromisos)
            .Where(o => o.EmpresaId == empresaId && o.FechaDocumento >= q.Desde && o.FechaDocumento <= q.Hasta
                && o.FechaContabilizacion < fin
                && (o.Estado == EstadoOrdenCompra.Autorizada || o.Estado == EstadoOrdenCompra.Cerrada
                    || o.Estado == EstadoOrdenCompra.CancelacionSolicitada || o.Estado == EstadoOrdenCompra.Cancelada)
                && (q.SucursalesPermitidas == null || (q.SucursalesPermitidas ?? Array.Empty<Guid>()).Contains(o.SucursalDestinoId))
                && (q.SucursalId == null || o.SucursalDestinoId == q.SucursalId)
                && (q.ProveedorId == null || o.ProveedorId == q.ProveedorId))
            .OrderBy(o => o.FechaDocumento).ThenBy(o => o.Id).ToListAsync(cancellationToken);
        var canceladas = ordenes.Where(o => o.Estado == EstadoOrdenCompra.Cancelada).ToArray();
        if (canceladas.Length > 0)
        {
            var ids = canceladas.Select(o => o.Id).ToArray();
            var historial = await db.Set<AuditLogEntry>().AsNoTracking()
                .Where(a => a.EmpresaId == empresaId && a.Modulo == "Compras" && a.Entidad == "OrdenCompra"
                    && a.EntidadId != null && ids.Contains(a.EntidadId.Value)).ToListAsync(cancellationToken);
            foreach (var o in canceladas)
            {
                var canceladaEn = o.SolicitudesCancelacion.Where(s => s.Confirmada == true)
                    .Select(s => s.FechaResolucion).Min()
                    ?? CancelacionAnalisis.Fecha(historial.Where(a => a.EntidadId == o.Id));
                if (canceladaEn == null)
                    throw new BusinessRuleException("COMPRAS_CANCELACION_POR_CONFIRMAR",
                        "Por confirmar: falta la fecha de cancelación de una orden del periodo. Completa su historial antes de conciliar el corte.");
                if (canceladaEn < fin) ordenes.Remove(o);
            }
        }
        var lineas = ordenes.SelectMany(o => o.Lineas).ToArray();
        var nombres = await proveedores.ObtenerPorIdsAsync(ordenes.Select(o => o.ProveedorId).Distinct().ToArray(), cancellationToken);
        var arts = await articulos.ObtenerPorIdsAsync(lineas.Select(l => l.ArticuloId).Distinct().ToArray(), cancellationToken);
        var deps = await departamentos.ObtenerAsync(lineas.Select(l => l.DepartamentoSolicitanteId).Distinct().ToArray(), cancellationToken);
        var movimientos = (await almacen.LeerMovimientosAsync(empresaId, lineas.Select(l =>
            new PartidaOcLectura(l.OrdenCompraId, l.Id, l.ArticuloId, l.UnidadMedida)).ToArray(), q.Hasta, cancellationToken)).ToLookup(m => m.LineaOcId);
        var facturas = await cxp.LeerAnalisisAsync(empresaId, ordenes.Select(o => o.Id).ToArray(), q.Hasta, cancellationToken);
        var porFacturar = new Dictionary<Guid, decimal>();
        var porPagar = new Dictionary<Guid, decimal>();
        var diferencias = new Dictionary<Guid, decimal>();
        var porId = lineas.ToDictionary(l => l.Id);
        var monedaOc = ordenes.ToDictionary(o => o.Id, o => o.Moneda);
        foreach (var f in facturas)
        {
            if (f.Moneda != monedaOc[f.OrdenCompraId])
                throw new BusinessRuleException("COMPRAS_MONEDA_POR_CONFIRMAR", "Por confirmar: la factura y su orden tienen distinta moneda. Revisa la conciliación en Cuentas por Pagar.");
            var saldos = ProrrateoAnalisis.Distribuir(f.Saldo, f.Lineas.Select(l => l.ImporteNeto).ToArray());
            for (var i = 0; i < f.Lineas.Count; i++)
            {
                var l = f.Lineas[i];
                if (!porId.TryGetValue(l.LineaOcId, out var oc) || oc.OrdenCompraId != f.OrdenCompraId)
                    throw new BusinessRuleException("COMPRAS_FACTURA_PARTIDA_INVALIDA", "Por confirmar: una factura referencia una partida ajena a su orden.");
                porFacturar[l.LineaOcId] = porFacturar.GetValueOrDefault(l.LineaOcId) + l.Cantidad;
                porPagar[l.LineaOcId] = porPagar.GetValueOrDefault(l.LineaOcId) + saldos[i];
                diferencias[l.LineaOcId] = diferencias.GetValueOrDefault(l.LineaOcId)
                    + l.ImporteNeto - (oc.SubtotalLinea / oc.Cantidad * l.Cantidad);
            }
        }
        var partidas = new List<PartidaCompraAnalisis>();
        foreach (var o in ordenes)
        {
            var ls = o.Lineas.OrderBy(l => l.Posicion).ToArray();
            var importes = ProrrateoAnalisis.Distribuir(o.CalcularTotales().TotalAPagar, ls.Select(l => l.SubtotalLinea).ToArray());
            for (var i = 0; i < ls.Length; i++)
            {
                var l = ls[i];
                var incidencias = o.Incidencias.Where(x => x.LineaOrdenCompraId == l.Id && x.RegistradaEn < fin).ToArray();
                var compromisos = incidencias.SelectMany(x => x.Compromisos).Where(c => c.RegistradoEn < fin)
                    .OrderBy(c => c.RegistradoEn).ThenBy(c => c.Id)
                    .Select(c => new CompromisoAnalisis(FechaContable.HoyLocal(c.RegistradoEn), c.FechaCompromiso)).ToArray();
                var respuesta = o.RespuestasProveedor.Where(r => r.RegistradaEn < fin && r.Compromisos.Any(c => c.LineaOrdenCompraId == l.Id))
                    .OrderBy(r => r.Numero).LastOrDefault();
                var fecha = respuesta?.Compromisos.Where(c => c.LineaOrdenCompraId == l.Id).Max(c => (DateOnly?)c.FechaPrometida)
                    ?? (l.FechaEntregaLinea is {} entrega ? FechaContable.HoyLocal(entrega) : o.FechaEntregaEsperada);
                partidas.Add(new(o.Id, o.Folio.Valor, l.Id, o.ProveedorId,
                    nombres.GetValueOrDefault(o.ProveedorId)?.RazonSocial ?? "Proveedor por confirmar", l.ArticuloId,
                    arts.GetValueOrDefault(l.ArticuloId)?.Nombre ?? "Artículo por confirmar", l.DepartamentoSolicitanteId,
                    deps.GetValueOrDefault(l.DepartamentoSolicitanteId)?.Nombre ?? "Departamento por confirmar", o.Moneda,
                    o.FechaDocumento, l.Cantidad, l.UnidadMedida, importes[i], l.SubtotalLinea / l.Cantidad,
                    l.EsServicio, fecha, compromisos, incidencias.Select(x => FechaContable.HoyLocal(x.RegistradaEn)).ToArray())
                {
                    Movimientos = movimientos[l.Id].Select(m => new MovimientoAnalisis(m.DocumentoId, m.Folio, m.Fecha, m.Cantidad, m.EsDevolucion)).ToArray(),
                    Facturada = porFacturar.GetValueOrDefault(l.Id), PorPagar = porPagar.GetValueOrDefault(l.Id),
                    DiferenciaPrecio = diferencias.GetValueOrDefault(l.Id),
                });
            }
        }
        return CalculoAnalisisCompras.Calcular(partidas, new(q.Desde, q.Hasta, q.ProveedorId, q.ArticuloId, q.DepartamentoId))
            with { GeneradoEn = clock.UtcNow };
    }
}

public static class ProrrateoAnalisis
{
    // Los pagos e impuestos son de cabecera; el último renglón absorbe centavos para conservar el total.
    public static decimal[] Distribuir(decimal total, IReadOnlyList<decimal> bases)
    {
        var resultado = new decimal[bases.Count];
        var suma = bases.Sum();
        decimal asignado = 0;
        for (var i = 0; i < bases.Count; i++)
        {
            resultado[i] = i == bases.Count - 1 ? total - asignado
                : Math.Round(total * (suma == 0 ? 1m / bases.Count : bases[i] / suma), 2);
            asignado += resultado[i];
        }
        return resultado;
    }
}
