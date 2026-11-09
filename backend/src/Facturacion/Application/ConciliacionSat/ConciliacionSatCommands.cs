using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.ConciliacionSat;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.ConciliacionSat;

public sealed record ConteoDiferenciaSat(TipoDiferenciaSat Tipo, int Total, int Revisadas);

public sealed record ConciliacionSatResponse(
    Guid Id,
    DateOnly Desde,
    DateOnly Hasta,
    FuenteConciliacionSat Fuente,
    string NombreArchivo,
    DateTimeOffset EjecutadaEn,
    Guid? EjecutadaPor,
    int CfdiSat,
    int CfdiErp,
    int Coincidentes,
    int ConDiferencias,
    bool SinDiferencias,
    int DiferenciasPendientes,
    IReadOnlyList<ConteoDiferenciaSat> PorTipo);

public sealed record DiferenciaSatResponse(
    Guid Id,
    string Uuid,
    TipoDiferenciaSat Tipo,
    Guid? ComprobanteId,
    string? Folio,
    string? RfcReceptor,
    string? ValorSat,
    string? ValorErp,
    bool Revisada,
    Guid? RevisadaPor,
    DateTimeOffset? RevisadaEn,
    string? Nota,
    string? RevisadaPorNombre,
    string? Moneda);

/// <summary>
/// Concilia los CFDI emitidos que reporta el SAT (archivo de metadatos) contra
/// los timbrados del ERP en el periodo (FAC-10): faltantes de un lado y del
/// otro, estatus, total, efecto y receptor. Deja registro de la corrida aunque
/// no haya diferencias.
/// </summary>
public sealed record ConciliarCfdiSatCommand(string NombreArchivo, byte[] Contenido, DateOnly Desde, DateOnly Hasta)
    : IRequest<ConciliacionSatResponse>;

public sealed record MarcarDiferenciaSatRevisadaCommand(Guid ConciliacionId, Guid DiferenciaId, string? Nota)
    : IRequest<DiferenciaSatResponse>;

public sealed record ConciliacionesSatQuery(int Limit = 20) : IRequest<IReadOnlyList<ConciliacionSatResponse>>;

public sealed record ConciliacionSatQuery(Guid Id) : IRequest<ConciliacionSatResponse>;

public sealed record DiferenciasConciliacionSatQuery(Guid ConciliacionId, TipoDiferenciaSat? Tipo, bool? Revisada)
    : IRequest<IReadOnlyList<DiferenciaSatResponse>>;

