using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Correcciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Correcciones;

public sealed record SolicitarCorreccionConsumo(Guid SalidaOrigenId, Guid SubAlmacenDestinoId, string Tipo, string Motivo,
    string? Observaciones, IReadOnlyList<CorreccionConsumoLinea> Lineas);

/// <summary>Consumido, corregido (devoluciones registradas) y neto de una línea de la salida.</summary>
public sealed record SaldoCorreccionLinea(Guid LineaSalidaId, Guid ArticuloId, string UnidadMedida, decimal Consumido, decimal Corregido,
    decimal Neto, decimal Pendiente);

/// <summary>
/// M-334 y M-331: corrección de consumo. Almacén la solicita (pendiente, sin mover saldo) y Costos la autoriza: entonces se aplica la
/// devolución interna (8.A) ligada a la salida, con fecha de hoy (mes corriente) y al costo con que salió. Una corrección no excede
/// lo consumido menos lo ya corregido, no se aplica sin autorización y no se crea dos veces.
/// </summary>
public static class CorreccionConsumoApp
{
    public static async Task<CorreccionConsumo> SolicitarAsync(AlmacenDbContext db, Guid empresaId, Guid usuarioId,
        SolicitarCorreccionConsumo s, CancellationToken ct)
    {
        var salida = await db.Movimientos.AsNoTracking().Include(m => m.Lineas)
            .FirstOrDefaultAsync(m => m.Id == s.SalidaOrigenId, ct)
            ?? throw new EntityNotFoundException("SALIDA_ORIGEN_NO_ENCONTRADA", "No existe la salida que se quiere corregir.");
        if (salida.Tipo is not (TipoMovimiento.SalidaConsumo or TipoMovimiento.SalidaPorVale) || salida.Estado != EstadoMovimiento.Registrado)
            throw new BusinessRuleException("CORRECCION_ORIGEN_NO_CONSUMO", "Solo se corrige una salida por consumo registrada.");
        if (await db.CorreccionesConsumo.AnyAsync(c => c.SalidaOrigenId == s.SalidaOrigenId && c.Estado == CorreccionConsumo.Pendiente, ct))
            throw new BusinessRuleException("CORRECCION_DUPLICADA", $"La salida {salida.Folio} ya tiene una corrección pendiente de autorizar.");

        var destino = await db.SubAlmacenes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.SubAlmacenDestinoId, ct)
            ?? throw new EntityNotFoundException("SUBALMACEN_NO_ENCONTRADO", "No existe el sub-almacén destino.");
        if (s.Tipo == CorreccionConsumo.DevolucionDanada && destino.Tipo != TipoSubAlmacen.MaterialEnRevision)
            throw new BusinessRuleException("CORRECCION_DANADO_A_REVISION", "El material dañado regresa al sub-almacén de material en revisión.");

        var saldos = await SaldosAsync(db, salida, ct);
        foreach (var g in s.Lineas.GroupBy(l => l.LineaSalidaOrigenId))
        {
            var saldo = saldos.FirstOrDefault(x => x.LineaSalidaId == g.Key)
                ?? throw new BusinessRuleException("CORRECCION_LINEA_AJENA", "El renglón no pertenece a la salida seleccionada.");
            var pide = g.Sum(l => l.Cantidad);
            if (pide > saldo.Neto)
                throw new BusinessRuleException("CORRECCION_EXCEDE_CONSUMO",
                    $"La corrección ({pide:0.####} {saldo.UnidadMedida}) excede lo consumido neto de la salida ({saldo.Neto:0.####}: consumido {saldo.Consumido:0.####}, ya corregido {saldo.Corregido:0.####}).");
        }

        var c = new CorreccionConsumo(empresaId, salida.Id, salida.Folio ?? string.Empty, s.SubAlmacenDestinoId, s.Tipo, s.Motivo,
            s.Observaciones, s.Lineas, usuarioId);
        db.CorreccionesConsumo.Add(c);
        return c;
    }

    /// <summary>
    /// Costos autoriza: aplica la devolución interna ligada a la salida con fecha <paramref name="hoy"/> (mes corriente) en la misma
    /// transacción que marca la corrección. La devolución vuelve a validar que no exceda lo consumido y el periodo abierto.
    /// </summary>
    public static async Task AutorizarAsync(AlmacenDbContext db, ISender mediator, CorreccionConsumo c, Guid usuarioId, DateOnly hoy,
        CancellationToken ct)
    {
        c.VerificarDecision(usuarioId);
        await using var tx = db.Database.IsNpgsql() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var motivo = $"Corrección de consumo · {CorreccionConsumo.Tipos[c.Tipo]}: {c.Motivo}";
        if (motivo.Length > 500) motivo = motivo[..500];
        var r = await mediator.Send(new AplicarDevolucionInternaCommand(c.SalidaOrigenId, c.SubAlmacenDestinoId, hoy, c.EstadoMaterial,
            motivo, c.Observaciones, c.Lineas.Select(l => new DevolucionInternaLineaInput(l.LineaSalidaOrigenId, l.Cantidad, l.UbicacionId)).ToList()), ct);
        c.Autorizar(usuarioId, r.DevolucionId, r.Folio);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    /// <summary>Por línea de la salida: consumido, corregido (devoluciones registradas ligadas), neto y lo pendiente de autorizar.</summary>
    public static async Task<List<SaldoCorreccionLinea>> SaldosAsync(AlmacenDbContext db, MovimientoInventario salida, CancellationToken ct)
    {
        var devueltas = await db.Movimientos.AsNoTracking()
            .Where(m => m.SalidaOrigenId == salida.Id && m.Estado == EstadoMovimiento.Registrado)
            .SelectMany(m => m.Lineas).Select(l => new { l.LineaSalidaOrigenId, l.ArticuloId, l.Cantidad }).ToListAsync(ct);
        var pendientes = (await db.CorreccionesConsumo.AsNoTracking()
                .Where(c => c.SalidaOrigenId == salida.Id && c.Estado == CorreccionConsumo.Pendiente).ToListAsync(ct))
            .SelectMany(c => c.Lineas).ToList();
        return salida.Lineas.OrderBy(l => l.Posicion).Select(l =>
        {
            // Histórico sin enlace por línea: se cuenta por artículo, igual que la devolución interna.
            var corregido = devueltas.Where(d => d.LineaSalidaOrigenId == l.Id || (d.LineaSalidaOrigenId == null && d.ArticuloId == l.ArticuloId))
                .Sum(d => d.Cantidad);
            return new SaldoCorreccionLinea(l.Id, l.ArticuloId, l.UnidadMedida, l.Cantidad, corregido, l.Cantidad - corregido,
                pendientes.Where(p => p.LineaSalidaOrigenId == l.Id).Sum(p => p.Cantidad));
        }).ToList();
    }
}
