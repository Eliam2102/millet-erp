using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Application.Salidas;

/// <summary>M-245: pide la salida de las piezas indicadas de un pedido que no tiene el 100 % en PT.</summary>
public sealed record SolicitarSalidaParcialCommand(string NumeroPedido, IReadOnlyList<string> Etiquetas, string Motivo) : IRequest<SolicitudParcialDto>;

/// <summary>M-245: autoriza (<c>Nivel</c> credito, o ventas como suplente con <c>Suplencia</c>) o rechaza. Otra persona distinta de quien pidió.</summary>
public sealed record ResolverSalidaParcialCommand(Guid Id, bool Autorizar, string? Nivel, string? Suplencia, string Motivo) : IRequest<SolicitudParcialDto>;

/// <summary>Bandeja de solicitudes de salida parcial (por estado y pedido).</summary>
public sealed record ListarSolicitudesParcialesQuery(string? Estado, string? Pedido, int Page, int PageSize) : IRequest<SolicitudesParcialesPagina>;

public sealed record SolicitudParcialDto(Guid Id, string NumeroPedido, Guid SucursalId, IReadOnlyList<string> Etiquetas, int Esperadas, int EnPt, string Motivo, string Estado,
    Guid SolicitadaPorId, DateTimeOffset SolicitadaAt, string? Nivel, string? Suplencia, string? MotivoResolucion, Guid? ResueltaPorId, DateTimeOffset? ResueltaAt);

public sealed record SolicitudesParcialesPagina(IReadOnlyList<SolicitudParcialDto> Items, int Total);