public sealed class ConciliacionSatHandlers(
    FacturacionDbContext db, IEmpresaFiscalReadPort empresasFiscales, IUsuariosReadPort usuarios, ICurrentEmpresaContext empresa, ICurrentUserContext user, IClock clock) :
    IRequestHandler<ConciliarCfdiSatCommand, ConciliacionSatResponse>,
    IRequestHandler<MarcarDiferenciaSatRevisadaCommand, DiferenciaSatResponse>,
    IRequestHandler<ConciliacionesSatQuery, IReadOnlyList<ConciliacionSatResponse>>,
    IRequestHandler<ConciliacionSatQuery, ConciliacionSatResponse>,
    IRequestHandler<DiferenciasConciliacionSatQuery, IReadOnlyList<DiferenciaSatResponse>>
{
    /// <summary>Estados con timbre que el SAT debe conocer.</summary>
    private static readonly EstadoTimbrado[] ConTimbre =
        [EstadoTimbrado.Timbrado, EstadoTimbrado.CancelacionPendiente, EstadoTimbrado.Cancelado];

    public async Task<ConciliacionSatResponse> Handle(ConciliarCfdiSatCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (empresaId, usuarioId) = ContextoRequerido.De(empresa, user);
        if (command.Contenido.Length == 0)
            throw new BusinessRuleException("CONCILIACION_SAT_ARCHIVO_VACIO", "El archivo del SAT está vacío.");
        var sat = LectorMetadataSat.Leer(command.Contenido);

        // El archivo debe ser de CFDI emitidos por la empresa (no el de recibidos): se compara contra el RFC
        // configurado de la empresa, no contra los emisores históricos de los timbrados (incluye RFC de sandbox).
        var rfcEmpresa = (await empresasFiscales.ObtenerAsync(empresaId, ct))?.Rfc?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(rfcEmpresa))
            throw new BusinessRuleException("CONCILIACION_SAT_EMPRESA_SIN_RFC",
                "La empresa no tiene RFC configurado; captúralo en Administración antes de conciliar.");
        var ajenos = sat.Select(s => s.RfcEmisor).Distinct().Where(r => r != rfcEmpresa).ToList();
        if (ajenos.Count > 0)
            throw new BusinessRuleException("CONCILIACION_SAT_OTRO_EMISOR",
                $"El archivo trae CFDI emitidos por {string.Join(", ", ajenos.Take(3))}, que no es el RFC de la empresa ({rfcEmpresa}). " +
                "Usa el archivo de CFDI emitidos, no el de recibidos.");

        // Periodo en la hora local por omisión de las sucursales (el CFDI se fecha en el lugar de expedición).
        var desde = DiaOperacion.FinDelDia(command.Desde.AddDays(-1), null);
        var hasta = DiaOperacion.FinDelDia(command.Hasta, null);
        var uuidsSat = sat.Select(s => s.Uuid).Distinct().ToList();
        // El PAC entrega el UUID en mayúsculas; por si alguno se guardó en minúsculas se buscan ambas formas.
        var variantesUuid = uuidsSat.Concat(uuidsSat.Select(u => u.ToLowerInvariant())).ToList();
        var erp = await db.Comprobantes.AsNoTracking()
            .Where(c => c.Uuid != null && ConTimbre.Contains(c.Estado)
                && ((c.FechaTimbrado >= desde && c.FechaTimbrado < hasta) || variantesUuid.Contains(c.Uuid!)))
            .Select(c => new { c.Id, c.Uuid, c.Folio, c.ReceptorRfc, c.Tipo, c.Total, c.Estado, c.FechaTimbrado })
            .ToListAsync(ct);
        var conciliables = erp.Select(c => new CfdiErpConciliable(c.Id, c.Uuid!, c.Folio, c.ReceptorRfc, c.Tipo, c.Total, c.Estado,
            c.FechaTimbrado >= desde && c.FechaTimbrado < hasta)).ToList();

        var diferencias = ComparadorConciliacionSat.Comparar(sat, conciliables);
        var corrida = Domain.ConciliacionSat.ConciliacionSat.Registrar(
            empresaId, command.Desde, command.Hasta, FuenteConciliacionSat.ArchivoMetadata, command.NombreArchivo,
            Convert.ToHexString(SHA256.HashData(command.Contenido)), usuarioId, clock.UtcNow,
            uuidsSat.Count, conciliables.Count(c => c.EnPeriodo), diferencias);
        db.ConciliacionesSat.Add(corrida);
        await db.SaveChangesAsync(ct);
        return Respuesta(corrida, corrida.Diferencias);
    }

    public async Task<DiferenciaSatResponse> Handle(MarcarDiferenciaSatRevisadaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (_, usuarioId) = ContextoRequerido.De(empresa, user);
        var d = await db.DiferenciasConciliacionSat
            .SingleOrDefaultAsync(x => x.Id == command.DiferenciaId && x.ConciliacionId == command.ConciliacionId, ct)
            ?? throw new EntityNotFoundException("CONCILIACION_SAT_DIFERENCIA_NO_ENCONTRADA", "No existe esa diferencia en la conciliación.");
        d.MarcarRevisada(usuarioId, clock.UtcNow, command.Nota);
        await db.SaveChangesAsync(ct);
        return (await RespuestasAsync([d], ct)).Single();
    }

    public async Task<IReadOnlyList<ConciliacionSatResponse>> Handle(ConciliacionesSatQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var limit = Math.Clamp(query.Limit, 1, 100);
        var corridas = await db.ConciliacionesSat.AsNoTracking()
            .OrderByDescending(c => c.EjecutadaEn).Take(limit).ToListAsync(ct);
        var ids = corridas.Select(c => c.Id).ToList();
        var diferencias = (await db.DiferenciasConciliacionSat.AsNoTracking()
                .Where(d => ids.Contains(d.ConciliacionId)).ToListAsync(ct))
            .ToLookup(d => d.ConciliacionId);
        return corridas.Select(c => Respuesta(c, diferencias[c.Id].ToList())).ToList();
    }

    public async Task<ConciliacionSatResponse> Handle(ConciliacionSatQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var c = await db.ConciliacionesSat.AsNoTracking().SingleOrDefaultAsync(x => x.Id == query.Id, ct)
            ?? throw new EntityNotFoundException("CONCILIACION_SAT_NO_ENCONTRADA", "No existe esa conciliación.");
        var diferencias = await db.DiferenciasConciliacionSat.AsNoTracking()
            .Where(d => d.ConciliacionId == c.Id).ToListAsync(ct);
        return Respuesta(c, diferencias);
    }

    public async Task<IReadOnlyList<DiferenciaSatResponse>> Handle(DiferenciasConciliacionSatQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var q = db.DiferenciasConciliacionSat.AsNoTracking().Where(d => d.ConciliacionId == query.ConciliacionId);
        if (query.Tipo is { } tipo) q = q.Where(d => d.Tipo == tipo);
        if (query.Revisada is { } revisada) q = q.Where(d => d.Revisada == revisada);
        var lista = await q.OrderBy(d => d.Tipo).ThenBy(d => d.Folio).ThenBy(d => d.Uuid).ToListAsync(ct);
        return await RespuestasAsync(lista, ct);
    }

    private static ConciliacionSatResponse Respuesta(Domain.ConciliacionSat.ConciliacionSat c, IReadOnlyCollection<DiferenciaConciliacionSat> diferencias) =>
        new(c.Id, c.Desde, c.Hasta, c.Fuente, c.NombreArchivo, c.EjecutadaEn, c.EjecutadaPor, c.CfdiSat, c.CfdiErp,
            c.Coincidentes, c.ConDiferencias, c.SinDiferencias, diferencias.Count(d => !d.Revisada),
            diferencias.GroupBy(d => d.Tipo).OrderBy(g => g.Key)
                .Select(g => new ConteoDiferenciaSat(g.Key, g.Count(), g.Count(d => d.Revisada))).ToList());

    /// <summary>
    /// Completa cada diferencia con el nombre de quien la revisó y la moneda del CFDI del ERP. Los montos del
    /// renglón van en la moneda del CFDI: se supone que el <c>Monto</c> del archivo del SAT también (a validar con
    /// un archivo real); un CFDI que solo está en el SAT no trae moneda en los metadatos y queda sin ella.
    /// </summary>
    private async Task<IReadOnlyList<DiferenciaSatResponse>> RespuestasAsync(
        IReadOnlyCollection<DiferenciaConciliacionSat> lista, CancellationToken ct)
    {
        var revisores = lista.Where(d => d.RevisadaPor != null).Select(d => d.RevisadaPor!.Value).Distinct().ToList();
        var nombres = revisores.Count == 0 ? new Dictionary<Guid, string>() : await usuarios.ObtenerNombresAsync(revisores, ct);
        var comprobantes = lista.Where(d => d.ComprobanteId != null).Select(d => d.ComprobanteId!.Value).Distinct().ToList();
        var monedas = comprobantes.Count == 0 ? new Dictionary<Guid, string>()
            : await db.Comprobantes.AsNoTracking().Where(c => comprobantes.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Moneda, ct);
        return lista.Select(d => new DiferenciaSatResponse(d.Id, d.Uuid, d.Tipo, d.ComprobanteId, d.Folio, d.RfcReceptor,
            d.ValorSat, d.ValorErp, d.Revisada, d.RevisadaPor, d.RevisadaEn, d.Nota,
            d.RevisadaPor is { } u && nombres.TryGetValue(u, out var nombre) ? nombre : null,
            d.ComprobanteId is { } cid && monedas.TryGetValue(cid, out var moneda) ? moneda : null)).ToList();
    }
}
