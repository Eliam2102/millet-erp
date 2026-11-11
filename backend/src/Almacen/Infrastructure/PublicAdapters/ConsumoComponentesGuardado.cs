using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Consumo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>
/// Descuento de componentes por composición (A4.8). Lo llama el lector de A+W (worker, sin usuario autenticado):
/// por eso el <c>Bypass()</c> vive aquí, con la empresa explícita y el usuario de servicio del ERP (mismo precedente que
/// <see cref="SalidaAppAlmacenesGuardado"/>). D8: usa el mismo camino de salida de la app (todo o nada, sin existencia
/// → revisión, con folio). Sucursal (D7) y artículo se resuelven con el mismo puerto que las salidas de la app.
/// </summary>
public sealed class ConsumoComponentesGuardado(AlmacenDbContext db, ICurrentEmpresaContext empresa,
    IAppSalidaResolverPort resolver, IUsuarioServicioReadPort usuarioServicio, IIntegrationEventPublisher events,
    IArticuloReadPort articulos)
{
    /// <summary>Hora del último escaneo registrado para el origen (marca de agua del lector).</summary>
    public async Task<DateTime?> MarcaAguaAsync(Guid empresaId, string origen, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        return await db.ConsumosComponenteAw.AsNoTracking().Where(x => x.EmpresaId == empresaId && x.Origen == origen)
            .MaxAsync(x => (DateTime?)x.BearbZeit, ct);
    }

    /// <summary>Consumos en revisión, los que llevan más tiempo sin reintentarse primero.</summary>
    public async Task<IReadOnlyList<Guid>> EnRevisionAsync(Guid empresaId, int max, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        return await db.ConsumosComponenteAw.AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.Estado == ConsumoComponenteAw.EnRevision)
            .OrderBy(x => x.IntentadoAt).Select(x => x.Id).Take(max).ToListAsync(ct);
    }

    /// <summary>Registra y descuenta un componente. <c>false</c> = ya estaba registrado (no se vuelve a descontar).</summary>
    public async Task<bool> RegistrarAsync(Guid empresaId, string origen, PiezaComponenteAw p, CancellationToken ct,
        bool afectaInventario = false)
    {
        using var bypass = empresa.Bypass();
        if (await db.ConsumosComponenteAw.AsNoTracking().AnyAsync(x => x.EmpresaId == empresaId && x.Origen == origen
                && x.Etiqueta == p.Etiqueta && x.Operacion == p.Operacion && x.ComponenteRef == p.ComponenteRef, ct))
            return false;
        var c = new ConsumoComponenteAw(empresaId, origen, p.Etiqueta, p.Operacion, p.ComponenteRef, p.Capas, p.Auftnr, p.Pos,
            p.AnchoMm, p.AltoMm, p.ClaveSucursal, p.BearbZeit);
        db.ConsumosComponenteAw.Add(c);
        return await EvaluarYGuardarAsync(c, afectaInventario, ct);
    }

    /// <summary>Vuelve a evaluar un consumo en revisión (p. ej. ya se capturó la norma o llegó existencia).</summary>
    public async Task ReintentarAsync(Guid id, CancellationToken ct, bool afectaInventario = false)
    {
        using var bypass = empresa.Bypass();
        var c = await db.ConsumosComponenteAw.SingleOrDefaultAsync(x => x.Id == id && x.Estado == ConsumoComponenteAw.EnRevision, ct);
        if (c is not null) await EvaluarYGuardarAsync(c, afectaInventario, ct);
    }

    private async Task<bool> EvaluarYGuardarAsync(ConsumoComponenteAw c, bool afectaInventario, CancellationToken ct)
    {
        await using var tx = db.Database.IsNpgsql() ? await db.Database.BeginTransactionAsync(ct) : null;
        await EvaluarAsync(c, afectaInventario, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Otra lectura del mismo escaneo ganó la carrera: se revierte todo, incluido el movimiento.
            if (tx is not null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task EvaluarAsync(ConsumoComponenteAw c, bool afectaInventario, CancellationToken ct)
    {
        if (c.ComponenteRef.Length == 0)
        {
            c.Resolver(ConsumoComponenteAw.Excepcion, null, null,
                $"La pieza {c.Etiqueta} (pedido {c.Auftnr}) no trae componentes legibles en su composición: solo se descuenta el vidrio.");
            return;
        }
        void Revision(string error) => c.Resolver(ConsumoComponenteAw.EnRevision, null, null, error);

        if (string.IsNullOrWhiteSpace(c.ClaveSucursal)) { Revision($"El pedido {c.Auftnr} no trae sucursal (OR_AVBEREICH)."); return; }
        var usuario = await usuarioServicio.ObtenerReordenAsync(ct);
        if (usuario is null || !usuario.Activo) { Revision("El usuario de servicio del ERP no está disponible."); return; }
        var r = await resolver.ResolverAsync(c.ClaveSucursal, [new GrupoSalidaApp(c.ComponenteRef, null, null)], ct);
        if (r.Error is not null) { Revision(r.Error); return; }
        var g = r.Grupos[0];
        // M-172 (AC-030): silicón y Ködispace ya salieron del inventario por tambor a la línea; la pieza no los descuenta.
        if (await articulos.ObtenerAsync(g.ArticuloId, ct) is { ConsumoPorTamborALinea: true })
        {
            c.Resolver(ConsumoComponenteAw.PorTambor, null, null, null);
            return;
        }
        var norma = await db.NormasConsumoComponente.AsNoTracking()
            .FirstOrDefaultAsync(n => n.EmpresaId == c.EmpresaId && n.ArticuloId == g.ArticuloId, ct);
        var (cantidad, error) = ConsumoComponentes.Calcular(g.UnidadMedida, c.AnchoMm, c.AltoMm, norma, c.ComponenteRef);
        if (cantidad is null) { Revision(error!); return; }
        cantidad *= c.Capas;
        // Fernando 10-nov: hoy el inventario baja con la salida del almacén; el consumo calculado solo se guarda para estadística.
        if (!afectaInventario)
        {
            c.Resolver(ConsumoComponenteAw.Estadistico, cantidad, null, null);
            return;
        }

        var motivo = $"A+W consumo por composición: operación {c.Operacion} pieza {c.Etiqueta} pedido {c.Auftnr} pos {c.Pos}";
        var (estado, folio, errorSalida) = await SalidaAppAlmacenes.RegistrarAsync(db, events, c.EmpresaId, usuario.Id,
            new SalidaApp(TipoMovimiento.SalidaConsumo, r.SucursalId, g.ArticuloId, g.UnidadMedida, null, cantidad.Value, null, motivo,
                Causa: "consumo_composicion"), ct);
        c.Resolver(estado, cantidad, folio, errorSalida);
    }
}
