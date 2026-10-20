using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;

/// <summary>
/// Alimenta el panel «recepciones y OC pendientes» del registro de factura: OC autorizadas
/// del proveedor (por RFC o id) con saldo por facturar y sus recepciones proyectadas localmente.
/// </summary>
public sealed record PendientesFacturarProveedorQuery(string? Rfc, Guid? ProveedorId = null)
    : IRequest<PendientesFacturarProveedorResponse>, Millet.SharedKernel.Application.ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.facturas.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed record PendientesFacturarProveedorResponse(
    Guid? ProveedorId, string? ProveedorNombre, IReadOnlyList<OcPendienteFacturar> Ocs);

public sealed record OcPendienteFacturar(
    Guid OcId, string Folio, Guid SucursalId, string Moneda, decimal Total, decimal CantidadPorFacturar,
    IReadOnlyList<RecepcionPendiente> Recepciones);

public sealed record RecepcionPendiente(
    Guid RecepcionId, string Folio, DateOnly Fecha, decimal CantidadRecibida, bool FacturaPendiente);

public sealed class PendientesFacturarProveedorHandler(
    CuentasPorPagarDbContext db, IProveedorReadPort proveedores, IComprasOcReadPort compras)
    : IRequestHandler<PendientesFacturarProveedorQuery, PendientesFacturarProveedorResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<PendientesFacturarProveedorResponse> Handle(
        PendientesFacturarProveedorQuery request, CancellationToken cancellationToken)
    {
        var rfc = (request.Rfc ?? string.Empty).Trim().ToUpperInvariant();
        var proveedor = request.ProveedorId is Guid pid
            ? await proveedores.ObtenerAsync(pid, cancellationToken)
            : rfc.Length == 0 ? null : await proveedores.ObtenerPorRfcAsync(rfc, cancellationToken);
        if (proveedor is null) return new(null, null, []);

        var ocs = (await compras.ListarAutorizadasPorProveedorAsync(proveedor.Id, cancellationToken))
            .Where(o => request.SucursalesPermitidas == null || request.SucursalesPermitidas.Contains(o.SucursalId))
            .Select(o => (Oc: o, PorFacturar: o.Lineas.Sum(l => Math.Max(0, l.Cantidad - l.CantidadFacturada))))
            .Where(x => x.PorFacturar > 0)
            .OrderBy(x => x.Oc.Folio, StringComparer.Ordinal)
            .ToList();
        if (ocs.Count == 0) return new(proveedor.Id, proveedor.RazonSocial, []);

        var ids = ocs.Select(x => x.Oc.Id).ToList();
        var filas = await db.RecepcionesOcLocal.AsNoTracking()
            .Where(r => ids.Contains(r.OrdenCompraId))
            .Select(r => new { r.OrdenCompraId, r.RecepcionId, r.FolioRecepcion, r.FechaMovimiento, r.FacturaPendiente, r.LineasJson })
            .ToListAsync(cancellationToken);
        // Recepciones ya ligadas a una factura vigente: no se ofrecen de nuevo (la OC sigue saliendo si tiene saldo).
        var recepcionIds = filas.Select(r => r.RecepcionId).ToList();
        var ligadas = (await db.FacturasProveedor.AsNoTracking()
            .Where(f => f.RecepcionId != null && recepcionIds.Contains(f.RecepcionId.Value) && f.Estado != EstadoPasivo.Cancelada)
            .Select(f => f.RecepcionId!.Value).ToListAsync(cancellationToken)).ToHashSet();
        var porOc = filas.Where(r => !ligadas.Contains(r.RecepcionId)).ToLookup(r => r.OrdenCompraId);

        var resultado = ocs.Select(x => new OcPendienteFacturar(
            x.Oc.Id, x.Oc.Folio, x.Oc.SucursalId, x.Oc.Moneda, x.Oc.Total, x.PorFacturar,
            porOc[x.Oc.Id].OrderByDescending(r => r.FechaMovimiento)
                .Select(r => new RecepcionPendiente(r.RecepcionId, r.FolioRecepcion, r.FechaMovimiento,
                    (JsonSerializer.Deserialize<List<LineaRecepcionPayload>>(r.LineasJson, JsonOptions) ?? []).Sum(l => l.Cantidad),
                    r.FacturaPendiente)).ToList())).ToList();
        return new(proveedor.Id, proveedor.RazonSocial, resultado);
    }
}
