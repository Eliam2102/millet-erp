using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Application.PublicPorts;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Application.PublicPorts;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Application;
public sealed class CrearExpedienteHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, ICatalogosCeReadPort catalogos, IOcImportacionReadPort compras, ICurrentEmpresaContext empresa, IClock clock, IIntegrationEventPublisher eventos) : IRequestHandler<CrearExpedienteCommand, ExpedienteDetalle>
{
    public async Task<ExpedienteDetalle> Handle(CrearExpedienteCommand request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesCrear, cancellationToken);
        var ids = request.OcIds ?? (request.OcId is Guid origen ? [origen] : Array.Empty<Guid>());
        if (ids.Length > 100 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length || (request.OcId is Guid origenId && (ids.Length == 0 || ids[0] != origenId)))
            throw new BusinessRuleException("CE_ORDENES_INVALIDAS", "Seleccione órdenes distintas y conserve la OC de origen.");
        if (request.Tipo == TipoExpediente.Exportacion && ids.Length > 0)
            throw new BusinessRuleException("CE_ORIGEN_INVALIDO", "La exportación nace de una solicitud de contenedor.");
        var ordenes = request.Tipo == TipoExpediente.Importacion ? await compras.ObtenerVariasAsync(ids, cancellationToken) : [];
        var oc = ids.Length == 0 ? null : ordenes.SingleOrDefault(o => o.Id == ids[0]);
        var sucursal = oc?.SucursalId ?? request.SucursalId ?? Guid.Empty;
        await servicio.SucursalAsync(sucursal, true, cancellationToken);
        if (oc is not null && oc.Estado != Millet.Compras.Domain.Oc.EstadoOrdenCompra.Autorizada)
            throw new BusinessRuleException("CE_OC_NO_VIGENTE", "La OC debe estar autorizada o con recepción en curso.");
        if (request.Tipo == TipoExpediente.Importacion && (oc is null || !oc.EsImportacion || oc.InformacionImportacion is not { } datosOc || datosOc.EsVacio))
            throw new BusinessRuleException("CE_OC_SIN_IMPORTACION", "La OC no tiene datos de importación.");
        if (request.Tipo == TipoExpediente.Importacion && (oc is null || ordenes.Count != ids.Length || ordenes.Any(o => !o.EsImportacion || o.InformacionImportacion is not { } datosOrden || datosOrden.EsVacio || o.Estado != Millet.Compras.Domain.Oc.EstadoOrdenCompra.Autorizada || o.ProveedorId != oc.ProveedorId || o.Moneda != oc.Moneda || o.SucursalId != oc.SucursalId)))
            throw new BusinessRuleException("CE_ORDENES_INVALIDAS", "Las órdenes deben ser de importación, autorizadas y del mismo proveedor, moneda y sucursal.");
        var incoterm = request.IncotermId ?? (oc?.InformacionImportacion?.IncotermId ?? (request.Tipo == TipoExpediente.Exportacion ? await catalogos.IncotermDdpAsync(cancellationToken) : Guid.Empty));
        // Cambiar la propuesta de la OC/DDP es una edición y exige su permiso específico.
        var propuesto = oc?.InformacionImportacion?.IncotermId ?? (request.Tipo == TipoExpediente.Exportacion ? await catalogos.IncotermDdpAsync(cancellationToken) : Guid.Empty);
        if (incoterm != propuesto)
            await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesEditar, cancellationToken);
        var moneda = request.Moneda ?? oc?.Moneda ?? "";
        await catalogos.ValidarAsync(incoterm, moneda, sucursal, request.ResponsableUsuarioId, cancellationToken);
        // PLATFORM-TODO(<CeFolioPorAnio>): la secuencia es global; reinicio anual por confirmar con Eliam.
        var numero = await db.Database.SqlQueryRaw<long>("SELECT nextval('comercio_exterior.folio_seq') AS \"Value\"").SingleAsync(cancellationToken);
        var folio = string.Create(CultureInfo.InvariantCulture, $"CE-{(request.Tipo == TipoExpediente.Importacion ? "IMP" : "EXP")}-{clock.HoyLocal().Year}-{numero:D6}");
        var e = new Expediente(Guid.CreateVersion7(), empresa.Current ?? throw new UnauthorizedAccessException(), folio, request.Tipo, sucursal, request.Referencia, oc?.Id, incoterm, moneda, request.ResponsableUsuarioId, request.Clientes ?? []);
        if (oc is not null) e.VincularOrdenesCompra(ids);
        e.Editar(request.Referencia, incoterm, moneda, request.Tc, request.FechaTc, request.FuenteTc, request.ResponsableUsuarioId);
        e.InicializarHitos(oc?.FechaDocumento ?? clock.HoyLocal());
        db.Expedientes.Add(e);
        await eventos.PublishAsync(new ExpedienteCeAbiertoEvent(e.EmpresaId, clock.UtcNow, e.Id, e.Folio), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return await servicio.DetalleAsync(e, cancellationToken);
    }
}

