using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.CuentaPuente;

/// <summary>
/// Partida ya registrada en una póliza, con la referencia de OC (contrato de C1.4) si afectó la cuenta puente; sin referencia no genera
/// movimiento. En una nota de cargo o de crédito que no conoce la OC, <c>OrdenCompraId</c> va vacío y
/// <see cref="ReferenciaPuente.DocumentoOrigenFacturaId"/> indica la factura de la que se toma la OC.
/// </summary>
public sealed record PartidaPuente(
    Guid PolizaPartidaId, int Linea, Guid CuentaId, decimal Cargo, decimal Abono, Guid SucursalId, Guid? ProveedorId,
    string Moneda, decimal TipoCambio, decimal? ImporteOriginal, ReferenciaPuente? Referencia, string? OrdenCompraFolio = null);

/// <summary>OC encontrada a partir de la factura de origen de una nota.</summary>
public sealed record OcResueltaPuente(Guid OrdenCompraId, string? OrdenCompraFolio, Guid? ProveedorId);

/// <summary>Reglas puras de la entrada a la cuenta puente: qué movimientos nacen de una póliza y cuáles de su anulación.</summary>
public static class PlaneadorMovimientosPuente
{
    /// <summary>
    /// Un movimiento por partida con referencia que no esté ya registrada (idempotente por partida). Una partida en negativo se toma del
    /// lado contrario en positivo. La OC de una nota sin OC se resuelve por su factura de origen (registrada antes o en la misma póliza).
    /// </summary>
    public static IReadOnlyList<MovimientoPuente> Planear(Guid empresaId, Guid polizaId, string? folio, DateOnly fecha,
        IReadOnlyList<PartidaPuente> partidas, IReadOnlySet<Guid> partidasYaRegistradas, Func<Guid, OcResueltaPuente?> ocDeFactura)
    {
        var nuevos = new List<MovimientoPuente>();
        var vistas = new HashSet<Guid>(partidasYaRegistradas);
        var facturasDelLote = new Dictionary<Guid, OcResueltaPuente>();
        // Primero las que traen OC: así una nota puede tomar la OC de una factura de la misma póliza.
        foreach (var p in partidas.Where(x => x.Referencia is not null).OrderBy(x => x.Referencia!.OrdenCompraId == Guid.Empty).ThenBy(x => x.Linea))
        {
            if (!vistas.Add(p.PolizaPartidaId)) continue;
            var r = p.Referencia!;
            var oc = r.OrdenCompraId != Guid.Empty ? new OcResueltaPuente(r.OrdenCompraId, p.OrdenCompraFolio, p.ProveedorId)
                : r.DocumentoOrigenFacturaId is { } f ? facturasDelLote.GetValueOrDefault(f) ?? ocDeFactura(f) : null;
            if (oc is null)
                throw new BusinessRuleException("PUENTE_OC_NO_RESUELTA",
                    $"Partida {p.Linea}: no se pudo determinar la orden de compra del movimiento de la cuenta puente"
                    + (r.DocumentoOrigenFacturaId is { } fo ? $" (la factura de origen {fo} no tiene movimientos en la puente)." : " (indique la OC o la factura de origen)."));
            var (cargo, abono) = (p.Cargo, p.Abono) switch
            {
                ( < 0m, _) => (0m, -p.Cargo),
                (_, < 0m) => (-p.Abono, 0m),
                var x => x,
            };
            var m = MovimientoPuente.Registrar(Guid.CreateVersion7(), empresaId, polizaId, p.PolizaPartidaId, folio, fecha, p.CuentaId,
                p.SucursalId, p.ProveedorId ?? oc.ProveedorId, oc.OrdenCompraId, p.OrdenCompraFolio ?? oc.OrdenCompraFolio, r.LineaOcId,
                r.TipoDocumento, r.DocumentoId, r.DocumentoOrigenFacturaId, cargo, abono, p.Moneda, p.TipoCambio, p.ImporteOriginal);
            if (r.TipoDocumento == TipoDocumentoPuente.Factura)
                facturasDelLote.TryAdd(r.DocumentoId, new(m.OrdenCompraId, m.OrdenCompraFolio, m.ProveedorId));
            nuevos.Add(m);
        }
        return nuevos;
    }

    /// <summary>Reversas de los movimientos de una póliza anulada; omite reversas y partidas ya revertidas (idempotente).</summary>
    public static IReadOnlyList<MovimientoPuente> PlanearReversas(IEnumerable<MovimientoPuente> movimientosDeLaAnulada,
        IReadOnlySet<Guid> partidasYaRevertidas, Guid polizaAnulacionId, string? folio, DateOnly fechaAnulacion) =>
        [.. movimientosDeLaAnulada.Where(m => !m.EsReversa && !partidasYaRevertidas.Contains(m.PolizaPartidaId))
            .Select(m => m.Revertir(Guid.CreateVersion7(), polizaAnulacionId, folio, fechaAnulacion))];
}

