using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

#pragma warning disable CA1304, CA1311, CA1862
namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>
/// Historial de un cliente en A+W: pedidos (<c>BW_AUFTR_KOPF</c>) y cotizaciones (<c>BW_ANGEB_KOPF</c>), total y fecha de la última.
/// </summary>
public sealed record AwPedidosCliente(int Pedidos, DateTime? Ultimo, int Cotizaciones = 0, DateTime? UltimaCotizacion = null);

/// <summary>
/// Puerto de solo lectura: historial de pedidos en A+W por referencia de cliente (<c>AH_IDENT</c>).
/// Lo implementa <c>Integraciones.Aw</c>; el origen simulado/demo devuelve vacío. Jamás escribe en A+W.
/// </summary>
public interface IAwClientesPedidosOrigen
{
    Task<IReadOnlyDictionary<string, AwPedidosCliente>> ContarAsync(IReadOnlyCollection<string> referencias, CancellationToken ct);
}

/// <summary>Puerto de lectura: pedidos facturables del ERP por cliente (lo implementa Facturación).</summary>
public interface IPedidosErpPorClienteReadPort
{
    Task<IReadOnlyDictionary<Guid, int>> ContarAsync(IReadOnlyCollection<Guid> clienteIds, CancellationToken ct);
}

public sealed record RfcRepetidoClienteDto(
    Guid ClienteId, string? ReferenciaAw, string RazonSocial, bool TieneRfc, string Estatus,
    Guid? UnificadoEnClienteId, int PedidosAw, DateTime? UltimoPedidoAw, int PedidosErp, string AccionSugerida,
    int CotizacionesAw = 0, DateTime? UltimaCotizacionAw = null);

public sealed record RfcRepetidoGrupoDto(
    string Rfc, Guid PrincipalSugeridoId, string MotivoSugerencia, bool Resuelto,
    IReadOnlyList<RfcRepetidoClienteDto> Clientes);

public sealed record ListarRfcRepetidosResponse(int Total, int Offset, int Limit, IReadOnlyList<RfcRepetidoGrupoDto> Items);

/// <summary>Miembro de un grupo con sus conteos; insumo de las reglas puras.</summary>
public sealed record MiembroRfcRepetido(
    Guid Id, string? ReferenciaAw, bool TieneRfc, bool Unificado, int PedidosAw, DateTime? UltimoPedidoAw, int PedidosErp, int CotizacionesAw = 0, DateTime? UltimaCotizacionAw = null);

/// <summary>Reglas de propuesta (pendientes de validar con Millet): principal sugerido y acción por secundario.</summary>
public static class RfcRepetidosReglas
{
    public static (Guid Id, string Motivo) SugerirPrincipal(IReadOnlyList<MiembroRfcRepetido> miembros)
    {
        var c = miembros.Where(m => !m.Unificado).ToList();
        if (c.Count == 0) throw new InvalidOperationException("El grupo no tiene clientes sin unificar.");
        if (c.Count == 1) return (c[0].Id, "Es el cliente en el que ya se unificaron los demás.");
        if (c.FirstOrDefault(m => m.TieneRfc) is { } conRfc) return (conRfc.Id, "Ya tiene el RFC en el ERP.");
        if (c.Any(m => UltimaActividadAw(m) is not null))
            return (c.OrderByDescending(UltimaActividadAw).ThenByDescending(m => m.PedidosErp).ThenBy(Orden).First().Id,
                "Tiene la actividad más reciente en A+W (pedido o cotización).");
        if (c.Any(m => m.PedidosErp > 0))
            return (c.OrderByDescending(m => m.PedidosErp).ThenBy(Orden).First().Id, "Tiene más pedidos en el ERP.");
        return (c.OrderBy(Orden).First().Id, "Es la referencia más baja de A+W.");
    }

    private static DateTime? UltimaActividadAw(MiembroRfcRepetido m) =>
        m.UltimoPedidoAw is { } p && m.UltimaCotizacionAw is { } q ? (p > q ? p : q) : m.UltimoPedidoAw ?? m.UltimaCotizacionAw;

    /// <summary>Fusionar solo sin pedidos ni cotizaciones en A+W ni pedidos en el ERP; con historial se Agrupa.</summary>
    public static bool ConHistorial(MiembroRfcRepetido m) => m.PedidosAw > 0 || m.CotizacionesAw > 0 || m.PedidosErp > 0;