public sealed class MutarExpedienteHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, ICatalogosCeReadPort catalogos, IIntegrationEventPublisher eventos, IClock clock, IPedidosEmbarcablesReadPort pedidos, IOcImportacionReadPort compras, Millet.Compartido.Application.Ports.ITipoDocumentoCondicionalPort tiposDocumento, ConciliacionRecepcionServicio conciliacion) : IRequestHandler<MutarExpedienteCommand, ExpedienteDetalle>
{
    public async Task<ExpedienteDetalle> Handle(MutarExpedienteCommand request, CancellationToken cancellationToken)
    {
        var permiso = request.Accion switch
        {
            "cerrar" => PermisosCanonicos.ComercioExteriorExpedientesAprobarCierre,
            "cerrar-fisico" or "cerrar-documental" or "cerrar-financiero" => PermisosCanonicos.ComercioExteriorExpedientesCerrar,
            "hito-compromiso" or "hito-real" or "hito-omitir" => PermisosCanonicos.ComercioExteriorExpedientesHitosRegistrar,
            // M-373: permiso propio; cambiar quién corrige (M-151) es sólo reasignar el permiso a otro rol.
            "hito-corregir" => PermisosCanonicos.ComercioExteriorExpedientesHitosCorregir,
            "cancelar" => PermisosCanonicos.ComercioExteriorExpedientesCancelar,
            "requisito-hito" or "requisito-condicional" or "revisar-documentos" => PermisosCanonicos.ComercioExteriorExpedientesDocumentosRevisar,
            "documentos" => PermisosCanonicos.ComercioExteriorExpedientesAdjuntosSubir,
            "quitar-documento" => PermisosCanonicos.ComercioExteriorExpedientesAdjuntosBaja,
            "editar" or "participantes" => PermisosCanonicos.ComercioExteriorExpedientesEditar,
            _ => throw new BusinessRuleException("CE_ACCION_INVALIDA", "Acción desconocida.")};
        await servicio.PermisoAsync(permiso, cancellationToken);
        if (RequiereConciliacion(request.Accion))
        {
            ExpedienteDetalle? resultado = null;
            await PedidoEmbarqueLock.ExecuteAsync(db, [], async ct =>
            {
                var origen = await db.Expedientes.AsNoTracking().Where(e => e.Id == request.Id)
                    .Select(e => new { e.EmpresaId, e.SucursalId, e.OrdenesCompraIds }).SingleOrDefaultAsync(ct)
                    ?? throw new EntityNotFoundException("CE_EXPEDIENTE_NO_ENCONTRADO", "No existe el expediente.");
                await servicio.SucursalAsync(origen.SucursalId, true, ct);
                await conciliacion.BloquearAsync(origen.EmpresaId, origen.OrdenesCompraIds, ct);
                resultado = await MutarAsync(request, ct);
            }, cancellationToken);
            return resultado!;
        }
        if (request.Accion == "cancelar")
        {
            var ids = await db.Partidas.AsNoTracking().Where(p => p.ExpedienteId == request.Id && p.Estado != EstadoPartida.Liberada)
                .Select(p => p.PedidoFacturableId).Distinct().ToArrayAsync(cancellationToken);
            var ordenes = await db.Expedientes.Where(e => e.Id == request.Id).Select(e => e.OrdenesCompraIds).SingleOrDefaultAsync(cancellationToken);
            ids = ids.Concat(ordenes ?? []).Distinct().ToArray();
            ExpedienteDetalle? resultado = null;
            await PedidoEmbarqueLock.ExecuteAsync(db, ids, async ct => resultado = await MutarAsync(request, ct), cancellationToken);
            return resultado!;
        }
        return await MutarAsync(request, cancellationToken);
    }

