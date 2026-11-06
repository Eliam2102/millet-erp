using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>CXC-06 (86e3qddef): pólizas de cobro vigentes para la conciliación de cartera de CxC.</summary>
public sealed class CobrosContabilizadosReadAdapter(ContabilidadDbContext db) : ICobrosContabilizadosReadPort
{
    private static readonly string[] Tipos =
        [DocumentoCobroContable.PagoCliente, DocumentoCobroContable.CobroMostrador, DocumentoCobroContable.CajaSesion];

    public Task<IReadOnlyList<PolizaCobroLectura>> ListarAsync(DateOnly desde, DateOnly hasta, CancellationToken ct) =>
        LeerAsync(db.Polizas.AsNoTracking().Where(p => p.Fecha >= desde && p.Fecha <= hasta && Tipos.Contains(p.OrigenTipo!)), ct);

    public Task<IReadOnlyList<PolizaCobroLectura>> ListarPorDocumentosAsync(
        string tipoDocumento, IReadOnlyCollection<Guid> documentos, CancellationToken ct) =>
        documentos.Count == 0
            ? Task.FromResult<IReadOnlyList<PolizaCobroLectura>>([])
            : LeerAsync(db.Polizas.AsNoTracking().Where(p => p.OrigenTipo == tipoDocumento && documentos.Contains(p.OrigenDocumentoId!.Value)), ct);

    public async Task<IReadOnlyList<EfectivoCobroLectura>> EfectivoAsync(IReadOnlyCollection<Guid> cobrosMostrador, CancellationToken ct)
    {
        if (cobrosMostrador.Count == 0) return [];
        return await db.CobrosEfectivoCaja.AsNoTracking()
            .Where(c => cobrosMostrador.Contains(c.CobroId) && c.Estado != EstadoCobroEfectivo.Cancelado)
            .Select(c => new EfectivoCobroLectura(c.CobroId, c.Importe, c.Estado == EstadoCobroEfectivo.Liquidado, c.PolizaLiquidacionId))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Vigentes = autorizadas, ni anulación (K10.3) ni anuladas. Por póliza y cliente: abonos menos cargos de las partidas con auxiliar de
    /// cliente, en la moneda original (el importe en MXN si la partida es nacional).
    /// </summary>
    private static async Task<IReadOnlyList<PolizaCobroLectura>> LeerAsync(IQueryable<Poliza> polizas, CancellationToken ct)
    {
        var filas = await polizas
            .Where(p => p.Estado == EstadoPoliza.Autorizada && p.PolizaAnuladaId == null && p.AnuladaPorPolizaId == null
                && p.OrigenDocumentoId != null)
            .SelectMany(p => p.Partidas.Where(d => d.ClienteId != null).Select(d => new
            {
                p.Id, p.TipoClave, p.Numero, p.Fecha, p.OrigenTipo, Documento = p.OrigenDocumentoId!.Value, d.ClienteId, d.Moneda,
                d.Cargo, d.Abono, d.ImporteOriginal,
            }))
            .ToListAsync(ct);

        return filas
            .GroupBy(f => new { f.Id, f.TipoClave, f.Numero, f.Fecha, f.OrigenTipo, f.Documento, f.ClienteId, f.Moneda })
            .Select(g => new PolizaCobroLectura(
                g.Key.Id, g.Key.Numero is { } n ? Poliza.FormatearFolio(g.Key.TipoClave, n) : string.Empty, g.Key.Fecha,
                g.Key.OrigenTipo!, g.Key.Documento, g.Key.ClienteId, g.Key.Moneda,
                g.Sum(x => x.ImporteOriginal is { } original ? Math.Sign(x.Abono - x.Cargo) * Math.Abs(original) : x.Abono - x.Cargo)))
            .ToList();
    }
}
