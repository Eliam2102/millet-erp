using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Application.PublicPorts;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Npgsql;

namespace Millet.ComercioExterior.Application;
public sealed class CrearExpedienteHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, ICatalogosCeReadPort catalogos, IOcImportacionReadPort compras, ICurrentEmpresaContext empresa, IClock clock, IIntegrationEventPublisher eventos) : IRequestHandler<CrearExpedienteCommand, ExpedienteDetalle>
{
    public async Task<ExpedienteDetalle> Handle(CrearExpedienteCommand request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesCrear, cancellationToken);
        var oc = request.Tipo == TipoExpediente.Importacion && request.OcId is Guid id ? await compras.ObtenerAsync(id, cancellationToken) : null;
        var sucursal = oc?.SucursalId ?? request.SucursalId ?? Guid.Empty;
        await servicio.SucursalAsync(sucursal, true, cancellationToken);
        if (oc is not null && oc.Estado != Millet.Compras.Domain.Oc.EstadoOrdenCompra.Autorizada)
            throw new BusinessRuleException("CE_OC_NO_VIGENTE", "La OC debe estar autorizada o con recepción en curso.");
        if (request.Tipo == TipoExpediente.Importacion && (oc is null || !oc.EsImportacion || oc.InformacionImportacion is not { } info || info.EsVacio))
            throw new BusinessRuleException("CE_OC_SIN_IMPORTACION", "La OC no tiene datos de importación.");
        if (oc is not null && await db.Expedientes.AnyAsync(x => x.OcId == oc.Id && x.Estado == EstadoExpediente.Abierto, cancellationToken))
            throw new ConflictException("CE_OC_CON_EXPEDIENTE", "La OC ya tiene un expediente abierto.");
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
        var e = new Expediente(Guid.CreateVersion7(), empresa.Current ?? throw new UnauthorizedAccessException(), folio, request.Tipo, sucursal, request.Referencia, request.OcId, incoterm, moneda, request.ResponsableUsuarioId, request.Clientes ?? []);
        e.Editar(request.Referencia, incoterm, moneda, request.Tc, request.FechaTc, request.FuenteTc, request.ResponsableUsuarioId);
        e.InicializarHitos(oc?.FechaDocumento ?? clock.HoyLocal());
        db.Expedientes.Add(e);
        await eventos.PublishAsync(new ExpedienteCeAbiertoEvent(e.EmpresaId, clock.UtcNow, e.Id, e.Folio), cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)when (ex.InnerException is PostgresException { ConstraintName: "ux_ce_oc_abierta" })
        {
            throw new ConflictException("CE_OC_CON_EXPEDIENTE", "La OC ya tiene un expediente abierto.", ex);
        }

        return await servicio.DetalleAsync(e, cancellationToken);
    }
}

public sealed class MutarExpedienteHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, ICatalogosCeReadPort catalogos, IIntegrationEventPublisher eventos, IClock clock) : IRequestHandler<MutarExpedienteCommand, ExpedienteDetalle>
{
    public async Task<ExpedienteDetalle> Handle(MutarExpedienteCommand request, CancellationToken cancellationToken)
    {
        var permiso = request.Accion switch
        {
            "cerrar" => PermisosCanonicos.ComercioExteriorExpedientesCerrar,
            "hito-compromiso" or "hito-real" or "hito-omitir" => PermisosCanonicos.ComercioExteriorExpedientesHitosRegistrar,
            "cancelar" => PermisosCanonicos.ComercioExteriorExpedientesCancelar,
            "revisar-documentos" => PermisosCanonicos.ComercioExteriorExpedientesDocumentosRevisar,
            "documentos" => PermisosCanonicos.ComercioExteriorExpedientesAdjuntosSubir,
            "editar" or "participantes" => PermisosCanonicos.ComercioExteriorExpedientesEditar,
            _ => throw new BusinessRuleException("CE_ACCION_INVALIDA", "Acción desconocida.")};
        await servicio.PermisoAsync(permiso, cancellationToken);
        var e = await servicio.CargarAsync(request.Id, true, cancellationToken);
        if (e.Version != request.Version)
            throw new ConcurrencyException("Expediente", request.Id);
        e.ExigirAbierto();
        switch (request.Accion)
        {
            case "cerrar":
                e.Cerrar();
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
            case "documentos":
                var doc = request.Documento ?? throw new BusinessRuleException("CE_DOCUMENTO_INVALIDO", "Faltan datos del documento.");
                var requisito = (await catalogos.RequisitosAsync(e.Tipo, cancellationToken)).SingleOrDefault(x => x.Id == doc.TipoDocumentoId) ?? throw new BusinessRuleException("CE_TIPO_DOCUMENTO_INVALIDO", "Este tipo de documento no aplica a la operación.");
                if (doc.NoAplica)
                    e.MarcarNoAplica(Guid.CreateVersion7(), requisito, doc.Motivo ?? "", servicio.Actor);
                else
                {
                    await catalogos.ValidarAdjuntoAsync(e.Id, requisito.Id, doc.AdjuntoId ?? Guid.Empty, cancellationToken);
                    e.RegistrarDocumento(Guid.CreateVersion7(), requisito, doc.AdjuntoId ?? Guid.Empty, doc.Motivo, servicio.Actor);
                }

                break;
            case "revisar-documentos":
                e.RevisarDocumentos(await catalogos.RequisitosAsync(e.Tipo, cancellationToken), await catalogos.AdjuntosVigentesAsync(e.Id, cancellationToken), servicio.Actor);
                break;
            case "cancelar":
                e.Cancelar(request.Motivo ?? "");
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