    public static string AccionSugerida(MiembroRfcRepetido m, Guid principalId) =>
        m.Id == principalId ? "Principal" : ConHistorial(m) ? "Agrupar" : "Fusionar";

    // Referencia numérica ascendente; sin referencia (cliente manual) al final.
    private static (int, long, string) Orden(MiembroRfcRepetido m) =>
        m.ReferenciaAw is null ? (1, 0, "")
        : long.TryParse(m.ReferenciaAw, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? (0, n, "")
        : (0, long.MaxValue, m.ReferenciaAw);
}

/// <summary>Agrupación y armado de grupos compartidos por la consulta y el comando.</summary>
internal static class RfcRepetidosGrupos
{
    /// <summary>
    /// Clave de ligado normalizada → clientes. Une <c>clientes.rfc</c> y el UST_ID candidato de A+W; sin genéricos ni
    /// formas inválidas; el principal de un unificado se mantiene en el grupo; solo grupos con 2+.
    /// ponytail: lee (id, rfc) de todos los clientes en memoria; con cientos de miles usar SQL agrupado.
    /// </summary>
    public static async Task<SortedDictionary<string, HashSet<Guid>>> CargarAsync(CompartidoDbContext db, CancellationToken ct)
    {
        var porRfc = await db.Clientes.AsNoTracking().Where(c => c.Rfc != null).Select(c => new { c.Id, Valor = c.Rfc }).ToListAsync(ct);
        var porUst = await db.Set<ClienteSincronizacionAw>().AsNoTracking().Where(r => r.CandidatoFiscalUstId != null)
            .Select(r => new { Id = r.ClienteId, Valor = r.CandidatoFiscalUstId }).ToListAsync(ct);
        var unificados = await db.Clientes.AsNoTracking().Where(c => c.UnificadoEnClienteId != null)
            .Select(c => new { c.Id, Principal = c.UnificadoEnClienteId!.Value }).ToListAsync(ct);

        var grupos = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        foreach (var x in porRfc.Concat(porUst))
            if (RfcCliente.ClaveLigado(x.Valor) is { } k)
                (grupos.TryGetValue(k, out var s) ? s : grupos[k] = []).Add(x.Id);
        foreach (var u in unificados)
            foreach (var g in grupos.Values.Where(g => g.Contains(u.Id)).ToList()) g.Add(u.Principal);

        return new(grupos.Where(g => g.Value.Count >= 2).ToDictionary(g => g.Key, g => g.Value), StringComparer.Ordinal);
    }

    /// <summary>Pendiente = más de un cliente sin unificar (resuelto = a lo más uno).</summary>
    public static async Task<SortedDictionary<string, HashSet<Guid>>> SoloPendientesAsync(
        CompartidoDbContext db, SortedDictionary<string, HashSet<Guid>> grupos, CancellationToken ct)
    {
        var unificados = (await db.Clientes.AsNoTracking().Where(c => c.UnificadoEnClienteId != null).Select(c => c.Id).ToListAsync(ct)).ToHashSet();
        return new(grupos.Where(g => g.Value.Count(id => !unificados.Contains(id)) > 1).ToDictionary(g => g.Key, g => g.Value), StringComparer.Ordinal);
    }

    public static async Task<List<RfcRepetidoGrupoDto>> ConstruirAsync(
        CompartidoDbContext db, IAwClientesPedidosOrigen aw, IPedidosErpPorClienteReadPort erp,
        IReadOnlyList<(string Rfc, HashSet<Guid> Ids)> grupos, CancellationToken ct)
    {
        if (grupos.Count == 0) return [];
        var ids = grupos.SelectMany(g => g.Ids).ToHashSet();
        var clientes = await db.Clientes.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.ReferenciaExterna, c.RazonSocial, c.Rfc, c.Estatus, c.UnificadoEnClienteId, c.UnificacionConHistorial }).ToListAsync(ct);
        var porId = clientes.ToDictionary(c => c.Id);
        var refs = clientes.Where(c => c.ReferenciaExterna is not null).Select(c => c.ReferenciaExterna!).ToArray();
        var pedidosAw = refs.Length == 0 ? new Dictionary<string, AwPedidosCliente>() : await aw.ContarAsync(refs, ct);
        var pedidosErp = await erp.ContarAsync(ids, ct);

