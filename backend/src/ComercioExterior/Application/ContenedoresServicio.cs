using MediatR;
using Millet.Compras.Application.PublicPorts;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.ComercioExterior.Application.PublicPorts;
using Npgsql;
using Millet.SharedKernel.Domain;

namespace Millet.ComercioExterior.Application;
public sealed record PedimentoExportacionDatos(string Numero, string Clave, DateOnly Fecha, Guid? DocumentoId = null, string? Aduana = null, string? AduanaNombre = null);
public sealed record PartidaDatos(Guid PedidoFacturableId, int Posicion, decimal Cantidad);
public sealed record PartidaDto(Guid Id, Guid ContenedorId, Guid PedidoFacturableId, int Posicion, Guid ClienteId,
    decimal Cantidad, string Unidad, long? VersionOrigenAsignada, EstadoPartida Estado, Guid ConfirmadoPorId, decimal? PesoKg = null, int? Bultos = null, string? FraccionArancelaria = null, string? UnidadAduana = null, Guid? AduanalesRevisadoPorId = null);
public sealed record ContenedorDto(Guid Id, string? Numero, string? Sello, string? Booking, string? BlGuia, string? Ruta,
    DateOnly? FechaCargaPrevista, DateOnly? FechaEmbarqueReal, decimal? PesoKg, int? Bultos, EstadoContenedor Estado, string? MotivoCancelacion, ModalidadTransporte? Modalidad = null);
public sealed record ContenedoresDetalle(int Version, PedimentoExportacionDatos? Pedimento,
    IReadOnlyList<ContenedorDto> Contenedores, IReadOnlyList<PartidaDto> Partidas, IReadOnlyList<PedidoEmbarcable> Pedidos, IReadOnlyDictionary<string, string> Aduanas, IReadOnlyList<PartidaImportacionDto>? PartidasImportacion = null, IReadOnlyList<OcPartidaRemanente>? PartidasOc = null, int VersionEmbarque = 0, bool SalidaAutorizada = false, IReadOnlyList<AutorizacionSalidaDto>? Autorizaciones = null);
public sealed record AutorizacionSalidaDto(int VersionEmbarque, Guid ActorId, DateTimeOffset Fecha, bool Autorizada, string? Motivo);
public sealed record AduanalesPartidaDatos(string FraccionArancelaria, string UnidadAduana, bool Revisar);
public sealed record PackingPartidaDatos(decimal PesoKg, int Bultos);
public sealed record PartidaImportacionDto(Guid Id, Guid ContenedorId, Guid OcLineaId, int Posicion, decimal Cantidad, string Unidad);
public sealed record OcPartidaRemanente(Guid Id, int Posicion, string Descripcion, decimal Cantidad, string Unidad, decimal Remanente);
public sealed record PartidaImportacionDatos(Guid OcLineaId, decimal Cantidad);
public sealed record MutarContenedorCommand(Guid ExpedienteId, int Version, string Accion, Guid? ContenedorId = null,
    DatosContenedor? Datos = null, PartidaDatos? Partida = null, Guid? PartidaId = null, DateOnly? Fecha = null,
    string? Motivo = null, PedimentoExportacionDatos? Pedimento = null, PartidaImportacionDatos? Importacion = null, PackingPartidaDatos? Packing = null, AduanalesPartidaDatos? Aduanales = null) : IRequest<ContenedoresDetalle>;

