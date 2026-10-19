using System.Globalization;
using Microsoft.Extensions.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Application;
public sealed record AplicarEnvioOtr(Guid EmpresaId, OtrEntrada Entrada) : IRequest<bool>;
public sealed record VincularEnvioOtr(Guid Id, int Version, Guid ExpedienteId) : IRequest<OtrDetalle>;
public sealed record AbrirExpedienteDesdeEnvioOtr(Guid Id, int Version, Guid ResponsableUsuarioId, string Moneda) : IRequest<OtrDetalle>;
public sealed record ResolverConflictoOtr(Guid Id, int Version, Guid ConflictoId, bool UsarOtr, string Motivo) : IRequest<OtrDetalle>;
public sealed record ReintentarSalidaOtr(Guid Id, int Version, Guid SalidaId) : IRequest<OtrDetalle>;
public sealed record OtrCandidato(Guid Id, string Folio);
public sealed record OtrLineaDto(Guid Id, long VersionOrigen, string NumeroPedido, int Posicion, decimal Cantidad, string Unidad, EstadoOtrLinea Estado, string? Motivo, Guid? PartidaId);
public sealed record OtrConflictoDto(Guid Id, long VersionOrigen, string Campo, string ValorOtr, string ValorErp, string ActorErp, DateTimeOffset FechaErp, DateTimeOffset RecibidoAt, Guid? ResueltoPorId, DateTimeOffset? ResueltoAt, bool? UsarOtr, string? Motivo, string Estado, long? SuperadoPorVersion);
public sealed record OtrSalidaDto(Guid Id, string Tipo, int VersionErp, EstadoOtrSalida Estado, int Intentos, string? UltimoError, DateTimeOffset? AcuseAt, string? AcuseResultado, string? AcuseMotivo, int Intento, Guid? ReemplazadaPorId, bool SinAcuse);
public sealed record OtrDetalle(Guid Id, string IdOrigen, int Version, long VersionAplicada, string Hash, Guid SucursalId, Guid ClienteId, Guid? ExpedienteId, Guid? ContenedorId, EstadoOtrEnvio Estado, OtrContenido Contenido, IReadOnlyList<OtrLineaDto> Lineas, IReadOnlyList<OtrConflictoDto> Conflictos, IReadOnlyList<OtrSalidaDto> Salidas, string? MotivoVinculacion);