        return grupos.Select(g =>
        {
            var miembros = g.Ids.Select(id =>
            {
                var c = porId[id];
                var a = c.ReferenciaExterna is not null && pedidosAw.TryGetValue(c.ReferenciaExterna, out var v) ? v : null;
                return new MiembroRfcRepetido(id, c.ReferenciaExterna, RfcCliente.Normalizar(c.Rfc) == g.Rfc,
                    c.UnificadoEnClienteId is not null, a?.Pedidos ?? 0, a?.Ultimo, pedidosErp.GetValueOrDefault(id), a?.Cotizaciones ?? 0, a?.UltimaCotizacion);
            }).ToList();
            var (principal, motivo) = RfcRepetidosReglas.SugerirPrincipal(miembros);
            // Grupo ya unificado: el principal es el elegido, no el que sugeriría la regla hoy.
            if (miembros.Select(m => porId[m.Id].UnificadoEnClienteId).FirstOrDefault(x => x is not null) is { } elegido
                && porId.ContainsKey(elegido))
                (principal, motivo) = (elegido, "Principal elegido al unificar.");
            var dtos = miembros
                .OrderBy(m => m.Id == principal ? 0 : 1).ThenBy(m => m.ReferenciaAw is not null && long.TryParse(m.ReferenciaAw, out var n) ? n : long.MaxValue)
                .Select(m =>
                {
                    var c = porId[m.Id];
                    // Un ya unificado conserva la acción con la que se unificó (Fusionado = Inactivo, Agrupado = Activo).
                    var accion = m.Unificado ? (c.UnificacionConHistorial ? "Agrupar" : "Fusionar")
                        : RfcRepetidosReglas.AccionSugerida(m, principal);
                    return new RfcRepetidoClienteDto(m.Id, c.ReferenciaExterna, c.RazonSocial, m.TieneRfc, c.Estatus.ToString(),
                        c.UnificadoEnClienteId, m.PedidosAw, m.UltimoPedidoAw, m.PedidosErp, accion, m.CotizacionesAw, m.UltimaCotizacionAw);
                }).ToList();
            return new RfcRepetidoGrupoDto(g.Rfc, principal, motivo, miembros.Count(m => !m.Unificado) == 1, dtos);
        }).ToList();
    }
}

/// <summary>Grupos de clientes que comparten RFC; <c>SoloPendientes</c> oculta los ya resueltos.</summary>
public sealed record ListarRfcRepetidosQuery(int Offset = 0, int Limit = 25, bool SoloPendientes = true)
    : IRequest<ListarRfcRepetidosResponse>;

public sealed class ListarRfcRepetidosHandler(
    CompartidoDbContext db, IAwClientesPedidosOrigen aw, IPedidosErpPorClienteReadPort erp)
    : IRequestHandler<ListarRfcRepetidosQuery, ListarRfcRepetidosResponse>
{
    public async Task<ListarRfcRepetidosResponse> Handle(ListarRfcRepetidosQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var off = Math.Max(0, request.Offset);
        var lim = Math.Clamp(request.Limit, 1, 100);
        var grupos = await RfcRepetidosGrupos.CargarAsync(db, ct);
        if (request.SoloPendientes) grupos = await RfcRepetidosGrupos.SoloPendientesAsync(db, grupos, ct);
        var pagina = grupos.Skip(off).Take(lim).Select(g => (g.Key, g.Value)).ToList();
        return new(grupos.Count, off, lim, await RfcRepetidosGrupos.ConstruirAsync(db, aw, erp, pagina, ct));
    }
}

/// <summary>Un grupo por RFC (misma forma que la lista); 404 si no hay 2+ clientes o el RFC es genérico/inválido.</summary>
public sealed record ObtenerRfcRepetidoQuery(string Rfc) : IRequest<RfcRepetidoGrupoDto>;

public sealed class ObtenerRfcRepetidoHandler(
    CompartidoDbContext db, IAwClientesPedidosOrigen aw, IPedidosErpPorClienteReadPort erp)
    : IRequestHandler<ObtenerRfcRepetidoQuery, RfcRepetidoGrupoDto>
{
    public async Task<RfcRepetidoGrupoDto> Handle(ObtenerRfcRepetidoQuery request, CancellationToken cancellationToken)
    {
        var rfc = RfcCliente.ClaveLigado(request.Rfc);
        var ids = rfc is null ? null : (await RfcRepetidosGrupos.CargarAsync(db, cancellationToken)).GetValueOrDefault(rfc);
        if (ids is null) throw new EntityNotFoundException("CLIENTE_UNIFICAR_GRUPO_NO_ENCONTRADO", "No hay clientes repetidos con ese RFC.");
        return (await RfcRepetidosGrupos.ConstruirAsync(db, aw, erp, [(rfc!, ids)], cancellationToken))[0];
    }
}

