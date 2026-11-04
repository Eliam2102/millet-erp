using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Domain.Ports.Tesoreria;
using Millet.CuentasPorPagar.Domain.Portal;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Portal;

// ---- Lo que ve el proveedor: siempre acotado al proveedor del token (nunca por parámetro del cliente) ----

public sealed record FacturaPortalResponse(
    Guid Id, string? Folio, string? Uuid, DateTimeOffset Fecha, decimal Total, string Moneda,
    string Estado, bool Detenida, string? Motivo, DateOnly? FechaProgramadaPago, DateOnly FechaVencimiento, DateOnly? FechaPago = null);

public sealed record PagoPortalResponse(
    Guid PagoId, Guid FacturaId, string? Folio, DateOnly FechaPago, decimal Importe, decimal ComplementoPendiente);

public sealed record ListarFacturasPortalQuery(Guid ProveedorId) : IRequest<IReadOnlyList<FacturaPortalResponse>>;
public sealed record ObtenerFacturaPortalQuery(Guid ProveedorId, Guid FacturaId) : IRequest<FacturaPortalResponse>;
public sealed record ListarPagosPortalQuery(Guid ProveedorId) : IRequest<IReadOnlyList<PagoPortalResponse>>;

internal static class PortalMapeo
{
    public static string? Folio(string? serie, string? folio) =>
        string.IsNullOrWhiteSpace(serie) ? folio : $"{serie}-{folio}";

    public static FacturaPortalResponse Mapear(Factura f, DateOnly? fechaCorrida, bool sinOpinionCumplimiento = false, DateOnly? fechaPago = null)
    {
        var folio = Folio(f.SerieProveedor, f.FolioProveedor);
        var faltaRepp = f.EnRevision && f.MotivoRevisionId == Factura.MotivoFaltaReppId;
        // 86e3ke2d3 (CA8.4): sin opinión de cumplimiento vigente la factura queda detenida por «documentación» (si ya está en revisión por otra causa, manda esa).
        var faltaOpinion = sinOpinionCumplimiento && !f.EnRevision && f.Estado is not (EstadoPasivo.Pagada or EstadoPasivo.Cancelada);
        var detenida = f.EnRevision || faltaOpinion;
        var (estado, motivo) = f.Estado switch
        {
            _ when faltaOpinion => ("Detenida: documentación", "Falta tu opinión de cumplimiento vigente"),
            EstadoPasivo.EnRevision when faltaRepp =>
                ("Detenida: falta el complemento de pago", $"Falta el complemento de pago de la factura {folio ?? f.UuidCfdi}"),
            EstadoPasivo.EnRevision => ("En revisión", (string?)"La factura está en revisión por Cuentas por Pagar."),
            EstadoPasivo.Autorizada when detenida => ("En revisión", "La factura está en revisión por Cuentas por Pagar."),
            EstadoPasivo.Autorizada => ("Programada para pago", null),
            EstadoPasivo.Pagada => ("Pagada", null),
            EstadoPasivo.Cancelada => ("Cancelada", null),
            _ => ("Recibida", null),
        };
        // Solo una autorizada y no detenida está «programada»; el vencimiento va en su propia columna.
        DateOnly? programada = f.Estado == EstadoPasivo.Autorizada && !detenida ? fechaCorrida : null;
        // Una pagada muestra cuándo se le pagó (el último pago vigente), igual que «Pagos y complementos».
        return new(f.Id, folio, f.UuidCfdi, f.FechaDocumento, f.Total, f.Moneda, estado, detenida, motivo, programada, f.FechaVencimiento,
            f.Estado == EstadoPasivo.Pagada ? fechaPago : null);
    }
}

