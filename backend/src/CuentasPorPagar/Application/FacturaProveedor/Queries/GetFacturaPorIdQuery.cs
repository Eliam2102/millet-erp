using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Revision;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;

public sealed record GetFacturaPorIdQuery(Guid Id) : IRequest<FacturaDetalleResponse>;

public sealed record FacturaDetalleResponse(
    Guid Id,
    Guid EmpresaId,
    Guid? CfdiRecibidoId,
    string? UuidCfdi,
    Guid ProveedorId,
    Guid SucursalId,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaDocumento,
    DateTimeOffset FechaContabilizacion,
    DateOnly FechaVencimiento,
    string Moneda,
    decimal? TipoCambio,
    decimal Subtotal,
    decimal Descuentos,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    Guid? OrdenCompraId,
    EstadoPasivo Estado,
    decimal DiferenciaContraOc,
    decimal AnticipoAplicadoTotal,
    decimal NcAplicadasTotal,
    decimal ImportePagado,
    decimal SaldoPendiente,
    MotivoCancelacion? MotivoCancelacion,
    string? MotivoCancelacionTexto,
    bool EnRevision,
    int Version,
    IReadOnlyList<FacturaLineaResponse> Lineas,
    // Etiquetas resueltas server-side vía read ports (ADR-0042): el
    // cliente no tiene catálogo completo. Null si el id no resuelve.
    string? ProveedorNombre = null,
    string? SucursalNombre = null,
    decimal Elegible = 0,
    decimal Retenido = 0,
    IReadOnlyList<Domain.Cfdi.RetencionCfdi>? RetencionesDetalle = null,
    string? Obra = null, string? ConceptoRetencion = null, string? AlertaRetenciones = null, decimal CargosAplicadosTotal = 0,
    string? OrdenCompraFolio = null,
    DateTimeOffset? RetencionesRevisadasEn = null,
    string? RetencionesRevisionMotivo = null,
    RevisionVigenteResponse? RevisionVigente = null,
    Guid? RecepcionId = null,
    string? RecepcionFolio = null);

/// <summary>Revisión por área en curso (C-06): motivo, área, fecha de envío y plazo en días hábiles.</summary>
public sealed record RevisionVigenteResponse(
    string? MotivoNombre,
    string? AreaNombre,
    DateTimeOffset FechaEnvio,
    int DiasHabiles,
    int? SlaDias,
    // 1 = aviso al gerente, 2 = plazo vencido, 3 = escalada; null = sin aviso en el episodio vigente.
    int? NivelAviso,
    bool Vencida);

public sealed record FacturaLineaResponse(
    Guid Id,
    int Posicion,
    Guid? ArticuloId,
    string? ClaveProdServ,
    string Descripcion,
    decimal Cantidad,
    string ClaveUnidad,
    decimal PrecioUnitario,
    decimal Importe,
    decimal? Descuento,
    Guid? LineaOcId,
    Guid? ConceptoContableId);

