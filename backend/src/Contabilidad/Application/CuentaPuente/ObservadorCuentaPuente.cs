using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.CuentaPuente;

/// <summary>Folio y proveedor de una orden de compra para el auxiliar de la puente (puerto de CONSUMIDOR; lo implementa Compras).</summary>
public interface IOrdenCompraPuentePort
{
    Task<IReadOnlyDictionary<Guid, OrdenCompraPuente>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

public sealed record OrdenCompraPuente(Guid Id, string Folio, Guid ProveedorId);

/// <summary>
/// Observador de pólizas automáticas de C1.4 (K10.5): dentro de la misma transacción, convierte las partidas con
/// <see cref="Polizas.ReferenciaPuente"/> en movimientos de la puente por OC y, en la anulación, revierte los de la póliza original.
/// La partida guardada aporta su id, sucursal, proveedor y moneda; Compras aporta el folio de la OC. Guarda en el mismo contexto y
/// transacción del puerto (el puerto también guarda después de avisar): si la póliza se revierte, el movimiento también.
/// </summary>
public sealed class ObservadorCuentaPuente(ContabilidadDbContext db, RegistroMovimientosPuente registro, IOrdenCompraPuentePort ordenes)
    : IPolizaAutomaticaObservador
{
    public async Task PolizaRegistradaAsync(PolizaAutomaticaRegistrada poliza, CancellationToken ct)
    {
        if (poliza.PolizaAnuladaId is { } anulada)
        {
            await registro.RevertirPorPolizaAsync(poliza.EmpresaId, anulada, poliza.PolizaId, poliza.Folio, poliza.Fecha, ct);
            await registro.GuardarAsync(ct);
            return;
        }
        var puentes = poliza.Partidas.Where(p => p.Puente is not null).ToDictionary(p => p.Linea, p => p.Puente!);
        if (puentes.Count == 0) return;

        var guardadas = await db.PolizasPartidas.AsNoTracking().Where(x => x.PolizaId == poliza.PolizaId).OrderBy(x => x.Linea).ToListAsync(ct);
        var ocs = await ordenes.ObtenerAsync(
            [.. puentes.Values.Where(r => r.OrdenCompraId != Guid.Empty).Select(r => r.OrdenCompraId).Distinct()], ct);
        var partidas = guardadas.Select(x =>
        {
            var r = puentes.GetValueOrDefault(x.Linea);
            var oc = r is null ? null : ocs.GetValueOrDefault(r.OrdenCompraId);
            return new PartidaPuente(x.Id, x.Linea, x.CuentaId, x.Cargo, x.Abono, x.SucursalId, x.ProveedorId ?? oc?.ProveedorId, x.Moneda,
                x.TipoCambio, x.ImporteOriginal, r, oc?.Folio);
        }).ToList();
        await registro.RegistrarAsync(poliza.EmpresaId, poliza.PolizaId, poliza.Folio, poliza.Fecha, partidas, ct);
        await registro.GuardarAsync(ct);
    }
}
