using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="ICxpDocumentosReadPort"/>
/// declarado en <c>Almacen.Domain.Ports</c> — primer puerto de lectura
/// Almacén → CxP. Reemplaza el <c>NoOpCxpDocumentosReadPort</c> de Almacén
/// desde el día 1 (no queda deuda de plataforma).
///
/// <para>Lectura de presentación cross-módulo vía
/// <see cref="CuentasPorPagarDbContext"/> con <c>AsNoTracking</c>. Usa
/// <c>ICurrentEmpresaContext.Bypass()</c> por el mismo motivo que
/// <see cref="CxpFacturasTrazabilidadProvider"/>: la query de Almacén ya
/// operó bajo el filtro de empresa de su propio módulo; aquí solo se
/// resuelven etiquetas de ids que Almacén ya tiene persistidos.</para>
///
/// <para>State-agnostic (ADR-0042): resuelve el folio/UUID aunque el
/// documento CxP haya avanzado o cambiado de estado — es lectura de
/// presentación, no operativa.</para>
/// </summary>
public sealed class CxpDocumentosReadAdapter : ICxpDocumentosReadPort
{
    private static readonly IReadOnlyDictionary<Guid, string> Empty =
        new Dictionary<Guid, string>();

    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public CxpDocumentosReadAdapter(
        CuentasPorPagarDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosFacturaAsync(
        IReadOnlyCollection<Guid> facturaIds,
        CancellationToken cancellationToken)
    {
        if (facturaIds.Count == 0) return Empty;
        var distinct = facturaIds.Distinct().ToArray();

        using var bypass = _empresaContext.Bypass();

        var facturas = await _db.FacturasProveedor
            .AsNoTracking()
            .Where(f => distinct.Contains(f.Id))
            .Select(f => new { f.Id, f.SerieProveedor, f.FolioProveedor })
            .ToListAsync(cancellationToken);

        return facturas
            .Select(f => new { f.Id, Folio = FormatearFolio(f.SerieProveedor, f.FolioProveedor) })
            .Where(f => f.Folio is not null)
            .ToDictionary(f => f.Id, f => f.Folio!);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerUuidsFiscalesCfdiAsync(
        IReadOnlyCollection<Guid> cfdiRecibidoIds,
        CancellationToken cancellationToken)
    {
        if (cfdiRecibidoIds.Count == 0) return Empty;
        var distinct = cfdiRecibidoIds.Distinct().ToArray();

        using var bypass = _empresaContext.Bypass();

        // UuidCfdi es un VO con value converter: se proyecta el VO y se
        // extrae .Valor en memoria (la conversión a string la hace EF).
        var cfdis = await _db.CfdisRecibidos
            .AsNoTracking()
            .Where(c => distinct.Contains(c.Id))
            .Select(c => new { c.Id, c.UuidCfdi })
            .ToListAsync(cancellationToken);

        return cfdis.ToDictionary(c => c.Id, c => c.UuidCfdi.Valor);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosNotaCreditoAsync(
        IReadOnlyCollection<Guid> notaCreditoIds,
        CancellationToken cancellationToken)
    {
        if (notaCreditoIds.Count == 0) return Empty;
        var distinct = notaCreditoIds.Distinct().ToArray();

        using var bypass = _empresaContext.Bypass();

        var notas = await _db.NotasCreditoProveedor
            .AsNoTracking()
            .Where(n => distinct.Contains(n.Id))
            .Select(n => new { n.Id, n.SerieProveedor, n.FolioProveedor })
            .ToListAsync(cancellationToken);

        return notas
            .Select(n => new { n.Id, Folio = FormatearFolio(n.SerieProveedor, n.FolioProveedor) })
            .Where(n => n.Folio is not null)
            .ToDictionary(n => n.Id, n => n.Folio!);
    }

    public async Task<CfdiRecibidoLectura?> ObtenerCfdiAsync(
        Guid cfdiRecibidoId,
        CancellationToken cancellationToken)
    {
        // Lectura operativa: respeta el filtro de empresa (sin Bypass()).
        // RfcEmisor es VO con value converter: se proyecta el VO y se extrae .Valor en memoria.
        var fila = await _db.CfdisRecibidos
            .AsNoTracking()
            .Where(c => c.Id == cfdiRecibidoId)
            .Select(c => new { c.Id, c.RfcEmisor, c.Estado, c.Tipo })
            .FirstOrDefaultAsync(cancellationToken);

        if (fila is null) return null;
        var ocLibre = await _db.FacturasProveedor.AsNoTracking()
            .Where(f => f.CfdiRecibidoId == cfdiRecibidoId && f.Estado != EstadoPasivo.Cancelada && f.RecepcionId == null)
            .Select(f => f.OrdenCompraId).FirstOrDefaultAsync(cancellationToken);
        return new CfdiRecibidoLectura(fila.Id, fila.RfcEmisor.Valor, fila.Estado.ToString(), fila.Tipo.ToString(), ocLibre);
    }

    public async Task<IReadOnlyList<FacturaLigadaRecepcionLectura>> ObtenerFacturasLigadasRecepcionAsync(
        IReadOnlyCollection<Guid> recepcionIds,
        IReadOnlyCollection<Guid> facturaIds,
        CancellationToken cancellationToken)
    {
        var recepciones = recepcionIds.Distinct().ToArray();
        var facturas = facturaIds.Distinct().ToArray();
        if (recepciones.Length == 0 && facturas.Length == 0) return [];

        // Lectura operativa: respeta el filtro de empresa (sin Bypass()).
        // ImporteSaldado es calculado en el dominio: se proyectan sus sumandos y se suma en memoria.
        var filas = await _db.FacturasProveedor
            .AsNoTracking()
            .Where(f => f.Estado != EstadoPasivo.Cancelada
                && ((f.RecepcionId != null && recepciones.Contains(f.RecepcionId.Value)) || facturas.Contains(f.Id)))
            .Select(f => new
            {
                f.Id, f.SerieProveedor, f.FolioProveedor, f.Estado,
                f.ImportePagado, f.AnticipoAplicadoTotal, f.NcAplicadasTotal, f.CargosAplicadosTotal,
            })
            .ToListAsync(cancellationToken);

        return filas
            .Select(f => new FacturaLigadaRecepcionLectura(
                f.Id, FormatearFolio(f.SerieProveedor, f.FolioProveedor), f.Estado.ToString(),
                f.ImportePagado + f.AnticipoAplicadoTotal + f.NcAplicadasTotal + f.CargosAplicadosTotal))
            .ToList();
    }

    private static string? FormatearFolio(string? serie, string? folio)
    {
        if (string.IsNullOrWhiteSpace(folio) && string.IsNullOrWhiteSpace(serie))
            return null;
        if (string.IsNullOrWhiteSpace(serie)) return folio;
        if (string.IsNullOrWhiteSpace(folio)) return serie;
        return $"{serie}-{folio}";
    }
}