    private static bool RequiereConciliacion(string accion) => accion is "revisar-documentos" or "cerrar" or "cerrar-fisico" or "cerrar-documental" or "cerrar-financiero";

    private async Task<ExpedienteDetalle> MutarAsync(MutarExpedienteCommand request, CancellationToken cancellationToken)
    {
        var e = await servicio.CargarAsync(request.Id, true, cancellationToken);
        if (e.Version != request.Version)
            throw new ConcurrencyException("Expediente", request.Id);
        e.ExigirAbierto();
        if (RequiereConciliacion(request.Accion) && e.Tipo == TipoExpediente.Importacion)
            await conciliacion.AplicarAsync(e.EmpresaId, e.OrdenesCompraIds,
                e.PartidasImportacion.Where(p => p.CantidadRecibida is not null).Select(p => p.OcLineaId).Distinct().ToArray(),
                null, clock.HoyLocal(), servicio.Actor, clock.UtcNow, cancellationToken);
        switch (request.Accion)
        {
            case "requisito-hito":
                var rh = request.RequisitoHito ?? throw new BusinessRuleException("CE_DOCUMENTO_INVALIDO", "Seleccione el requisito y su hito.");
                if (!(await catalogos.RequisitosAsync(e.Tipo, cancellationToken)).Any(r => r.Id == rh.TipoDocumentoId))
                    throw new BusinessRuleException("CE_TIPO_DOCUMENTO_INVALIDO", "El documento no corresponde al tipo de operación.");
                var config = await db.RequisitosHitos.SingleOrDefaultAsync(x => x.Tipo == e.Tipo && x.TipoDocumentoId == rh.TipoDocumentoId, cancellationToken);
                if (config is null) db.RequisitosHitos.Add(new(e.EmpresaId, e.Tipo, rh.TipoDocumentoId, rh.HitoClave));
                else config.Cambiar(rh.HitoClave);
                break;
            case "requisito-condicional":
                var rc = request.RequisitoCondicional ?? throw new BusinessRuleException("CE_DOCUMENTO_INVALIDO", "Seleccione el requisito.");
                if (!(await catalogos.RequisitosAsync(e.Tipo, cancellationToken)).Any(r => r.Id == rc.TipoDocumentoId))
                    throw new BusinessRuleException("CE_TIPO_DOCUMENTO_INVALIDO", "El documento no corresponde al tipo de operación.");
                await tiposDocumento.CambiarCondicionalAsync(Millet.ComercioExterior.Infrastructure.PublicAdapters.CatalogosCeReadAdapter.Entidad, rc.TipoDocumentoId, rc.Condicional, cancellationToken);
                break;
            case "hito-corregir":
                var hc = request.Hito ?? throw new BusinessRuleException("CE_HITO_INVALIDO", "Faltan datos del hito.");
                e.CorregirHito(hc.Clave, hc.FechaReal, hc.Omitir, hc.Motivo, servicio.Actor, clock.UtcNow, clock.HoyLocal(),
                    await catalogos.RequisitosAsync(e.Tipo, cancellationToken), await catalogos.AdjuntosVigentesAsync(e.Id, cancellationToken));
                break;
            case "cerrar":
            case "cerrar-fisico":
            case "cerrar-documental":
            case "cerrar-financiero":
                if (request.Accion is "cerrar" or "cerrar-financiero")
                {
                    var pagosPendientes = await servicio.MotivosPagoServiciosAsync(e, cancellationToken);
                    if (pagosPendientes.Count > 0)
                        throw new BusinessRuleException("CE_SERVICIOS_PENDIENTES", string.Join(" ", pagosPendientes));
                }
                var requisitosCierre = await catalogos.RequisitosAsync(e.Tipo, cancellationToken);
                var adjuntosCierre = await catalogos.AdjuntosVigentesAsync(e.Id, cancellationToken);
                var recibidas = (await compras.ObtenerVariasAsync(e.OrdenesCompraIds, cancellationToken)).SelectMany(o => o.Partidas ?? []).ToDictionary(p => p.Id, p => p.CantidadRecibida);
                if (request.Accion == "cerrar") e.AprobarCierre(servicio.Actor, clock.UtcNow, requisitosCierre, adjuntosCierre, recibidas);
                else e.CerrarDimension(request.Accion[7..], servicio.Actor, clock.UtcNow, requisitosCierre, adjuntosCierre, recibidas);
                break;
            case "hito-compromiso":
            case "hito-real":
            case "hito-omitir":
                var h = request.Hito ?? throw new BusinessRuleException("CE_HITO_INVALIDO", "Faltan datos del hito.");
                await catalogos.ValidarResponsableAsync(e.SucursalId, h.ResponsableUsuarioId, cancellationToken);
                if (request.Accion == "hito-compromiso")
                    e.CambiarCompromiso(h.Clave, h.FechaCompromiso ?? throw new BusinessRuleException("CE_FECHA_COMPROMISO_REQUERIDA", "Capture la fecha compromiso."), h.ResponsableUsuarioId, h.Motivo);
                else if (request.Accion == "hito-omitir")
                    e.OmitirHito(h.Clave, h.Motivo ?? "", h.ResponsableUsuarioId);
                else
                {
                    e.RegistrarHito(h.Clave, h.FechaReal ?? throw new BusinessRuleException("CE_FECHA_REAL_REQUERIDA", "Capture la fecha real."), h.ResponsableUsuarioId, h.EvidenciaAdjuntoId, h.Motivo, await catalogos.RequisitosAsync(e.Tipo, cancellationToken), await catalogos.AdjuntosVigentesAsync(e.Id, cancellationToken), clock.HoyLocal());
                    if (e.Tipo == TipoExpediente.Importacion && h.Clave == "despacho-aduanal") e.RegistrarAduanaEntrada(h.AduanaEntrada);
                    await eventos.PublishAsync(new ExpedienteCeHitoRegistradoEvent(e.EmpresaId, clock.UtcNow, e.Id, h.Clave, h.FechaReal!.Value, h.ResponsableUsuarioId), cancellationToken);
                }
                break;
            case "editar":
                var d = request.Datos ?? throw new BusinessRuleException("CE_DATOS_INVALIDOS", "Faltan datos de edición.");
                await catalogos.ValidarAsync(d.IncotermId, d.Moneda, e.SucursalId, d.ResponsableUsuarioId, cancellationToken);
                e.Editar(d.Referencia, d.IncotermId, d.Moneda, d.Tc, d.FechaTc, d.FuenteTc, d.ResponsableUsuarioId);
                break;
            case "participantes":
                var p = request.Participante ?? throw new BusinessRuleException("CE_PARTICIPANTE_INVALIDO", "Faltan datos del participante.");
                await catalogos.ValidarParticipanteAsync(p.ProveedorId, p.ClienteId, cancellationToken);
                if (request.ParticipanteId is Guid pid)
                    e.EditarParticipante(pid, p.Nombre, p.Tipo, p.Contacto, p.Papel, p.ProveedorId, p.ClienteId, p.Descripcion);
                else
                    e.AgregarParticipante(Guid.CreateVersion7(), p.Nombre, p.Tipo, p.Contacto, p.Papel, p.ProveedorId, p.ClienteId, p.Descripcion);
                break;
            case "quitar-documento":
                e.RetirarDocumento(request.DocumentoId ?? Guid.Empty, request.Motivo ?? "");
                break;
            case "documentos":
                var doc = request.Documento ?? throw new BusinessRuleException("CE_DOCUMENTO_INVALIDO", "Faltan datos del documento.");
                var requisito = (await catalogos.RequisitosAsync(e.Tipo, cancellationToken)).SingleOrDefault(x => x.Id == doc.TipoDocumentoId) ?? throw new BusinessRuleException("CE_TIPO_DOCUMENTO_INVALIDO", "Este tipo de documento no aplica a la operación.");
                if (doc.NoAplica)
                    e.MarcarNoAplica(Guid.CreateVersion7(), requisito, doc.Motivo ?? "", servicio.Actor);
                else
                {
                    await catalogos.ValidarAdjuntoAsync(e.Id, requisito.Id, doc.AdjuntoId ?? Guid.Empty, cancellationToken);
                    e.RegistrarDocumento(Guid.CreateVersion7(), requisito, doc.AdjuntoId ?? Guid.Empty, doc.Motivo, servicio.Actor, doc.NumeroPedimento, doc.ReemplazaA);
                }

                break;
            case "revisar-documentos":
                e.RevisarDocumentos(await catalogos.RequisitosAsync(e.Tipo, cancellationToken), await catalogos.AdjuntosVigentesAsync(e.Id, cancellationToken), servicio.Actor);
                break;
            case "cancelar":
                var origen = await pedidos.ObtenerAsync(e.Partidas.Where(p => p.Estado != EstadoPartida.Liberada)
                    .Select(p => p.PedidoFacturableId).Distinct().ToArray(), cancellationToken);
                e.Cancelar(request.Motivo ?? "", origen.Any(p => p.FacturaVigente));
                await eventos.PublishAsync(new ExpedienteCeCanceladoEvent(e.EmpresaId, clock.UtcNow, e.Id, e.MotivoCancelacion!), cancellationToken);
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await servicio.DetalleAsync(e, cancellationToken);
    }
}

public sealed class ObtenerExpedienteCeHandler(ExpedienteServicio servicio) : IRequestHandler<ObtenerExpedienteCeQuery, ExpedienteDetalle>
{
    public async Task<ExpedienteDetalle> Handle(ObtenerExpedienteCeQuery request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesLeer, cancellationToken);
        return await servicio.DetalleAsync(await servicio.CargarAsync(request.Id, false, cancellationToken), cancellationToken);
    }
}