public sealed class ContenedoresServicio(ComercioExteriorDbContext db, ExpedienteServicio servicio, IPedidosEmbarcablesReadPort pedidos, IOcImportacionReadPort compras)
{
    public async Task<ContenedoresDetalle> ObtenerAsync(Guid id, CancellationToken ct)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesLeer, ct);
        return await DetalleAsync(await servicio.CargarAsync(id, false, ct), ct);
    }
    public async Task<IReadOnlyList<PedidoEmbarcable>> ListarPedidosAsync(Guid id, Guid cliente, CancellationToken ct)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorContenedoresGestionar, ct);
        var e = await servicio.CargarAsync(id, true, ct);
        if (!e.Participantes.Any(p => p.ClienteId == cliente))
            throw new BusinessRuleException("CE_PEDIDO_NO_ASIGNABLE", "Vincule primero el cliente en participantes.");
        var candidatos = await pedidos.ListarAsync(cliente, e.SucursalId, ct);
        var ids = candidatos.Select(p => p.Id).ToArray();
        var ajenos = await db.Expedientes.Where(x => x.Id != id && x.Estado == EstadoExpediente.Abierto)
            .SelectMany(x => x.Partidas).Where(p => ids.Contains(p.PedidoFacturableId) && p.Estado != EstadoPartida.Liberada).Select(p => p.PedidoFacturableId).Distinct().ToArrayAsync(ct);
        return candidatos.Where(p => !ajenos.Contains(p.Id)).ToArray();
    }
    public async Task<ContenedoresDetalle> DetalleAsync(Expediente e, CancellationToken ct)
    {
        var oc = e.OcId is Guid ocId ? await compras.ObtenerAsync(ocId, ct) : null;
        var origen = await pedidos.ObtenerAsync(e.Partidas.Select(p => p.PedidoFacturableId).Distinct().ToArray(), ct);
        var versiones = origen.ToDictionary(p => p.Id, p => p.VersionOrigen);
        return new(e.Version, e.ClavePedimento is null || e.FechaPedimento is null ? null :
            new(e.NumeroPedimentoExportacion ?? "", e.ClavePedimento, e.FechaPedimento.Value, e.DocumentoPedimentoExportacionId, e.AduanaPedimentoExportacion,
                e.AduanaPedimentoExportacion is { } aduana ? CatalogoAduanas.Aduanas.GetValueOrDefault(aduana) : null),
            e.Contenedores.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).Select(c => new ContenedorDto(c.Id, c.Numero, c.Sello, c.Booking, c.BlGuia, c.Ruta, c.FechaCargaPrevista, c.FechaEmbarqueReal, c.PesoKg, c.Bultos, c.Estado, c.MotivoCancelacion, c.Modalidad)).ToArray(),
            e.Partidas.Select(p => new PartidaDto(p.Id, p.ContenedorId, p.PedidoFacturableId, p.Posicion, p.ClienteId, p.Cantidad, p.Unidad, p.VersionOrigenAsignada,
                p.Estado == EstadoPartida.Liberada ? EstadoPartida.Liberada : versiones.TryGetValue(p.PedidoFacturableId, out var version) ? p.EstadoActual(version) : EstadoPartida.RequiereRevision, p.ConfirmadoPorId, p.PesoKg, p.Bultos, p.FraccionArancelaria, p.UnidadAduana, p.AduanalesRevisadoPorId)).ToArray(), origen, CatalogoAduanas.Aduanas, e.PartidasImportacion.Select(p => new PartidaImportacionDto(p.Id, p.ContenedorId, p.OcLineaId, p.Posicion, p.Cantidad, p.Unidad)).ToArray(),
            oc?.Partidas?.Select(l => new OcPartidaRemanente(l.Id, l.Posicion, l.Descripcion, l.Cantidad, l.Unidad, e.RemanenteImportacion(l.Id, l.Cantidad))).ToArray(), e.VersionEmbarque, e.SalidaAutorizada, e.AutorizacionesSalida.OrderBy(a => a.Secuencia).Select(a => new AutorizacionSalidaDto(a.VersionEmbarque, a.ActorId, a.Fecha, a.Autorizada, a.Motivo)).ToArray());
    }
}