/// <summary>
/// Unificación automática (acuerdo Millet 27-oct): los grupos donde NINGÚN cliente sin unificar tiene historial
/// (pedidos o cotizaciones en A+W, pedidos en el ERP) se unifican en el principal sugerido, con las mismas reglas del
/// comando manual. Los grupos con historial se dejan pendientes para que una persona elija. Idempotente. Devuelve cuántos grupos unificó.
/// </summary>
public sealed record UnificarRfcRepetidosAutomaticoCommand(string Actor) : IRequest<int>;

public sealed class UnificarRfcRepetidosAutomaticoHandler(
    CompartidoDbContext db, IAwClientesPedidosOrigen aw, IPedidosErpPorClienteReadPort erp, ISender sender)
    : IRequestHandler<UnificarRfcRepetidosAutomaticoCommand, int>
{
    public const string Motivo = "Unificación automática: sin pedidos ni cotizaciones";

    public async Task<int> Handle(UnificarRfcRepetidosAutomaticoCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var pendientes = await RfcRepetidosGrupos.SoloPendientesAsync(db, await RfcRepetidosGrupos.CargarAsync(db, ct), ct);
        var grupos = await RfcRepetidosGrupos.ConstruirAsync(db, aw, erp, pendientes.Select(g => (g.Key, g.Value)).ToList(), ct);
        var unificados = 0;
        foreach (var g in grupos.Where(g => g.Clientes.Where(c => c.UnificadoEnClienteId is null)
                     .All(c => c.PedidosAw == 0 && c.CotizacionesAw == 0 && c.PedidosErp == 0)))
        {
            try
            {
                await sender.Send(new UnificarRfcRepetidoCommand(g.Rfc, g.PrincipalSugeridoId, Motivo, request.Actor, Automatica: true), ct);
                unificados++;
            }
            catch (BusinessRuleException) { } // el grupo no cumple una regla del comando: queda pendiente para una persona
        }
        return unificados;
    }
}

/// <summary>Unifica todos los demás clientes del grupo del RFC en el principal elegido.</summary>
/// <summary><c>Automatica</c>: lo dispara la sincronización (sin usuario); los conflictos quedan resueltos por <see cref="CambioMaestroAw.UsuarioSistemaId"/>.</summary>
public sealed record UnificarRfcRepetidoCommand(string Rfc, Guid PrincipalClienteId, string Motivo, string Actor, bool Automatica = false)
    : IRequest<RfcRepetidoGrupoDto>;