public sealed class ListarExpedientesHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, ICatalogosCeReadPort catalogos, IClock clock) : IRequestHandler<ListarExpedientesQuery, ExpedientesPagina>
{
    public async Task<ExpedientesPagina> Handle(ListarExpedientesQuery request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesLeer, cancellationToken);
        var permitidas = await servicio.SucursalesAsync(request.SucursalId, cancellationToken);
        var q = db.Expedientes.AsNoTracking();
        if (permitidas is not null)
            q = q.Where(x => permitidas.Contains(x.SucursalId));
        if (request.Tipo is { } tipo)
            q = q.Where(x => x.Tipo == tipo);
        if (request.Estado is { } estado)
            q = q.Where(x => x.Estado == estado);
        if (!string.IsNullOrWhiteSpace(request.Q))
            q = q.Where(x => EF.Functions.ILike(x.Folio, "%" + request.Q.Trim() + "%") || EF.Functions.ILike(x.Referencia, "%" + request.Q.Trim() + "%"));
        var hoy = clock.HoyLocal();
        if (request.Atraso is bool atraso)
            q = q.Where(x => (x.Estado == EstadoExpediente.Abierto && x.Hitos.Any(h => h.FechaCompromiso < hoy && h.FechaReal == null && !h.Omitido)) == atraso);
        var total = await q.CountAsync(cancellationToken);
        var offset = Math.Max(0, request.Offset);
        var limit = Math.Clamp(request.Limit, 1, 100);
        var items = await q.Include(x => x.Hitos).Include(x => x.Avisos).AsSplitQuery().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        var nombres = await catalogos.NombresAsync(items.Select(x => x.ResponsableUsuarioId).Distinct(), cancellationToken);
        var sucursales = await catalogos.SucursalesAsync(items.Select(x => x.SucursalId).Distinct(), cancellationToken);
        return new(items.Select(x => ExpedienteServicio.Fila(x, nombres, sucursales, hoy)).ToArray(), total, offset, limit);
    }
}