internal static class ParcialMapeo
{
    public static async Task<List<SolicitudParcialDto>> ADtoAsync(ProductoTerminadoDbContext db, IReadOnlyList<SolicitudSalidaParcialPt> solicitudes, CancellationToken ct)
    {
        var pedidoIds = solicitudes.Select(s => s.PedidoPtId).Distinct().ToList();
        var pedidos = await db.PedidosPt.AsNoTracking().Include(p => p.Posiciones).Where(p => pedidoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var piezaIds = solicitudes.SelectMany(s => s.PiezaIds).Distinct().ToList();
        var etiquetas = await db.Piezas.AsNoTracking().Where(p => piezaIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Etiqueta, ct);
        var enPt = await db.Piezas.AsNoTracking().Where(p => pedidoIds.Contains(p.PedidoPtId) && p.Estado != EstadoPieza.Baja)
            .GroupBy(p => p.PedidoPtId).Select(g => new { g.Key, N = g.Count(), Sucursal = g.Max(p => p.SucursalId) }).ToDictionaryAsync(x => x.Key, ct);
        return [.. solicitudes.Select(s =>
        {
            var p = pedidos[s.PedidoPtId];
            var c = enPt.GetValueOrDefault(s.PedidoPtId);
            return new SolicitudParcialDto(s.Id, p.NumeroPedido, c?.Sucursal ?? p.SucursalVentaId, [.. s.PiezaIds.Select(id => etiquetas.GetValueOrDefault(id) ?? "")],
                p.Posiciones.Sum(x => x.PiezasEsperadas), c?.N ?? 0, s.Motivo, s.Estado.ToString(), s.SolicitadaPorId, s.SolicitadaAt, s.Nivel, s.Suplencia,
                s.MotivoResolucion, s.ResueltaPorId, s.ResueltaAt);
        })];
    }
}

public sealed class SolicitarSalidaParcialHandler(ProductoTerminadoDbContext db, PtAcceso acceso, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<SolicitarSalidaParcialCommand, SolicitudParcialDto>
{
    public async Task<SolicitudParcialDto> Handle(SolicitarSalidaParcialCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();
        var numero = c.NumeroPedido.Trim();
        var pedido = await db.PedidosPt.Include(p => p.Posiciones).FirstOrDefaultAsync(p => p.NumeroPedido == numero, cancellationToken)
            ?? throw new EntityNotFoundException("PT_PEDIDO_NO_ENCONTRADO", $"El pedido {numero} no tiene piezas en PT.");
        if (await SalidaParcial.CompletoAsync(db, pedido, cancellationToken))
            throw new BusinessRuleException("PT_PARCIAL_PEDIDO_COMPLETO", "El pedido ya tiene todas sus piezas en PT: si sale incompleto, la autorización se pide en la hoja de salida.");
        if (await db.SolicitudesParciales.AnyAsync(s => s.PedidoPtId == pedido.Id && s.Estado == EstadoSolicitudPt.Pendiente, cancellationToken))
            throw new BusinessRuleException("PT_PARCIAL_PENDIENTE", $"El pedido {numero} ya tiene una solicitud de salida parcial pendiente.");

        var etiquetas = c.Etiquetas.Select(e => e.Trim()).Distinct().ToList();
        var piezas = await db.Piezas.Where(p => p.PedidoPtId == pedido.Id && etiquetas.Contains(p.Etiqueta)).ToListAsync(cancellationToken);
        var ajenas = etiquetas.Except(piezas.Select(p => p.Etiqueta)).ToList();
        if (ajenas.Count > 0) throw new BusinessRuleException("PT_PARCIAL_PIEZAS_INVALIDAS", $"Piezas que no son del pedido {numero}: {string.Join(", ", ajenas)}");
        var fuera = piezas.Where(p => p.Estado != EstadoPieza.EnPT).Select(p => p.Etiqueta).ToList();
        if (fuera.Count > 0) throw new BusinessRuleException("PT_PARCIAL_PIEZA_NO_EN_PT", $"Solo salen piezas que están en PT: {string.Join(", ", fuera)}");
        foreach (var s in piezas.Select(p => p.SucursalId).Distinct())
            await acceso.VerificarSucursalAsync(s, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);

        var solicitud = SolicitudSalidaParcialPt.Crear(empresaId, pedido.Id, piezas.Select(p => p.Id).ToList(), c.Motivo, acceso.Actor, clock.UtcNow);
        db.SolicitudesParciales.Add(solicitud);
        await db.SaveChangesAsync(cancellationToken);
        return (await ParcialMapeo.ADtoAsync(db, [solicitud], cancellationToken))[0];
    }
}

public sealed class ResolverSalidaParcialHandler(ProductoTerminadoDbContext db, PtAcceso acceso, LiberacionPedido liberacion, IPedidosPtReadPort pedidos, IClock clock)
    : IRequestHandler<ResolverSalidaParcialCommand, SolicitudParcialDto>
{
    public async Task<SolicitudParcialDto> Handle(ResolverSalidaParcialCommand c, CancellationToken cancellationToken)
    {
        var s = await db.SolicitudesParciales.FirstOrDefaultAsync(x => x.Id == c.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PT_PARCIAL_NO_ENCONTRADA", "No existe la solicitud de salida parcial.");
        var ahora = clock.UtcNow;
        if (c.Autorizar)
        {
            // Regla 4 (Guadalupe, 9-nov): Crédito y Cobranza; Gerencia de Ventas solo como suplente y dice por qué.
            var nivel = c.Nivel?.Trim() is "ventas" ? "ventas" : c.Nivel?.Trim() is "credito" ? "credito"
                : throw new BusinessRuleException("PT_NIVEL_AUTORIZACION_INVALIDO", "La salida parcial la autoriza Crédito y Cobranza (credito) o, en su ausencia, Gerencia de Ventas (ventas).");
            await acceso.ExigirPermisoAsync(nivel == "credito" ? PermisosCanonicos.ProductoTerminadoSalidaAprobarCredito : PermisosCanonicos.ProductoTerminadoSalidaAprobarVentas, cancellationToken);
            s.Autorizar(nivel, nivel == "ventas" ? ReglasSalidaPt.MotivoValido(c.Suplencia) : null, c.Motivo, acceso.Actor, ahora);

            // Autorizada, el pedido entra a 69 y se libera por saldo o REPARTO como uno completo (LiberacionPedido); si no, queda retenido para CxC.
            var pedido = await db.PedidosPt.Include(p => p.Posiciones).FirstAsync(p => p.Id == s.PedidoPtId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (pedido.Liberacion is null && await pedidos.ObtenerPorNumeroAsync(pedido.EmpresaId, pedido.NumeroPedido, cancellationToken) is { } origen)
                await liberacion.EntrarA69Async(pedido, origen, cancellationToken);
            else await liberacion.ReevaluarAsync(pedido, cancellationToken);
        }
        else
        {
            if (!await acceso.TieneAsync(PermisosCanonicos.ProductoTerminadoSalidaAprobarCredito, cancellationToken)
                && !await acceso.TieneAsync(PermisosCanonicos.ProductoTerminadoSalidaAprobarVentas, cancellationToken))
                throw new ForbiddenException("PT_PERMISO_REQUERIDO", "No tiene permiso para resolver salidas parciales.");
            s.Rechazar(c.Motivo, acceso.Actor, ahora);
        }
        await db.SaveChangesAsync(cancellationToken);
        return (await ParcialMapeo.ADtoAsync(db, [s], cancellationToken))[0];
    }
}

public sealed class ListarSolicitudesParcialesHandler(ProductoTerminadoDbContext db, PtAcceso acceso) : IRequestHandler<ListarSolicitudesParcialesQuery, SolicitudesParcialesPagina>
{
    public async Task<SolicitudesParcialesPagina> Handle(ListarSolicitudesParcialesQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        var visibles = await acceso.SucursalesVisiblesAsync(PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, cancellationToken);
        var consulta = db.SolicitudesParciales.AsNoTracking().AsQueryable();
        if (q.Estado is { Length: > 0 } e && Enum.TryParse<EstadoSolicitudPt>(e, out var estado)) consulta = consulta.Where(x => x.Estado == estado);
        if (q.Pedido?.Trim() is { Length: > 0 } numero)
            consulta = consulta.Where(x => db.PedidosPt.Any(p => p.Id == x.PedidoPtId && p.NumeroPedido == numero));
        // Alcance por sucursal física de las piezas del pedido (ADR-0051).
        if (visibles is not null)
            consulta = consulta.Where(x => db.Piezas.Any(p => p.PedidoPtId == x.PedidoPtId && visibles.Contains(p.SucursalId)));
        var total = await consulta.CountAsync(cancellationToken);
        var items = await consulta.OrderByDescending(x => x.SolicitadaAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(cancellationToken);
        return new SolicitudesParcialesPagina(await ParcialMapeo.ADtoAsync(db, items, cancellationToken), total);
    }
}