public sealed class UnificarRfcRepetidoHandler(
    CompartidoDbContext db, IAwClientesPedidosOrigen aw, IPedidosErpPorClienteReadPort erp, ICurrentUserContext user)
    : IRequestHandler<UnificarRfcRepetidoCommand, RfcRepetidoGrupoDto>
{
    public async Task<RfcRepetidoGrupoDto> Handle(UnificarRfcRepetidoCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rfc = RfcCliente.ClaveLigado(request.Rfc)
            ?? throw new BusinessRuleException("CLIENTE_UNIFICAR_RFC_INVALIDO", "El RFC no es válido o es genérico; no se agrupa.");
        var motivo = request.Motivo?.Trim() ?? string.Empty;
        if (motivo.Length is < 5 or > 300)
            throw new BusinessRuleException("CLIENTE_UNIFICAR_MOTIVO_INVALIDO", "El motivo debe tener entre 5 y 300 caracteres.");

        await using var scope = await ClienteRfcScope.AbrirAsync(db, rfc, ct);
        var ids = (await RfcRepetidosGrupos.CargarAsync(db, ct)).GetValueOrDefault(rfc)
            ?? throw new EntityNotFoundException("CLIENTE_UNIFICAR_GRUPO_NO_ENCONTRADO", "No hay clientes repetidos con ese RFC.");
        if (!ids.Contains(request.PrincipalClienteId))
            throw new BusinessRuleException("CLIENTE_UNIFICAR_PRINCIPAL_FUERA_DE_GRUPO", "El cliente principal no pertenece al grupo de ese RFC.");

        var clientes = await db.Clientes.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var principal = clientes.Single(c => c.Id == request.PrincipalClienteId);
        if (principal.UnificadoEnClienteId is not null)
            throw new BusinessRuleException("CLIENTE_PRINCIPAL_UNIFICADO", "El cliente principal ya está unificado en otro cliente.");
        var secundarios = clientes.Where(c => c.Id != principal.Id && c.UnificadoEnClienteId is null).ToList();

        // Evita cadenas: el resolver sigue un solo salto, así que un cliente que ya es destino de otros no pasa a secundario.
        var secIds = secundarios.Select(s => s.Id).ToList();
        if (await db.Clientes.AnyAsync(c => c.UnificadoEnClienteId != null && secIds.Contains(c.UnificadoEnClienteId.Value), ct))
            throw new BusinessRuleException("CLIENTE_SECUNDARIO_TIENE_UNIFICADOS", "Uno de los clientes a unificar ya es principal de otros; elige ese como principal.");

        // El servidor decide Fusionar/Agrupar con el historial real; el cliente no puede forzarlo.
        var refs = secundarios.Where(s => s.ReferenciaExterna is not null).Select(s => s.ReferenciaExterna!).ToArray();
        var pedidosAw = refs.Length == 0 ? new Dictionary<string, AwPedidosCliente>() : await aw.ContarAsync(refs, ct);
        var pedidosErp = await erp.ContarAsync(secIds, ct);
        var ahora = DateTimeOffset.UtcNow;
        foreach (var s in secundarios)
        {
            var conHistorial = (s.ReferenciaExterna is not null && pedidosAw.TryGetValue(s.ReferenciaExterna, out var a) && (a.Pedidos > 0 || a.Cotizaciones > 0))
                || pedidosErp.GetValueOrDefault(s.Id) > 0;
            s.UnificarEn(principal, conHistorial, motivo, request.Actor, ahora);
        }
        await CompletarPrincipalAsync(principal, clientes, rfc, ct);
        // Unificar resuelve los conflictos RfcCompartido pendientes de los clientes del grupo para esta clave.
        var refsGrupo = clientes.Where(c => c.ReferenciaExterna is not null).Select(c => c.ReferenciaExterna!).ToList();
        var conflictos = await db.CambiosMaestrosAw.Where(c => c.TipoEntidad == "Cliente" && c.Tipo == TipoCambioMaestroAw.RfcCompartido
            && c.Estado == EstadoCambioMaestroAw.Pendiente && c.ValorDespues == rfc && refsGrupo.Contains(c.ReferenciaExterna)).ToListAsync(ct);
        foreach (var c in conflictos) c.Resolver("Unificado", user.UserId ?? (request.Automatica ? CambioMaestroAw.UsuarioSistemaId : throw new ForbiddenException("USUARIO_REQUERIDO", "Inicia sesión para unificar.")), ahora);
        await db.SaveChangesAsync(ct);
        await scope.ConfirmarAsync(ct);

        return (await RfcRepetidosGrupos.ConstruirAsync(db, aw, erp, [(rfc, ids)], ct))[0];
    }

    /// <summary>
    /// El principal queda completo (decisión del owner 22-oct): recibe el RFC del grupo y los datos fiscales y de
    /// contacto que le falten, tomados de los demás clientes del grupo. Si otro cliente tenía el RFC, se le quita
    /// primero (índice único de RFC); su historial ya emitido conserva el RFC en sus CFDI.
    /// </summary>
    private async Task CompletarPrincipalAsync(Cliente principal, List<Cliente> grupo, string rfc, CancellationToken ct)
    {
        var otros = grupo.Where(c => c.Id != principal.Id).ToList();
        if (principal.Rfc is null)
        {
            foreach (var o in otros.Where(o => o.Rfc is not null && RfcCliente.ClaveLigado(o.Rfc) == rfc))
                o.ActualizarDatos(limpiarRfc: true);
            await db.SaveChangesAsync(ct);
            principal.ActualizarDatos(rfc: rfc);
        }
        string? De(Func<Cliente, string?> campo) => otros.Select(campo).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        principal.ActualizarDatos(
            regimenFiscal: principal.RegimenFiscal is null ? De(c => c.RegimenFiscal) : null,
            codigoPostalFiscal: principal.CodigoPostalFiscal is null ? De(c => c.CodigoPostalFiscal) : null,
            usoCfdiDefault: principal.UsoCfdiDefault is null ? De(c => c.UsoCfdiDefault) : null,
            formaPagoDefault: principal.FormaPagoDefault is null ? De(c => c.FormaPagoDefault) : null,
            metodoPagoDefault: principal.MetodoPagoDefault is null ? De(c => c.MetodoPagoDefault) : null,
            email: principal.Email is null ? De(c => c.Email) : null,
            telefono: principal.Telefono is null ? De(c => c.Telefono) : null);
    }
}