public sealed class PortalProveedorQueryHandlers(CuentasPorPagarDbContext db, IPagosProveedorReadPort pagosTesoreria, IProveedorReadPort proveedores) :
    IRequestHandler<ListarFacturasPortalQuery, IReadOnlyList<FacturaPortalResponse>>,
    IRequestHandler<ObtenerFacturaPortalQuery, FacturaPortalResponse>,
    IRequestHandler<ListarPagosPortalQuery, IReadOnlyList<PagoPortalResponse>>
{
    // «Fecha programada de pago» = la de la corrida autorizada que incluye la factura; sin corrida, null («Sin programar»).
    private async Task<IReadOnlyList<FacturaPortalResponse>> MapearAsync(IReadOnlyList<Factura> facturas, CancellationToken ct)
    {
        var fechas = await pagosTesoreria.FechasProgramadasAsync(facturas.Select(f => f.Id).ToArray(), ct);
        var sinOpinion = await proveedores.ObtenerSinOpinionCumplimientoAsync(facturas.Select(f => f.ProveedorId).Distinct().ToArray(), ct);
        var pagadas = facturas.Where(f => f.Estado == EstadoPasivo.Pagada).Select(f => f.Id).ToArray();
        var pagos = pagadas.Length == 0 ? [] : await db.PagosProveedorLocal.AsNoTracking()
            .Where(p => pagadas.Contains(p.FacturaProveedorId) && !p.Revertido)
            .GroupBy(p => p.FacturaProveedorId).Select(g => new { g.Key, Fecha = g.Max(p => p.FechaPago) })
            .ToDictionaryAsync(x => x.Key, x => x.Fecha, ct);
        return facturas.Select(f => PortalMapeo.Mapear(f, fechas.TryGetValue(f.Id, out var d) ? d : null, sinOpinion.Contains(f.ProveedorId),
            pagos.TryGetValue(f.Id, out var p) ? p : null)).ToList();
    }

    public async Task<IReadOnlyList<FacturaPortalResponse>> Handle(ListarFacturasPortalQuery request, CancellationToken cancellationToken) =>
        await MapearAsync(await db.FacturasProveedor.AsNoTracking().Where(f => f.ProveedorId == request.ProveedorId)
            .OrderByDescending(f => f.FechaDocumento).Take(500).ToListAsync(cancellationToken), cancellationToken);

    public async Task<FacturaPortalResponse> Handle(ObtenerFacturaPortalQuery request, CancellationToken cancellationToken)
    {
        var f = await db.FacturasProveedor.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.FacturaId && x.ProveedorId == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PORTAL_FACTURA_NO_ENCONTRADA", "No encontramos la factura.");
        return (await MapearAsync([f], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<PagoPortalResponse>> Handle(ListarPagosPortalQuery request, CancellationToken cancellationToken)
    {
        var filas = await (from p in db.PagosProveedorLocal.AsNoTracking()
                           join f in db.FacturasProveedor.AsNoTracking() on p.FacturaProveedorId equals f.Id
                           where f.ProveedorId == request.ProveedorId && !p.Revertido
                           orderby p.FechaPago descending
                           select new { p.PagoId, FacturaId = f.Id, f.SerieProveedor, f.FolioProveedor, p.FechaPago, p.Importe, p.CubiertoRepp, f.MetodoPago })
            .Take(500).ToListAsync(cancellationToken);
        // Solo los pagos de facturas PPD exigen complemento de pago.
        return filas.Select(x => new PagoPortalResponse(x.PagoId, x.FacturaId, PortalMapeo.Folio(x.SerieProveedor, x.FolioProveedor),
            x.FechaPago, x.Importe, x.MetodoPago == "PPD" ? Math.Max(0, x.Importe - x.CubiertoRepp) : 0)).ToList();
    }
}

// ---- Alta de accesos desde la ficha del proveedor (permiso cuentas_por_pagar.portal.administrar) ----

public sealed record AccesoPortalResponse(Guid Id, Guid ProveedorId, string Email, bool Activo);

public sealed record ListarAccesosPortalQuery(Guid ProveedorId) : IRequest<IReadOnlyList<AccesoPortalResponse>>;
public sealed record AgregarAccesoPortalCommand(Guid ProveedorId, string Email) : IRequest<AccesoPortalResponse>;
public sealed record CambiarActivoAccesoPortalCommand(Guid ProveedorId, Guid AccesoId, bool Activo) : IRequest<AccesoPortalResponse>;

public sealed class AccesoPortalHandlers(
    CuentasPorPagarDbContext db, IProveedorReadPort proveedores, Millet.SharedKernel.Application.ICurrentEmpresaContext empresa) :
    IRequestHandler<ListarAccesosPortalQuery, IReadOnlyList<AccesoPortalResponse>>,
    IRequestHandler<AgregarAccesoPortalCommand, AccesoPortalResponse>,
    IRequestHandler<CambiarActivoAccesoPortalCommand, AccesoPortalResponse>
{
    private static AccesoPortalResponse Mapear(AccesoPortalProveedor a) => new(a.Id, a.ProveedorId, a.Email, a.Activo);

    public async Task<IReadOnlyList<AccesoPortalResponse>> Handle(ListarAccesosPortalQuery request, CancellationToken cancellationToken) =>
        (await db.AccesosPortalProveedor.AsNoTracking().Where(a => a.ProveedorId == request.ProveedorId).OrderBy(a => a.Email).ToListAsync(cancellationToken))
        .Select(Mapear).ToList();

    public async Task<AccesoPortalResponse> Handle(AgregarAccesoPortalCommand request, CancellationToken cancellationToken)
    {
        var empresaId = empresa.Current ?? throw new MissingEmpresaContextException(nameof(AccesoPortalProveedor));
        if (await proveedores.ObtenerAsync(request.ProveedorId, cancellationToken) is null)
            throw new EntityNotFoundException("PORTAL_PROVEEDOR_NO_ENCONTRADO", "No encontramos el proveedor.");
        var email = AccesoPortalProveedor.Normalizar(request.Email);
        // El correo es único en todo el portal: la sesión se resuelve solo con correo y código.
        var existente = await db.AccesosPortalProveedor.FirstOrDefaultAsync(a => a.Email == email, cancellationToken);
        if (existente is not null && existente.ProveedorId != request.ProveedorId)
            throw new ConflictException("PORTAL_CORREO_DE_OTRO_PROVEEDOR", "Ese correo ya tiene acceso al portal para otro proveedor.");
        if (existente is null)
        {
            existente = new AccesoPortalProveedor(empresaId, request.ProveedorId, email);
            db.AccesosPortalProveedor.Add(existente);
        }
        else existente.CambiarActivo(true);
        await db.SaveChangesAsync(cancellationToken);
        return Mapear(existente);
    }

    public async Task<AccesoPortalResponse> Handle(CambiarActivoAccesoPortalCommand request, CancellationToken cancellationToken)
    {
        var acceso = await db.AccesosPortalProveedor.FirstOrDefaultAsync(a => a.Id == request.AccesoId && a.ProveedorId == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PORTAL_ACCESO_NO_ENCONTRADO", "No encontramos el acceso.");
        acceso.CambiarActivo(request.Activo);
        await db.SaveChangesAsync(cancellationToken);
        return Mapear(acceso);
    }
}
