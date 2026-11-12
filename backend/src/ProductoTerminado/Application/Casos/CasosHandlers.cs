using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Entregas;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Application.Casos;

/// <summary>
/// F1-PT-06 / M-351: registra daño, rechazo o devolución de piezas de un pedido. La etapa sale del estado de las piezas: en PT (o rotas al
/// cargar) = AntesDeCarga; rechazadas en sitio = RechazoEnSitio; entregadas que regresaron = DevolucionCliente. <c>Evidencia</c>: foto en base64.
/// </summary>
public sealed record RegistrarCasoDanoCommand(IReadOnlyList<string> Etiquetas, string Motivo, string? Evidencia) : IRequest<CasoDanoDto>;

/// <summary>M-351: completa la evidencia faltante antes del dictamen.</summary>
public sealed record AdjuntarEvidenciaCasoCommand(Guid Id, string Evidencia) : IRequest<CasoDanoDto>;

/// <summary>M-351: dictamen de Calidad (RegresarDisponible, Reproceso, Refabricacion, Desecho). Otra persona distinta de quien registró.</summary>
public sealed record DictaminarCasoCommand(Guid Id, string Dictamen, string Observacion, string? PedidoRefabricacionAw) : IRequest<CasoDanoDto>;

/// <summary>M-352: emite la nota de crédito de devolución por las piezas facturadas del caso.</summary>
public sealed record EmitirNotaCreditoCasoCommand(Guid Id) : IRequest<CasoDanoDto>;

/// <summary>M-352: pide la cancelación de la factura de las piezas del caso (motivo SAT 01-04).</summary>
public sealed record SolicitarCancelacionFacturaCasoCommand(Guid Id, string MotivoSat, string? UuidSustituto) : IRequest<CasoDanoDto>;

public sealed record CerrarCasoCommand(Guid Id) : IRequest<CasoDanoDto>;

public sealed record ListarCasosQuery(string? Estado, string? Pedido, int Page, int PageSize) : IRequest<CasosPagina>;

public sealed record ObtenerCasoQuery(Guid Id) : IRequest<CasoDanoDto>;

public sealed record ObtenerEvidenciaCasoQuery(Guid Id) : IRequest<EntregaEvidenciaDto>;

/// <summary><c>Factura</c>: la factura timbrada de las piezas (si ya se facturaron). <c>Documento</c>: NC o cancelación ligada y su estado vigente.</summary>
public sealed record CasoDanoDto(Guid Id, string Folio, string NumeroPedido, Guid SucursalId, string Etapa, string Motivo, bool TieneEvidencia, string Estado,
    IReadOnlyList<CasoPiezaDto> Piezas, Guid RegistradoPorId, DateTimeOffset RegistradoAt, string? Dictamen, string? ObservacionDictamen, string? PedidoRefabricacionAw,
    Guid? DictaminadoPorId, DateTimeOffset? DictaminadoAt, string? FacturaFolio, string? Correctivo, DocumentoFiscalPt? Documento, DateTimeOffset? CerradoAt);

public sealed record CasoPiezaDto(string Etiqueta, int Posicion, string Estado);

public sealed record CasosPagina(IReadOnlyList<CasoDanoDto> Items, int Total);