public sealed class GetFacturaPorIdHandler : IRequestHandler<GetFacturaPorIdQuery, FacturaDetalleResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IProveedorReadPort _proveedores;
    private readonly ISucursalReadPort _sucursales;
    private readonly IComprasOcReadPort _ordenes;
    private readonly Elegibilidad.ElegibilidadFacturaService _elegibilidad;
    private readonly IClock _clock;
    private readonly ICalendarioHabil _calendario;
    private readonly IDependenciaRevisoraReadPort _dependencias;

    public GetFacturaPorIdHandler(
        CuentasPorPagarDbContext db,
        IProveedorReadPort proveedores,
        ISucursalReadPort sucursales, Elegibilidad.ElegibilidadFacturaService elegibilidad,
        IComprasOcReadPort ordenes,
        IClock clock, ICalendarioHabil calendario, IDependenciaRevisoraReadPort dependencias)
    {
        _clock = clock;
        _calendario = calendario;
        _dependencias = dependencias;
        _db = db;
        _proveedores = proveedores;
        _sucursales = sucursales;
        _elegibilidad = elegibilidad;
        _ordenes = ordenes;
    }

    public async Task<FacturaDetalleResponse> Handle(GetFacturaPorIdQuery query, CancellationToken cancellationToken)
    {
        var f = await _db.FacturasProveedor
            .Include(f => f.Lineas)
            .FirstOrDefaultAsync(f => f.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_NO_ENCONTRADA",
                $"No se encontró la factura con id '{query.Id}'.");

        var proveedor = await _proveedores.ObtenerAsync(f.ProveedorId, cancellationToken);
        var sucursal = await _sucursales.ObtenerAsync(f.SucursalId, cancellationToken);
        var orden = f.OrdenCompraId is Guid ocId ? await _ordenes.ObtenerAsync(ocId, cancellationToken) : null;

        RevisionVigenteResponse? revision = null;
        if (f is { EnRevision: true, MotivoRevisionId: Guid motivoId, FechaEntradaRevision: DateTimeOffset entrada })
        {
            var motivo = await _db.MotivosRevision.AsNoTracking().Where(m => m.Id == motivoId)
                .Select(m => new { m.Nombre, m.SlaDias }).FirstOrDefaultAsync(cancellationToken);
            var area = f.DependenciaRevisoraId is Guid depId
                ? await _dependencias.ObtenerAsync(depId, cancellationToken) : null;
            var dias = await PlazoRevision.DiasHabilesAsync(
                _calendario, entrada, _clock.UtcNow, new Dictionary<DateOnly, int>(), cancellationToken);
            var nivel = await _db.AvisosSlaRevision.AsNoTracking()
                .Where(a => a.FacturaProveedorId == f.Id && a.FechaEntradaRevision == entrada)
                .Select(a => (int?)a.Nivel).MaxAsync(cancellationToken);
            revision = new RevisionVigenteResponse(
                motivo?.Nombre, area?.Nombre, entrada, dias, motivo?.SlaDias, nivel,
                motivo?.SlaDias is int sla && dias >= PlazoRevision.Umbrales(sla).Vencido);
        }

        var folioRecepcion = f.RecepcionId is Guid rid
            ? await _db.RecepcionesOcLocal.AsNoTracking().Where(r => r.RecepcionId == rid)
                .Select(r => r.FolioRecepcion).FirstOrDefaultAsync(cancellationToken)
            : null;

        var elegibilidad = await _elegibilidad.CalcularAsync(f, cancellationToken);
        return new FacturaDetalleResponse(
            Id: f.Id,
            EmpresaId: f.EmpresaId,
            CfdiRecibidoId: f.CfdiRecibidoId,
            UuidCfdi: f.UuidCfdi,
            ProveedorId: f.ProveedorId,
            SucursalId: f.SucursalId,
            FolioProveedor: f.FolioProveedor,
            SerieProveedor: f.SerieProveedor,
            FechaDocumento: f.FechaDocumento,
            FechaContabilizacion: f.FechaContabilizacion,
            FechaVencimiento: f.FechaVencimiento,
            Moneda: f.Moneda,
            TipoCambio: f.TipoCambio,
            Subtotal: f.Subtotal,
            Descuentos: f.Descuentos,
            ImpuestosTrasladados: f.ImpuestosTrasladados,
            Retenciones: f.Retenciones,
            Total: f.Total,
            OrdenCompraId: f.OrdenCompraId,
            OrdenCompraFolio: orden?.Folio,
            RecepcionId: f.RecepcionId,
            RecepcionFolio: folioRecepcion,
            Estado: f.Estado,
            DiferenciaContraOc: f.DiferenciaContraOc,
            AnticipoAplicadoTotal: f.AnticipoAplicadoTotal,
            NcAplicadasTotal: f.NcAplicadasTotal,
            ImportePagado: f.ImportePagado,
            SaldoPendiente: f.SaldoPendiente,
            MotivoCancelacion: f.MotivoDeCancelacion,
            MotivoCancelacionTexto: f.MotivoCancelacionTexto,
            EnRevision: f.EnRevision,
            Version: f.Version,
            Lineas: f.Lineas.OrderBy(l => l.Posicion).Select(l => new FacturaLineaResponse(
                l.Id, l.Posicion, l.ArticuloId, l.ClaveProdServ, l.Descripcion,
                l.Cantidad, l.ClaveUnidad, l.PrecioUnitario, l.Importe, l.Descuento,
                l.LineaOcId, l.ConceptoContableId)).ToList(),
            ProveedorNombre: proveedor?.RazonSocial,
            SucursalNombre: sucursal is null ? null : $"{sucursal.Codigo} · {sucursal.Nombre}",
            Elegible: elegibilidad.ElegiblePendiente, Retenido: elegibilidad.Retenido,
            RetencionesDetalle: f.RetencionesDetalle, Obra: f.Obra, ConceptoRetencion: f.ConceptoRetencion, AlertaRetenciones: f.AlertaRetenciones, CargosAplicadosTotal: f.CargosAplicadosTotal,
            RetencionesRevisadasEn: f.RetencionesRevisadasEn, RetencionesRevisionMotivo: f.RetencionesRevisionMotivo,
            RevisionVigente: revision);
    }
}