public sealed class MutarContenedorHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, ContenedoresServicio contenedores,
    IPedidosEmbarcablesReadPort pedidos, ICatalogosCeReadPort catalogos, IClock clock, IOcImportacionReadPort compras) : IRequestHandler<MutarContenedorCommand, ContenedoresDetalle>
{
    public async Task<ContenedoresDetalle> Handle(MutarContenedorCommand request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(request.Accion == "aduanales" ? PermisosCanonicos.ComercioExteriorExpedientesDocumentosRevisar : request.Accion is "autorizar-salida" or "rechazar-salida" ? PermisosCanonicos.ComercioExteriorEmbarquesAutorizarSalida : PermisosCanonicos.ComercioExteriorContenedoresGestionar, cancellationToken);
        ContenedoresDetalle? resultado = null;
        var ids = await db.Partidas.AsNoTracking().Where(p => p.ExpedienteId == request.ExpedienteId && p.Estado != EstadoPartida.Liberada)
            .Select(p => p.PedidoFacturableId).Distinct().ToListAsync(cancellationToken);
        if (request.Partida is not null) ids.Add(request.Partida.PedidoFacturableId);
        await PedidoEmbarqueLock.ExecuteAsync(db, ids, async token =>
        {
            var e = await servicio.CargarAsync(request.ExpedienteId, true, token);
            if (e.Version != request.Version) throw new ConcurrencyException("Expediente", e.Id);
            e.ExigirAbierto();
            var origen = await pedidos.ObtenerAsync(e.Partidas.Select(p => p.PedidoFacturableId)
                .Concat(request.Partida is null ? [] : new[] { request.Partida.PedidoFacturableId }).Distinct().ToArray(), token);
            e.ActualizarRevisiones(origen);
            PedidoEmbarcable Pedido(Guid id) => origen.SingleOrDefault(p => p.Id == id) ?? throw new BusinessRuleException("CE_PEDIDO_NO_ASIGNABLE", "No existe el pedido.");
            bool Facturado(Guid contenedor) => e.Partidas.Where(p => p.ContenedorId == contenedor)
                .Any(p => origen.Any(o => o.Id == p.PedidoFacturableId && o.FacturaVigente));
            var contenedorPartidas = request.Accion == "quitar-partida" ? e.Partidas.SingleOrDefault(p => p.Id == request.PartidaId)?.ContenedorId : request.ContenedorId;
            var enviosOtr = await db.OtrEnvios.Where(x => x.ExpedienteId == e.Id && x.ContenedorId == contenedorPartidas).ToListAsync(token);
            Dictionary<string, string> Asignaciones() => e.Partidas.Where(p => p.ContenedorId == contenedorPartidas && p.Estado != EstadoPartida.Liberada)
                .GroupBy(p => OtrEnvio.ClavePartida(Pedido(p.PedidoFacturableId).NumeroPedido, p.Posicion)).ToDictionary(g => g.Key, g => OtrEnvio.ValorPartida(g.Sum(p => p.Cantidad), string.Join("/", g.Select(p => p.Unidad).Distinct().Order())));
            var antesPartidas = enviosOtr.Count > 0 && request.Accion is ("asignar" or "quitar-partida") ? Asignaciones() : null;
            var antes = request.Accion == "guardar" && request.ContenedorId is Guid contenedorId
                ? OtrEnvio.Datos(e.Contenedor(contenedorId)) : null;
            switch (request.Accion)
            {
                case "autorizar-salida":
                case "rechazar-salida":
                    e.AutorizarSalida(servicio.Actor, clock.UtcNow, request.Motivo, request.Accion == "autorizar-salida");
                    break;
                case "aduanales":
                    var adu = request.Aduanales ?? throw new BusinessRuleException("CE_DATOS_ADUANALES_INVALIDOS", "Capture los datos aduanales de la partida.");
                    e.GuardarDatosAduanales(request.PartidaId ?? Guid.Empty, adu.FraccionArancelaria, adu.UnidadAduana, servicio.Actor, clock.UtcNow, adu.Revisar);
                    break;
                case "packing":
                    var packing = request.Packing ?? throw new BusinessRuleException("CE_PACKING_INVALIDO", "Capture peso y bultos de la partida.");
                    e.GuardarPackingPartida(request.PartidaId ?? Guid.Empty, packing.PesoKg, packing.Bultos, servicio.Actor);
                    break;
                case "asignar-importacion":
                    var imp = request.Importacion ?? throw new BusinessRuleException("CE_PARTIDA_DATOS_INVALIDOS", "Seleccione una partida de la OC.");
                    var oc = e.OcId is Guid ocId ? await compras.ObtenerAsync(ocId, token) : null;
                    var linea = oc?.Partidas?.SingleOrDefault(l => l.Id == imp.OcLineaId) ?? throw new BusinessRuleException("CE_PARTIDA_DATOS_INVALIDOS", "La partida no pertenece a la OC del expediente.");
                    e.AsignarPartidaImportacion(request.ContenedorId ?? Guid.Empty, linea.Id, linea.Posicion, imp.Cantidad, linea.Cantidad, linea.Unidad, servicio.Actor);
                    break;
                case "quitar-importacion":
                    var retirarImp = e.PartidasImportacion.SingleOrDefault(p => p.Id == request.PartidaId) ?? throw new EntityNotFoundException("CE_PARTIDA_NO_ENCONTRADA", "No existe la partida de importación.");
                    e.QuitarPartidaImportacion(retirarImp.Id); db.Remove(retirarImp);
                    break;
                case "guardar":
                    e.GuardarContenedor(request.ContenedorId ?? Guid.CreateVersion7(), request.Datos ?? throw new BusinessRuleException("CE_CONTENEDOR_DATOS_INVALIDOS", "Capture los datos del contenedor."), servicio.Actor);
                    break;
                case "pedimento":
                    var ped = request.Pedimento ?? throw new BusinessRuleException("CE_PEDIMENTO_INVALIDO", "Capture el pedimento de exportación.");
                    if (ped.DocumentoId is Guid docId)
                    {
                        var doc = e.Documentos.SingleOrDefault(d => d.Id == docId);
                        var tipo = (await catalogos.RequisitosAsync(e.Tipo, token)).SingleOrDefault(x => x.Codigo == "EXP_PEDIMENTO");
                        if (doc is null || tipo is null || doc.TipoDocumentoId != tipo.Id || doc.AdjuntoId is not Guid a || !(await catalogos.AdjuntosVigentesAsync(e.Id, token)).Contains(a))
                            throw new BusinessRuleException("CE_PEDIMENTO_NO_VIGENTE", "Seleccione el documento vigente del pedimento de exportación.");
                    }
                    e.RegistrarPedimentoExportacion(ped.Numero, ped.Clave, ped.Fecha, clock.HoyLocal(), ped.Aduana, ped.DocumentoId);
                    break;
                case "asignar":
                    var datos = request.Partida ?? throw new BusinessRuleException("CE_PARTIDA_DATOS_INVALIDOS", "Seleccione pedido y posición.");
                    if (await db.Expedientes.AnyAsync(x => x.Id != e.Id && x.Estado == EstadoExpediente.Abierto && x.Partidas.Any(p => p.PedidoFacturableId == datos.PedidoFacturableId && p.Estado != EstadoPartida.Liberada), token))
                        throw new ConflictException("CE_PEDIDO_CON_EXPEDIENTE", "El pedido ya pertenece a otro expediente abierto.");
                    e.AsignarPartida(request.ContenedorId ?? Guid.Empty, Pedido(datos.PedidoFacturableId), datos.Posicion, datos.Cantidad, servicio.Actor);
                    break;
                case "confirmar":
                    var partida = e.Partidas.SingleOrDefault(p => p.Id == request.PartidaId) ?? throw new EntityNotFoundException("CE_PARTIDA_NO_ENCONTRADA", "No existe la partida.");
                    e.ConfirmarPartida(partida.Id, Pedido(partida.PedidoFacturableId), servicio.Actor);
                    break;
                case "quitar-partida":
                    var retirar = e.Partidas.SingleOrDefault(p => p.Id == request.PartidaId) ?? throw new EntityNotFoundException("CE_PARTIDA_NO_ENCONTRADA", "No existe la partida.");
                    e.QuitarPartida(retirar.Id, Pedido(retirar.PedidoFacturableId).FacturaVigente);
                    db.Partidas.Remove(retirar);
                    break;
                case "embarcar":
                    e.RegistrarEmbarque(request.ContenedorId ?? Guid.Empty, request.Fecha ?? throw new BusinessRuleException("CE_FECHA_EMBARQUE_INVALIDA", "Capture la fecha real."), clock.HoyLocal(), await catalogos.AdjuntosVigentesAsync(e.Id, token));
                    break;
                case "cancelar":
                    if (e.Tipo == TipoExpediente.Importacion && e.OcId is Guid ocCancelada)
                    {
                        var origenOc = await compras.ObtenerAsync(ocCancelada, token);
                        var lineasContenedor = e.PartidasImportacion.Where(p => p.ContenedorId == request.ContenedorId).Select(p => p.OcLineaId).ToArray();
                        if (origenOc?.Partidas?.Any(p => lineasContenedor.Contains(p.Id) && (p.CantidadRecibida > 0 || p.CantidadFacturada > 0)) == true)
                            throw new BusinessRuleException("CE_CONTENEDOR_CON_RECEPCION", "El contenedor tiene partidas recibidas o facturadas. Registre una incidencia para corregirlo.");
                    }
                    e.CancelarContenedor(request.ContenedorId ?? Guid.Empty, request.Motivo ?? "", Facturado(request.ContenedorId ?? Guid.Empty));
                    break;
                default: throw new BusinessRuleException("CE_ACCION_INVALIDA", "Acción desconocida.");
            }
            if (antesPartidas is not null)
                foreach (var envio in enviosOtr)
                    envio.RegistrarEdicionPartidas(antesPartidas, Asignaciones(), servicio.Actor.ToString(), clock.UtcNow);
            if (antes is not null)
                foreach (var envio in await db.OtrEnvios.Where(x => x.ExpedienteId == e.Id && x.ContenedorId == request.ContenedorId).ToListAsync(token))
                    envio.RegistrarEdicion(antes, OtrEnvio.Datos(e.Contenedor(request.ContenedorId!.Value)), servicio.Actor.ToString(), clock.UtcNow);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { ConstraintName: "ux_ce_pedido_expediente_abierto" })
            { throw new ConflictException("CE_PEDIDO_CON_EXPEDIENTE", "El pedido ya pertenece a otro expediente abierto.", ex); }
            resultado = await contenedores.DetalleAsync(e, token);
        }, cancellationToken);
        return resultado!;
    }
}