internal static class CasoMapeo
{
    public static async Task<CasoDanoDto> ADtoAsync(ProductoTerminadoDbContext db, IFacturacionSalidaPort facturacion, CasoDanoPt c, CancellationToken ct)
    {
        var pedido = await db.PedidosPt.AsNoTracking().Where(p => p.Id == c.PedidoPtId).Select(p => p.NumeroPedido).FirstAsync(ct);
        var ids = c.Piezas.Select(p => p.PiezaId).ToList();
        var estados = await db.Piezas.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Estado.ToString(), ct);
        var factura = await FacturaAsync(db, facturacion, c, ct);
        var documento = c.DocumentoFiscalId is { } doc ? await facturacion.EstadoDocumentoAsync(c.Correctivo == CorrectivoFiscalPt.NotaCredito, doc, ct) : null;
        return new CasoDanoDto(c.Id, c.Folio, pedido, c.SucursalId, c.Etapa.ToString(), c.Motivo, c.Evidencia is not null, c.Estado.ToString(),
            [.. c.Piezas.OrderBy(p => p.Posicion).ThenBy(p => p.Etiqueta).Select(p => new CasoPiezaDto(p.Etiqueta, p.Posicion, estados.GetValueOrDefault(p.PiezaId) ?? ""))],
            c.RegistradoPorId, c.RegistradoAt, c.Dictamen?.ToString(), c.ObservacionDictamen, c.PedidoRefabricacionAw, c.DictaminadoPorId, c.DictaminadoAt,
            factura?.Folio, c.Correctivo?.ToString(), documento, c.CerradoAt);
    }

    /// <summary>Factura timbrada de las piezas del caso, por el albarán emitido y vigente con el que salieron. Una sola por caso.</summary>
    public static async Task<FacturaAlbaranRuta?> FacturaAsync(ProductoTerminadoDbContext db, IFacturacionSalidaPort facturacion, CasoDanoPt c, CancellationToken ct)
    {
        var ids = c.Piezas.Select(p => p.PiezaId).ToList();
        var folios = await (from ap in db.AlbaranPiezas join a in db.Albaranes on ap.AlbaranPtId equals a.Id
                            where ids.Contains(ap.PiezaId) && a.CanceladoAt == null && a.EmitidoAt != null
                            select a.Folio).Distinct().ToListAsync(ct);
        if (folios.Count == 0) return null;
        var facturas = await facturacion.FacturasDeAlbaranesAsync(folios, ct);
        var distintas = facturas.Values.GroupBy(f => f.ComprobanteId).Select(g => g.First()).ToList();
        if (distintas.Count > 1)
            throw new BusinessRuleException("PT_CASO_VARIAS_FACTURAS", "Las piezas del caso salieron en facturas distintas: registre un caso por factura.");
        return distintas.FirstOrDefault();
    }

    public static async Task<CasoDanoPt> CargarAsync(ProductoTerminadoDbContext db, PtAcceso acceso, Guid id, string permisoTodas, CancellationToken ct)
    {
        var c = await db.CasosDano.Include(x => x.Piezas).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("PT_CASO_NO_ENCONTRADO", "No existe el caso.");
        await acceso.VerificarSucursalAsync(c.SucursalId, permisoTodas, ct);
        return c;
    }
}

public sealed class RegistrarCasoDanoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IFacturacionSalidaPort facturacion, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<RegistrarCasoDanoCommand, CasoDanoDto>
{
    public async Task<CasoDanoDto> Handle(RegistrarCasoDanoCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();
        var etiquetas = c.Etiquetas.Select(e => e.Trim()).Where(e => e.Length > 0).Distinct().ToList();
        var piezas = await db.Piezas.Where(p => etiquetas.Contains(p.Etiqueta)).ToListAsync(cancellationToken);
        var faltan = etiquetas.Except(piezas.Select(p => p.Etiqueta)).ToList();
        if (faltan.Count > 0 || piezas.Count == 0) throw new BusinessRuleException("PT_CASO_PIEZAS_INVALIDAS", $"Etiquetas inexistentes: {string.Join(", ", faltan)}");
        var sucursales = piezas.Select(p => p.SucursalId).Distinct().ToList();
        if (sucursales.Count > 1) throw new BusinessRuleException("PT_CASO_VARIAS_SUCURSALES", "Las piezas del caso están en una misma sucursal.");
        await acceso.VerificarSucursalAsync(sucursales[0], PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);

        var ids = piezas.Select(p => p.Id).ToList();
        var abiertos = await (from cp in db.CasoDanoPiezas join k in db.CasosDano on cp.CasoDanoPtId equals k.Id
                              where ids.Contains(cp.PiezaId) && k.Estado != EstadoCasoDanoPt.Cerrado select cp.Etiqueta).ToListAsync(cancellationToken);
        if (abiertos.Count > 0) throw new BusinessRuleException("PT_CASO_PIEZA_EN_CASO", $"Piezas que ya están en un caso abierto: {string.Join(", ", abiertos)}");
        var etapas = piezas.Select(p => p.Estado switch
        {
            EstadoPieza.EnPT => EtapaDanoPt.AntesDeCarga,
            EstadoPieza.EnRevision => EtapaDanoPt.RechazoEnSitio,
            EstadoPieza.Entregada => EtapaDanoPt.DevolucionCliente,
            _ => throw new BusinessRuleException("PT_PIEZA_NO_SEGREGABLE", $"La pieza {p.Etiqueta} está {p.Estado}: solo se registra daño de piezas en PT, rechazadas en sitio o entregadas que regresaron."),
        }).Distinct().ToList();
        if (etapas.Count > 1) throw new BusinessRuleException("PT_CASO_ETAPAS_MEZCLADAS", "Las piezas del caso están en etapas distintas (en PT, rechazadas o entregadas): registre un caso por etapa.");

        var evidencia = EntregaImagen.Decodificar(c.Evidencia, true, CasoDanoPt.MaxEvidenciaBytes, "evidencia");
        var ahora = clock.UtcNow;
        var caso = CasoDanoPt.Registrar(empresaId, sucursales[0], piezas[0].PedidoPtId, etapas[0], piezas, c.Motivo, evidencia, acceso.Actor, ahora, HoraAw.FechaLocal(ahora));
        foreach (var p in piezas)
        {
            var antes = p.Estado;
            p.SegregarPorDano();
            db.Movimientos.Add(MovimientoPieza.Hecho(p, TipoMovimientoPieza.DanoRegistrado, antes, caso.Id.ToString(), ahora, acceso.Actor));
        }
        db.CasosDano.Add(caso);
        await db.SaveChangesAsync(cancellationToken);
        return await CasoMapeo.ADtoAsync(db, facturacion, caso, cancellationToken);
    }
}

public sealed class AdjuntarEvidenciaCasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IFacturacionSalidaPort facturacion) : IRequestHandler<AdjuntarEvidenciaCasoCommand, CasoDanoDto>
{
    public async Task<CasoDanoDto> Handle(AdjuntarEvidenciaCasoCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var caso = await CasoMapeo.CargarAsync(db, acceso, c.Id, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        caso.AdjuntarEvidencia(EntregaImagen.Decodificar(c.Evidencia, true, CasoDanoPt.MaxEvidenciaBytes, "evidencia")
            ?? throw new BusinessRuleException("PT_CASO_SIN_EVIDENCIA", "Adjunte la foto del daño."));
        await db.SaveChangesAsync(cancellationToken);
        return await CasoMapeo.ADtoAsync(db, facturacion, caso, cancellationToken);
    }
}

public sealed class DictaminarCasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IFacturacionSalidaPort facturacion, IClock clock) : IRequestHandler<DictaminarCasoCommand, CasoDanoDto>
{
    public async Task<CasoDanoDto> Handle(DictaminarCasoCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoCalidadDictaminar, cancellationToken);
        if (!Enum.TryParse<DictamenCalidadPt>(c.Dictamen, true, out var dictamen))
            throw new BusinessRuleException("PT_DICTAMEN_INVALIDO", "Dictamen: RegresarDisponible, Reproceso, Refabricacion o Desecho.");
        var caso = await CasoMapeo.CargarAsync(db, acceso, c.Id, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, cancellationToken);
        var ahora = clock.UtcNow;
        caso.Dictaminar(dictamen, c.Observacion, c.PedidoRefabricacionAw, acceso.Actor, ahora);
        var ids = caso.Piezas.Select(p => p.PiezaId).ToList();
        var tipo = dictamen switch
        {
            DictamenCalidadPt.RegresarDisponible => TipoMovimientoPieza.DictamenDisponible,
            DictamenCalidadPt.Reproceso => TipoMovimientoPieza.DictamenReproceso,
            _ => TipoMovimientoPieza.Baja,
        };
        foreach (var p in await db.Piezas.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken))
        {
            var antes = p.Estado;
            p.AplicarDictamen(dictamen);
            db.Movimientos.Add(MovimientoPieza.Hecho(p, tipo, antes, caso.Id.ToString(), ahora, acceso.Actor));
        }
        await db.SaveChangesAsync(cancellationToken);
        return await CasoMapeo.ADtoAsync(db, facturacion, caso, cancellationToken);
    }
}

public sealed class EmitirNotaCreditoCasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IFacturacionSalidaPort facturacion) : IRequestHandler<EmitirNotaCreditoCasoCommand, CasoDanoDto>
{
    public async Task<CasoDanoDto> Handle(EmitirNotaCreditoCasoCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.FacturacionNotasCreditoBonificacion, cancellationToken);
        var caso = await CasoMapeo.CargarAsync(db, acceso, c.Id, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, cancellationToken);
        caso.ValidarCorrectivoPosible();
        var factura = await CasoMapeo.FacturaAsync(db, facturacion, caso, cancellationToken)
            ?? throw new BusinessRuleException("PT_CASO_SIN_FACTURA", "Las piezas del caso no tienen factura timbrada: no hay nota que emitir.");
        var piezas = caso.Piezas.GroupBy(p => p.Posicion).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())).ToList();
        var nc = await facturacion.EmitirNotaCreditoDevolucionAsync(factura.ComprobanteId, piezas, $"Devolución de producto terminado, caso {caso.Folio}", cancellationToken);
        caso.LigarCorrectivo(CorrectivoFiscalPt.NotaCredito, nc.Id, nc.Folio);
        await db.SaveChangesAsync(cancellationToken);
        return await CasoMapeo.ADtoAsync(db, facturacion, caso, cancellationToken);
    }
}

public sealed class SolicitarCancelacionFacturaCasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IFacturacionSalidaPort facturacion)
    : IRequestHandler<SolicitarCancelacionFacturaCasoCommand, CasoDanoDto>
{
    public async Task<CasoDanoDto> Handle(SolicitarCancelacionFacturaCasoCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.FacturacionCancelacionesSolicitar, cancellationToken);
        var caso = await CasoMapeo.CargarAsync(db, acceso, c.Id, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, cancellationToken);
        caso.ValidarCorrectivoPosible();
        var factura = await CasoMapeo.FacturaAsync(db, facturacion, caso, cancellationToken)
            ?? throw new BusinessRuleException("PT_CASO_SIN_FACTURA", "Las piezas del caso no tienen factura timbrada: no hay factura que cancelar.");
        var doc = await facturacion.SolicitarCancelacionFacturaAsync(factura.ComprobanteId, c.MotivoSat, c.UuidSustituto, cancellationToken);
        caso.LigarCorrectivo(CorrectivoFiscalPt.CancelacionFactura, doc.Id, doc.Folio);
        await db.SaveChangesAsync(cancellationToken);
        return await CasoMapeo.ADtoAsync(db, facturacion, caso, cancellationToken);
    }
}

public sealed class CerrarCasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IFacturacionSalidaPort facturacion, IClock clock) : IRequestHandler<CerrarCasoCommand, CasoDanoDto>
{
    public async Task<CasoDanoDto> Handle(CerrarCasoCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var caso = await CasoMapeo.CargarAsync(db, acceso, c.Id, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        var pendiente = caso.DocumentoFiscalId is { } doc
            && (await facturacion.EstadoDocumentoAsync(caso.Correctivo == CorrectivoFiscalPt.NotaCredito, doc, cancellationToken))?.Pendiente != false;
        caso.Cerrar(pendiente, acceso.Actor, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await CasoMapeo.ADtoAsync(db, facturacion, caso, cancellationToken);
    }
}

public sealed class ListarCasosHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IFacturacionSalidaPort facturacion) : IRequestHandler<ListarCasosQuery, CasosPagina>
{
    public async Task<CasosPagina> Handle(ListarCasosQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        var visibles = await acceso.SucursalesVisiblesAsync(PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, cancellationToken);
        var consulta = db.CasosDano.AsNoTracking().Include(x => x.Piezas).AsQueryable();
        if (visibles is not null) consulta = consulta.Where(x => visibles.Contains(x.SucursalId));
        if (q.Estado is { Length: > 0 } e && Enum.TryParse<EstadoCasoDanoPt>(e, out var estado)) consulta = consulta.Where(x => x.Estado == estado);
        if (q.Pedido?.Trim() is { Length: > 0 } numero) consulta = consulta.Where(x => db.PedidosPt.Any(p => p.Id == x.PedidoPtId && p.NumeroPedido == numero));
        var total = await consulta.CountAsync(cancellationToken);
        var items = await consulta.OrderByDescending(x => x.RegistradoAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(cancellationToken);
        var dtos = new List<CasoDanoDto>();
        foreach (var c in items) dtos.Add(await CasoMapeo.ADtoAsync(db, facturacion, c, cancellationToken));
        return new CasosPagina(dtos, total);
    }
}

public sealed class ObtenerCasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IFacturacionSalidaPort facturacion) : IRequestHandler<ObtenerCasoQuery, CasoDanoDto>
{
    public async Task<CasoDanoDto> Handle(ObtenerCasoQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        var caso = await CasoMapeo.CargarAsync(db, acceso, q.Id, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, cancellationToken);
        return await CasoMapeo.ADtoAsync(db, facturacion, caso, cancellationToken);
    }
}

public sealed class ObtenerEvidenciaCasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso) : IRequestHandler<ObtenerEvidenciaCasoQuery, EntregaEvidenciaDto>
{
    public async Task<EntregaEvidenciaDto> Handle(ObtenerEvidenciaCasoQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        var caso = await CasoMapeo.CargarAsync(db, acceso, q.Id, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, cancellationToken);
        return new EntregaEvidenciaDto(null, EntregaImagen.ADataUrl(caso.Evidencia));
    }
}
