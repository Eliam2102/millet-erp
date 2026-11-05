using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Application.Taller;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Taller;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>
/// Descarga del inventario del taller por pedido interno Serie 9000 (PT5.10). Lo llama el lector de A+W (worker, sin
/// usuario autenticado): el <c>Bypass()</c> vive aquí con la empresa explícita, igual que
/// <see cref="ConsumoComponentesGuardado"/>. Cada posición baja hojas de su medida en la sucursal por el mismo camino de
/// salida de la app (todo o nada; sin existencia queda en revisión). Nunca crea un pedido facturable.
/// </summary>
public sealed class DescargaTallerGuardado(AlmacenDbContext db, ICurrentEmpresaContext empresa,
    IAppSalidaResolverPort resolver, IUsuarioServicioReadPort usuarioServicio, IIntegrationEventPublisher events)
{
    public const string Causa = "descarga_taller_9000";

    /// <summary>Día del último pedido registrado para el origen (marca de agua del lector).</summary>
    public async Task<DateOnly?> MarcaAguaAsync(Guid empresaId, string origen, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        return await db.DescargasTallerAw.AsNoTracking().Where(x => x.EmpresaId == empresaId && x.Origen == origen)
            .MaxAsync(x => (DateOnly?)x.FechaPedido, ct);
    }

    public async Task<IReadOnlyList<Guid>> EnRevisionAsync(Guid empresaId, int max, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        return await db.DescargasTallerAw.AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.Estado == DescargaTallerAw.EnRevision)
            .OrderBy(x => x.IntentadoAt).Select(x => x.Id).Take(max).ToListAsync(ct);
    }

    /// <summary>Registra y descarga una posición. <c>false</c> = ya estaba registrada (no se vuelve a descargar).</summary>
    public async Task<bool> RegistrarAsync(Guid empresaId, string origen, PedidoInternoLineaAw p, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        if (await db.DescargasTallerAw.AsNoTracking().AnyAsync(x => x.EmpresaId == empresaId && x.Origen == origen
                && x.NumeroPedido == p.NumeroPedido && x.Posicion == p.Posicion, ct))
            return false;
        var d = new DescargaTallerAw(empresaId, origen, p.NumeroPedido, p.Posicion, p.ArticuloRef, p.Cantidad,
            p.AnchoMm, p.AltoMm, p.ClaveSucursal, p.Fecha);
        db.DescargasTallerAw.Add(d);
        return await EvaluarYGuardarAsync(d, ct);
    }

    /// <summary>Vuelve a evaluar una descarga en revisión (llegó la medida, la existencia o el maestro).</summary>
    public async Task ReintentarAsync(Guid id, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        var d = await db.DescargasTallerAw.SingleOrDefaultAsync(x => x.Id == id && x.Estado == DescargaTallerAw.EnRevision, ct);
        if (d is not null) await EvaluarYGuardarAsync(d, ct);
    }

    private async Task<bool> EvaluarYGuardarAsync(DescargaTallerAw d, CancellationToken ct)
    {
        await using var tx = db.Database.IsNpgsql() ? await db.Database.BeginTransactionAsync(ct) : null;
        await EvaluarAsync(d, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Otra lectura del mismo pedido ganó la carrera: se revierte todo, incluido el movimiento.
            if (tx is not null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task EvaluarAsync(DescargaTallerAw d, CancellationToken ct)
    {
        Guid? sucursalId = null;
        void Revision(string error) => d.Resolver(DescargaTallerAw.EnRevision, sucursalId, null, null, error);

        if (string.IsNullOrWhiteSpace(d.ClaveSucursal)) { Revision($"El pedido {d.NumeroPedido} no trae sucursal."); return; }
        // Sin grupos solo resuelve la sucursal: así la revisión queda en el día y taller correctos aunque falte lo demás.
        var rs = await resolver.ResolverAsync(d.ClaveSucursal, [], ct);
        if (rs.Error is not null) { Revision(rs.Error); return; }
        sucursalId = rs.SucursalId;
        // Regla 7 (A4.1): la existencia es por medida, nunca se suman medidas distintas.
        if (d.AnchoMm is not > 0 || d.AltoMm is not > 0) { Revision($"La posición {d.Posicion} del pedido {d.NumeroPedido} no trae la medida de la hoja."); return; }
        if (d.Cantidad <= 0) { Revision($"La posición {d.Posicion} del pedido {d.NumeroPedido} no trae cantidad de hojas."); return; }
        var usuario = await usuarioServicio.ObtenerReordenAsync(ct);
        if (usuario is null || !usuario.Activo) { Revision("El usuario de servicio del ERP no está disponible."); return; }
        var r = await resolver.ResolverAsync(d.ClaveSucursal, [new GrupoSalidaApp(d.ArticuloRef, d.AnchoMm, d.AltoMm)], ct);
        if (r.Error is not null) { Revision(r.Error); return; }
        var g = r.Grupos[0];
        // El vidrio por medida se lleva en m² (hojas × área de la hoja), igual que las salidas de la app de Almacenes.
        var area = g.AreaHojaM2 ?? d.AnchoMm.Value * d.AltoMm.Value / 1_000_000m;
        var m2 = Math.Round(d.Cantidad * area, 4);

        var motivo = $"Serie 9000 taller: pedido {d.NumeroPedido} pos {d.Posicion}";
        var (estado, folio, error) = await SalidaAppAlmacenes.RegistrarAsync(db, events, d.EmpresaId, usuario.Id,
            new SalidaApp(TipoMovimiento.SalidaConsumo, r.SucursalId, g.ArticuloId, g.UnidadMedida, g.ProductoAwVarianteId,
                g.AreaHojaM2 is null ? d.Cantidad : m2, null, motivo, g.AnchoMm, g.AltoMm, Causa), ct);
        d.Resolver(estado == SalidaAppAlmacenes.Recibido ? DescargaTallerAw.Recibido : DescargaTallerAw.EnRevision, r.SucursalId,
            estado == SalidaAppAlmacenes.Recibido ? m2 : null, folio, error);
    }
}