public sealed class OtrServicio(ComercioExteriorDbContext db, IPedidosEmbarcablesReadPort pedidos, ExpedienteServicio seguridad,
    ICatalogosCeReadPort catalogos, IClock clock, ICurrentEmpresaContext empresa, IIntegrationEventPublisher eventos, IConfiguration config) :
    IRequestHandler<AplicarEnvioOtr, bool>, IRequestHandler<VincularEnvioOtr, OtrDetalle>,
    IRequestHandler<AbrirExpedienteDesdeEnvioOtr, OtrDetalle>, IRequestHandler<ResolverConflictoOtr, OtrDetalle>,
    IRequestHandler<ReintentarSalidaOtr, OtrDetalle>
{
    private IQueryable<OtrEnvio> Envios => db.OtrEnvios.Include(x => x.Lineas).Include(x => x.Conflictos).AsSplitQuery();
    private IQueryable<Expediente> Expedientes => db.Expedientes.Include(x => x.Participantes).Include(x => x.Contenedores)
        .Include(x => x.Partidas).Include(x => x.VinculosPedidos).Include(x => x.Hitos).AsSplitQuery();
    public async Task<IReadOnlyList<OtrDetalle>> ListarAsync(Guid? expediente, CancellationToken cancellationToken)
    {
        await seguridad.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesLeer, cancellationToken);
        var sucursales = await seguridad.SucursalesAsync(null, cancellationToken);
        var vencimiento = clock.UtcNow.AddMinutes(-config.GetValue<int>("IntegracionesAw:Exportacion:AcuseVencidoMinutos", 1440));
        var q = Envios.Where(x => expediente == null ? x.Estado == EstadoOtrEnvio.PorVincular || db.OtrSalidas.Any(s => s.OtrEnvioId == x.Id && (s.Estado == EstadoOtrSalida.Fallida || (s.Estado == EstadoOtrSalida.Entregada && s.AcuseAt == null && s.UltimoIntentoAt <= vencimiento))) : x.ExpedienteId == expediente);
        if (expediente is Guid id) await seguridad.CargarAsync(id, false, cancellationToken);
        if (sucursales is not null) q = q.Where(x => sucursales.Contains(x.SucursalId));
        var result = new List<OtrDetalle>();
        foreach (var envio in await q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken)) result.Add(await DetalleAsync(envio, cancellationToken));
        return result;
    }
    public async Task<IReadOnlyList<OtrCandidato>> CandidatosAsync(Guid id, CancellationToken cancellationToken)
    {
        await seguridad.PermisoAsync(PermisosCanonicos.ComercioExteriorOtrGestionar, cancellationToken);
        var envio = await db.OtrEnvios.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new EntityNotFoundException("CE_OTR_NO_ENCONTRADO", "No existe el envío.");
        await seguridad.SucursalAsync(envio.SucursalId, true, cancellationToken);
        envio.ExigirIdentidad();
        return await db.Expedientes.Where(x => x.Tipo == TipoExpediente.Exportacion && x.Estado == EstadoExpediente.Abierto && x.SucursalId == envio.SucursalId && x.Participantes.Any(p => p.ClienteId == envio.ClienteId))
            .OrderBy(x => x.Folio).Select(x => new OtrCandidato(x.Id, x.Folio)).ToArrayAsync(cancellationToken);
    }
    public async Task<OtrDetalle> DetalleAsync(OtrEnvio x, CancellationToken cancellationToken) => new(x.Id, x.IdOrigen, x.Version, x.VersionAplicada, x.Hash, x.SucursalId, x.ClienteId, x.ExpedienteId, x.ContenedorId, x.Estado, x.Contenido(),
        x.Lineas.OrderBy(l => l.VersionOrigen).ThenBy(l => l.NumeroPedido).ThenBy(l => l.Posicion).Select(l => new OtrLineaDto(l.Id, l.VersionOrigen, l.NumeroPedido, l.Posicion, l.Cantidad, l.Unidad, l.Estado, l.Motivo, l.PartidaId)).ToArray(),
        x.Conflictos.Select(c => new OtrConflictoDto(c.Id, c.VersionOrigen, c.Campo, c.ValorOtr, c.ValorErp, c.ActorErp, c.FechaErp, c.RecibidoAt, c.ResueltoPorId, c.ResueltoAt, c.UsarOtr, c.Motivo, c.Estado, c.SuperadoPorVersion)).ToArray(),
        await db.OtrSalidas.Where(s => s.OtrEnvioId == x.Id).OrderByDescending(s => s.VersionErp).Select(s => new OtrSalidaDto(s.Id, s.Tipo, s.VersionErp, s.Estado, s.Intentos, s.UltimoError, s.AcuseAt, s.AcuseResultado, s.AcuseMotivo, s.Intento, s.ReemplazadaPorId, s.Estado == EstadoOtrSalida.Entregada && s.AcuseAt == null && s.UltimoIntentoAt <= clock.UtcNow.AddMinutes(-config.GetValue<int>("IntegracionesAw:Exportacion:AcuseVencidoMinutos", 1440)))).ToArrayAsync(cancellationToken), x.MotivoVinculacion);

    private async Task BloquearAsync(string clave, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({clave}, 0))", cancellationToken);
    private async Task BloquearPedidosAsync(OtrEnvio envio, CancellationToken cancellationToken)
    {
        var origen = await pedidos.BuscarPorNumeroAsync(envio.EmpresaId, envio.Lineas.Select(l => l.NumeroPedido).Distinct().ToArray(), cancellationToken);
        foreach (var id in origen.Select(p => p.Id).Distinct().Order()) await BloquearAsync("millet:ce:pedido:" + id, cancellationToken);
    }
    public async Task<bool> Handle(AplicarEnvioOtr request, CancellationToken cancellationToken)
    {
        // Puerto de aplicación exclusivo del worker. No se publica como endpoint HTTP.
        if (empresa.Current != request.EmpresaId && !empresa.IsBypassed) throw new UnauthorizedAccessException();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await BloquearAsync($"millet:ce:otr:{request.EmpresaId}:{request.Entrada.IdOrigen}", cancellationToken);
        var envio = await Envios.SingleOrDefaultAsync(x => x.EmpresaId == request.EmpresaId && x.IdOrigen == request.Entrada.IdOrigen, cancellationToken);
        if (envio is null) { envio = new(request.EmpresaId, request.Entrada.IdOrigen); db.OtrEnvios.Add(envio); }
        if (!envio.Aplicar(request.Entrada.Version, request.Entrada.Contenido)) { await tx.CommitAsync(cancellationToken); return false; }
        await BloquearPedidosAsync(envio, cancellationToken);
        if (envio.ExpedienteId is Guid id)
        {
            var e = await Expedientes.SingleAsync(x => x.Id == id && x.EmpresaId == request.EmpresaId, cancellationToken);
            e.RevisarPartidasOtr(e.Partidas.Where(p => p.ContenedorId == envio.ContenedorId).Select(p => p.Id));
            if (envio.Estado != EstadoOtrEnvio.Cancelado)
            { e.ExigirAbierto(); await ProponerAsync(envio, e, e.ResponsableUsuarioId, cancellationToken); }
        }
        else await EvaluarSinVinculoAsync(envio, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return true;
    }
    private async Task EvaluarSinVinculoAsync(OtrEnvio envio, CancellationToken cancellationToken)
    {
        var origen = await pedidos.BuscarPorNumeroAsync(envio.EmpresaId, envio.Lineas.Where(l => l.VersionOrigen == envio.VersionAplicada).Select(l => l.NumeroPedido).Distinct().ToArray(), cancellationToken);
        foreach (var l in envio.Lineas.Where(l => l.VersionOrigen == envio.VersionAplicada))
            l.Pendiente(envio.MotivoVinculacion ?? (origen.Count(p => p.NumeroPedido == l.NumeroPedido) > 1 ? "CE_OTR_PEDIDO_AMBIGUO: hay más de un pedido con ese número A+W." : origen.Any(p => p.NumeroPedido == l.NumeroPedido) ? "CE_OTR_POR_VINCULAR: vincule un expediente para validar K3–K5." : "CE_OTR_PEDIDO_INEXISTENTE: no existe el pedido A+W en el ERP."));
    }
    private async Task ProponerAsync(OtrEnvio envio, Expediente e, Guid actor, CancellationToken cancellationToken)
    {
        var actual = e.Contenedores.SingleOrDefault(c => c.Id == envio.ContenedorId);
        e.GuardarContenedor(envio.ContenedorId!.Value, envio.Conciliar(actual, clock.UtcNow));
        var origen = await pedidos.BuscarPorNumeroAsync(envio.EmpresaId, envio.Lineas.Select(l => l.NumeroPedido).Distinct().ToArray(), cancellationToken);
        foreach (var l in envio.Lineas.Where(l => l.VersionOrigen == envio.VersionAplicada && l.PartidaId is null))
        {
            var coincidencias = origen.Where(p => p.NumeroPedido == l.NumeroPedido).ToArray();
            if (coincidencias.Length > 1) { l.Pendiente("CE_OTR_PEDIDO_AMBIGUO: hay más de un pedido con ese número A+W."); continue; }
            var p = coincidencias.SingleOrDefault();
            if (p is null) { l.Pendiente("CE_OTR_PEDIDO_INEXISTENTE: no existe el pedido A+W en el ERP."); continue; }
            if (await db.Expedientes.AnyAsync(x => x.Id != e.Id && x.Estado == EstadoExpediente.Abierto && x.VinculosPedidos.Any(v => v.Activo && v.PedidoFacturableId == p.Id), cancellationToken))
            { l.Pendiente("CE_PEDIDO_CON_EXPEDIENTE: el pedido ya está en otro expediente abierto."); continue; }
            if (envio.ConflictoPartida(l, ValorAsignado(e, envio, p.Id, l), clock.UtcNow)) continue;
            // Una nueva versión conserva asignaciones confirmadas y las pone en revisión; no duplica cantidades.
            if (e.Partidas.Any(x => x.ContenedorId == envio.ContenedorId && x.PedidoFacturableId == p.Id && x.Posicion == l.Posicion && x.Estado != EstadoPartida.Liberada))
            { l.Pendiente("CE_OTR_PARTIDA_EN_REVISION: revise la asignación anterior antes de proponer la nueva cantidad."); continue; }
            try
            {
                if (p.Lineas.SingleOrDefault(x => x.Posicion == l.Posicion)?.Unidad != l.Unidad)
                    throw new BusinessRuleException("CE_OTR_UNIDAD_DISTINTA", "La unidad no coincide con el pedido del ERP.");
                e.AsignarPartida(envio.ContenedorId.Value, p, l.Posicion, l.Cantidad, actor, propuesta: true);
                var partida = e.Partidas.Last(); l.Proponer(partida.Id);
            }
            catch (BusinessRuleException ex) { l.Pendiente(ex.Code + ": " + ex.Message); }
        }
    }
    private static string ValorAsignado(Expediente e, OtrEnvio envio, Guid pedido, OtrLinea linea)
    {
        var partidas = e.Partidas.Where(p => p.ContenedorId == envio.ContenedorId && p.PedidoFacturableId == pedido && p.Posicion == linea.Posicion && p.Estado != EstadoPartida.Liberada).ToArray();
        return OtrEnvio.ValorPartida(partidas.Sum(p => p.Cantidad), string.Join("/", partidas.Select(p => p.Unidad).Distinct().Order().DefaultIfEmpty(linea.Unidad)));
    }
    private async Task<OtrEnvio> GestionarAsync(Guid id, int version, CancellationToken cancellationToken)
    {
        await seguridad.PermisoAsync(PermisosCanonicos.ComercioExteriorOtrGestionar, cancellationToken);
        await BloquearAsync("millet:ce:otr:id:" + id, cancellationToken);
        var referencia = await db.OtrEnvios.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.EmpresaId, x.IdOrigen }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("CE_OTR_NO_ENCONTRADO", "No existe el envío OTR.");
        await BloquearAsync($"millet:ce:otr:{referencia.EmpresaId}:{referencia.IdOrigen}", cancellationToken);
        var envio = await Envios.SingleAsync(x => x.Id == id, cancellationToken);
        await seguridad.SucursalAsync(envio.SucursalId, true, cancellationToken);
        if (envio.Version != version) throw new ConcurrencyException("OtrEnvio", id);
        return envio;
    }
    public async Task<OtrDetalle> Handle(VincularEnvioOtr request, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var envio = await GestionarAsync(request.Id, request.Version, cancellationToken);
        await BloquearPedidosAsync(envio, cancellationToken);
        var e = await seguridad.CargarAsync(request.ExpedienteId, true, cancellationToken);
        envio.Vincular(e); await ProponerAsync(envio, e, seguridad.Actor, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return await DetalleAsync(envio, cancellationToken);
    }
    public async Task<OtrDetalle> Handle(AbrirExpedienteDesdeEnvioOtr request, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var envio = await GestionarAsync(request.Id, request.Version, cancellationToken);
        envio.ExigirIdentidad();
        if (envio.ExpedienteId != null) throw new BusinessRuleException("CE_OTR_YA_VINCULADO", "El envío ya tiene expediente.");
        await seguridad.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesCrear, cancellationToken);
        await catalogos.ValidarParticipanteAsync(null, envio.ClienteId, cancellationToken);
        var incoterm = await catalogos.IncotermDdpAsync(cancellationToken);
        await catalogos.ValidarAsync(incoterm, request.Moneda, envio.SucursalId, request.ResponsableUsuarioId, cancellationToken);
        await BloquearPedidosAsync(envio, cancellationToken);
        var numero = await db.Database.SqlQueryRaw<long>("SELECT nextval('comercio_exterior.folio_seq') AS \"Value\"").SingleAsync(cancellationToken);
        var e = new Expediente(Guid.CreateVersion7(), envio.EmpresaId, string.Create(CultureInfo.InvariantCulture, $"CE-EXP-{clock.HoyLocal().Year}-{numero:D6}"), TipoExpediente.Exportacion, envio.SucursalId, "OTR " + envio.IdOrigen, null, incoterm, request.Moneda, request.ResponsableUsuarioId, [envio.Contenido().Cliente]);
        var cliente = e.Participantes.Single();
        e.EditarParticipante(cliente.Id, cliente.Nombre, TipoParticipante.Cliente, null, PapelParticipante.Captura, null, envio.ClienteId);
        e.InicializarHitos(clock.HoyLocal()); db.Expedientes.Add(e);
        envio.Vincular(e); await ProponerAsync(envio, e, seguridad.Actor, cancellationToken);
        await eventos.PublishAsync(new ExpedienteCeAbiertoEvent(e.EmpresaId, clock.UtcNow, e.Id, e.Folio), cancellationToken);
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return await DetalleAsync(envio, cancellationToken);
    }
    public async Task<OtrDetalle> Handle(ResolverConflictoOtr request, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var envio = await GestionarAsync(request.Id, request.Version, cancellationToken);
        if (envio.Estado != EstadoOtrEnvio.Vinculado) throw new BusinessRuleException("CE_OTR_NO_VINCULADO", "Vincule un envío activo antes de resolver conflictos.");
        await BloquearPedidosAsync(envio, cancellationToken);
        var e = await seguridad.CargarAsync(envio.ExpedienteId!.Value, true, cancellationToken); e.ExigirAbierto();
        var conflicto = envio.Conflicto(request.ConflictoId);
        if (conflicto.Campo.StartsWith("Partida:", StringComparison.Ordinal))
        {
            var linea = envio.Lineas.Single(l => "Partida:" + l.Id == conflicto.Campo);
            var origen = await pedidos.BuscarPorNumeroAsync(envio.EmpresaId, [linea.NumeroPedido], cancellationToken);
            if (origen.Count > 1) throw new BusinessRuleException("CE_OTR_PEDIDO_AMBIGUO", "Hay más de un pedido con ese número A+W.");
            var pedido = origen.SingleOrDefault() ?? throw new BusinessRuleException("CE_OTR_PEDIDO_INEXISTENTE", "No existe el pedido A+W.");
            envio.ResolverValor(conflicto.Id, request.UsarOtr, request.Motivo, seguridad.Actor, clock.UtcNow, ValorAsignado(e, envio, pedido.Id, linea), OtrEnvio.ClavePartida(linea.NumeroPedido, linea.Posicion));
            if (request.UsarOtr)
            {
                if (pedido.Lineas.SingleOrDefault(l => l.Posicion == linea.Posicion)?.Unidad != linea.Unidad)
                    throw new BusinessRuleException("CE_OTR_UNIDAD_DISTINTA", "La unidad no coincide con el pedido.");
                foreach (var anterior in e.Partidas.Where(p => p.ContenedorId == envio.ContenedorId && p.PedidoFacturableId == pedido.Id && p.Posicion == linea.Posicion && p.Estado != EstadoPartida.Liberada).ToArray())
                { e.QuitarPartida(anterior.Id, pedido.FacturaVigente); db.Partidas.Remove(anterior); }
                if (await db.Expedientes.AnyAsync(x => x.Id != e.Id && x.Estado == EstadoExpediente.Abierto && x.VinculosPedidos.Any(v => v.Activo && v.PedidoFacturableId == pedido.Id), cancellationToken))
                    throw new BusinessRuleException("CE_PEDIDO_CON_EXPEDIENTE", "El pedido ya está en otro expediente abierto.");
                e.AsignarPartida(envio.ContenedorId!.Value, pedido, linea.Posicion, linea.Cantidad, seguridad.Actor, propuesta: true);
                var partida = e.Partidas.Last(); linea.Proponer(partida.Id);
            }
            else linea.Pendiente("CE_OTR_CONSERVAR_ERP: " + request.Motivo);
        }
        else e.GuardarContenedor(envio.ContenedorId!.Value, envio.Resolver(request.ConflictoId, request.UsarOtr, request.Motivo, seguridad.Actor, clock.UtcNow, e.Contenedor(envio.ContenedorId.Value)));
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return await DetalleAsync(envio, cancellationToken);
    }
    public async Task<OtrDetalle> Handle(ReintentarSalidaOtr request, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var envio = await GestionarAsync(request.Id, request.Version, cancellationToken);
        var salida = await db.OtrSalidas.SingleOrDefaultAsync(x => x.Id == request.SalidaId && x.OtrEnvioId == envio.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CE_OTR_SALIDA_NO_ENCONTRADA", "No existe la salida.");
        db.OtrSalidas.Add(salida.Reintentar()); envio.RegistrarCambio(); await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return await DetalleAsync(envio, cancellationToken);
    }
}
