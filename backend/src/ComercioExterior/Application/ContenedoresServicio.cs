using MediatR;
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
    decimal Cantidad, string Unidad, long? VersionOrigenAsignada, EstadoPartida Estado, Guid ConfirmadoPorId);
public sealed record ContenedorDto(Guid Id, string? Numero, string? Sello, string? Booking, string? BlGuia, string? Ruta,
    DateOnly? FechaCargaPrevista, DateOnly? FechaEmbarqueReal, decimal? PesoKg, int? Bultos, EstadoContenedor Estado, string? MotivoCancelacion);
public sealed record ContenedoresDetalle(int Version, PedimentoExportacionDatos? Pedimento,
    IReadOnlyList<ContenedorDto> Contenedores, IReadOnlyList<PartidaDto> Partidas, IReadOnlyList<PedidoEmbarcable> Pedidos, IReadOnlyDictionary<string, string> Aduanas);
public sealed record MutarContenedorCommand(Guid ExpedienteId, int Version, string Accion, Guid? ContenedorId = null,
    DatosContenedor? Datos = null, PartidaDatos? Partida = null, Guid? PartidaId = null, DateOnly? Fecha = null,
    string? Motivo = null, PedimentoExportacionDatos? Pedimento = null) : IRequest<ContenedoresDetalle>;

public sealed class ContenedoresServicio(ComercioExteriorDbContext db, ExpedienteServicio servicio, IPedidosEmbarcablesReadPort pedidos)
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
        var origen = await pedidos.ObtenerAsync(e.Partidas.Select(p => p.PedidoFacturableId).Distinct().ToArray(), ct);
        var versiones = origen.ToDictionary(p => p.Id, p => p.VersionOrigen);
        return new(e.Version, e.ClavePedimento is null || e.FechaPedimento is null ? null :
            new(e.NumeroPedimentoExportacion ?? "", e.ClavePedimento, e.FechaPedimento.Value, e.DocumentoPedimentoExportacionId, e.AduanaPedimentoExportacion,
                e.AduanaPedimentoExportacion is { } aduana ? CatalogoAduanas.Aduanas.GetValueOrDefault(aduana) : null),
            e.Contenedores.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).Select(c => new ContenedorDto(c.Id, c.Numero, c.Sello, c.Booking, c.BlGuia, c.Ruta, c.FechaCargaPrevista, c.FechaEmbarqueReal, c.PesoKg, c.Bultos, c.Estado, c.MotivoCancelacion)).ToArray(),
            e.Partidas.Select(p => new PartidaDto(p.Id, p.ContenedorId, p.PedidoFacturableId, p.Posicion, p.ClienteId, p.Cantidad, p.Unidad, p.VersionOrigenAsignada,
                p.Estado == EstadoPartida.Liberada ? EstadoPartida.Liberada : versiones.TryGetValue(p.PedidoFacturableId, out var version) ? p.EstadoActual(version) : EstadoPartida.RequiereRevision, p.ConfirmadoPorId)).ToArray(), origen, CatalogoAduanas.Aduanas);
    }
}

public sealed class MutarContenedorHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, ContenedoresServicio contenedores,
    IPedidosEmbarcablesReadPort pedidos, ICatalogosCeReadPort catalogos, IClock clock) : IRequestHandler<MutarContenedorCommand, ContenedoresDetalle>
{
    public async Task<ContenedoresDetalle> Handle(MutarContenedorCommand request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorContenedoresGestionar, cancellationToken);
        ContenedoresDetalle? resultado = null;
        var ids = await db.Partidas.AsNoTracking().Where(p => p.ExpedienteId == request.ExpedienteId && p.Estado != EstadoPartida.Liberada &&
                ((request.Accion == "cancelar" && p.ContenedorId == request.ContenedorId) ||
                 ((request.Accion == "quitar-partida" || request.Accion == "confirmar") && p.Id == request.PartidaId)))
            .Select(p => p.PedidoFacturableId).ToListAsync(cancellationToken);
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
                case "guardar":
                    e.GuardarContenedor(request.ContenedorId ?? Guid.CreateVersion7(), request.Datos ?? throw new BusinessRuleException("CE_CONTENEDOR_DATOS_INVALIDOS", "Capture los datos del contenedor."));
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