/// <summary>
/// Entrada a la cuenta puente (K10.5). La llama el observador de pólizas automáticas en la misma transacción que registra o anula la
/// póliza (<see cref="ObservadorCuentaPuente"/>): aquí solo se agregan los movimientos al contexto y quien llama decide cuándo guardar
/// (o usa <see cref="GuardarAsync"/>).
/// </summary>
public sealed class RegistroMovimientosPuente(ContabilidadDbContext db)
{
    public async Task<IReadOnlyList<MovimientoPuente>> RegistrarAsync(Guid empresaId, Guid polizaId, string? folio, DateOnly fecha,
        IReadOnlyList<PartidaPuente> partidas, CancellationToken ct)
    {
        var conReferencia = partidas.Where(p => p.Referencia is not null).ToList();
        if (conReferencia.Count == 0) return [];

        var ids = conReferencia.Select(p => p.PolizaPartidaId).ToList();
        var ya = (await db.MovimientosPuente.AsNoTracking().Where(m => ids.Contains(m.PolizaPartidaId) && !m.EsReversa)
            .Select(m => m.PolizaPartidaId).ToListAsync(ct)).ToHashSet();
        ya.UnionWith(db.MovimientosPuente.Local.Where(m => !m.EsReversa && ids.Contains(m.PolizaPartidaId)).Select(m => m.PolizaPartidaId));

        var facturas = conReferencia.Where(p => p.Referencia!.OrdenCompraId == Guid.Empty && p.Referencia.DocumentoOrigenFacturaId is not null)
            .Select(p => p.Referencia!.DocumentoOrigenFacturaId!.Value).Distinct().ToList();
        var ocs = new Dictionary<Guid, OcResueltaPuente>();
        if (facturas.Count > 0)
        {
            var encontradas = await db.MovimientosPuente.AsNoTracking()
                .Where(m => m.TipoDocumento == TipoDocumentoPuente.Factura && !m.EsReversa && facturas.Contains(m.DocumentoId))
                .OrderBy(m => m.Fecha)
                .Select(m => new { m.DocumentoId, m.OrdenCompraId, m.OrdenCompraFolio, m.ProveedorId }).ToListAsync(ct);
            foreach (var f in encontradas) ocs.TryAdd(f.DocumentoId, new(f.OrdenCompraId, f.OrdenCompraFolio, f.ProveedorId));
        }

        var nuevos = PlaneadorMovimientosPuente.Planear(empresaId, polizaId, folio, fecha, partidas, ya, f => ocs.GetValueOrDefault(f));
        db.MovimientosPuente.AddRange(nuevos);
        return nuevos;
    }

    /// <summary>
    /// Reversa en la puente de los movimientos de una póliza anulada (K10.3). Repetir no duplica. <paramref name="empresaId"/> vacío no
    /// filtra: el puerto avisa la anulación antes de guardarla, cuando la póliza de anulación aún no tiene empresa; la reversa toma la
    /// empresa del movimiento original y el filtro global de empresa sigue aplicando.
    /// </summary>
    public async Task<IReadOnlyList<MovimientoPuente>> RevertirPorPolizaAsync(Guid empresaId, Guid polizaAnuladaId, Guid polizaAnulacionId,
        string? folio, DateOnly fechaAnulacion, CancellationToken ct)
    {
        var originales = await db.MovimientosPuente.AsNoTracking()
            .Where(m => (empresaId == Guid.Empty || m.EmpresaId == empresaId) && m.PolizaId == polizaAnuladaId && !m.EsReversa).ToListAsync(ct);
        if (originales.Count == 0) return [];
        var partidas = originales.Select(m => m.PolizaPartidaId).ToList();
        var revertidas = (await db.MovimientosPuente.AsNoTracking().Where(m => m.EsReversa && partidas.Contains(m.PolizaPartidaId))
            .Select(m => m.PolizaPartidaId).ToListAsync(ct)).ToHashSet();
        revertidas.UnionWith(db.MovimientosPuente.Local.Where(m => m.EsReversa && partidas.Contains(m.PolizaPartidaId)).Select(m => m.PolizaPartidaId));
        var reversas = PlaneadorMovimientosPuente.PlanearReversas(originales, revertidas, polizaAnulacionId, folio, fechaAnulacion);
        db.MovimientosPuente.AddRange(reversas);
        return reversas;
    }

    public Task GuardarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
