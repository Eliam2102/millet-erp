using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Cargas;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Cargas;
using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.Compartido.Application.Cargas.Lotes;

public sealed record TipoCargaResponse(string Tipo, string Nombre, IReadOnlyList<ColumnaTipoCarga> Columnas, bool RequiereFechaCorte,
    bool PuedeSubir, bool PuedeAutorizar);
public sealed record ColumnaTipoCarga(string Nombre, string Ayuda, bool Obligatoria, IReadOnlyList<string> Alias);
public sealed record EncabezadosResponse(IReadOnlyList<string> Encabezados, IReadOnlyDictionary<string, string> MapeoSugerido);
public sealed record LoteCargaResumen(Guid Id, string Tipo, string NombreArchivo, string Estado, DateOnly? FechaCorte, int TotalFilas,
    int TotalErrores, bool PuedeAplicar, string SubidoPor, DateTimeOffset SubidoEn, string? DecididoPor, DateTimeOffset? DecididoEn,
    string? FolioResultado, string? UltimoError);
public sealed record FilaLoteDetalle(int Fila, IReadOnlyDictionary<string, string> Valores, IReadOnlyList<ErrorCarga> Errores);
public sealed record HistorialLote(string Accion, string Usuario, DateTimeOffset En, string? Detalle);
public sealed record LoteCargaDetalle(LoteCargaResumen Lote, string Huella, IReadOnlyDictionary<string, string> Mapeo,
    IReadOnlyDictionary<string, string> Resumen, IReadOnlyList<ErrorCarga> ErroresArchivo, IReadOnlyList<FilaLoteDetalle> Filas,
    int FilasMostradas, string? MotivoRechazo, string? Referencia, IReadOnlyList<HistorialLote> Historial,
    bool PuedeAutorizar, bool PuedeRechazar, bool PuedeReintentar);

/// <summary>
/// M-047 + M-167 (86e3jvvmf): lote común de cargas. Subir (huella, mapeo, filas, validación del módulo dueño),
/// autorizar por otra persona (aplica en el mismo paso), rechazar con motivo y reintentar la aplicación si el módulo
/// la detuvo. Cada paso queda en la bitácora del lote. Un mismo archivo (huella) del mismo tipo no abre otro lote
/// mientras haya uno sin rechazar: regresa ese.
/// </summary>
public sealed class LotesCargaServicio(
    CompartidoDbContext db,
    IEnumerable<IProcesadorCarga> procesadores,
    ICurrentUserContext usuario,
    ICurrentUserPermissions permisos,
    ICurrentEmpresaContext empresa,
    IClock reloj)
{
    public const int FilasEnDetalle = 500;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<TipoCargaResponse>> TiposAsync(CancellationToken ct)
    {
        var lista = new List<TipoCargaResponse>();
        foreach (var p in procesadores.OrderBy(p => p.Nombre))
        {
            var subir = await permisos.TieneAsync(p.PermisoSubir, ct);
            var autorizar = await permisos.TieneAsync(p.PermisoAutorizar, ct);
            if (!subir && !autorizar) continue;
            lista.Add(new(p.Tipo, p.Nombre, [.. p.Columnas.Select(c => new ColumnaTipoCarga(c.Nombre, c.Ayuda,
                p.ColumnasObligatorias.Contains(c.Nombre), c.Alias))], p.RequiereFechaCorte, subir, autorizar));
        }
        return lista;
    }

    public async Task<EncabezadosResponse> EncabezadosAsync(string tipo, byte[] archivo, string nombre, CancellationToken ct)
    {
        var p = Procesador(tipo);
        await ExigirAsync(p.PermisoSubir, ct);
        var encabezados = ArchivoMaestros.Encabezados(archivo, nombre);
        // Sugerencia: el encabezado que coincide con el nombre o un alias de la columna.
        var sugerido = new Dictionary<string, string>();
        foreach (var e in encabezados.Where(e => e.Length > 0))
        {
            var col = p.Columnas.FirstOrDefault(c => c.Alias.Append(c.Nombre)
                .Any(a => ArchivoMaestros.Normalizar(a) == ArchivoMaestros.Normalizar(e)));
            if (col is not null && !sugerido.ContainsValue(col.Nombre)) sugerido[e] = col.Nombre;
        }
        return new(encabezados, sugerido);
    }

    public async Task<LoteCargaDetalle> SubirAsync(string tipo, byte[] archivo, string nombre, DateOnly? fechaCorte,
        IReadOnlyDictionary<string, string> mapeo, CancellationToken ct)
    {
        var p = Procesador(tipo);
        await ExigirAsync(p.PermisoSubir, ct);
        var empresaId = empresa.Current ?? throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "Seleccione una empresa.");
        if (p.RequiereFechaCorte && fechaCorte is null)
            throw new BusinessRuleException("LOTE_CARGA_FECHA_CORTE", "Capture la fecha de corte del archivo.");
        var faltan = p.ColumnasObligatorias.Where(c => !mapeo.Values.Contains(c)).ToList();
        if (faltan.Count > 0)
            throw new BusinessRuleException("LOTE_CARGA_MAPEO_INCOMPLETO",
                $"Elija la columna del archivo para: {string.Join(", ", faltan)}.");

        var huella = Convert.ToHexString(SHA256.HashData(archivo));
        var existente = await db.LotesCarga.FirstOrDefaultAsync(l => l.Tipo == tipo && l.Huella == huella
            && l.FechaCorte == fechaCorte && l.Estado != EstadoLoteCarga.Rechazado, ct);
        if (existente is not null) return await DetalleAsync(existente, p, ct);

        var tabla = ArchivoMaestros.Leer(archivo, nombre, p.Columnas, mapeo);
        var filas = tabla.Filas.Select(f => new FilaCarga(f.Numero, f.Valores)).ToList();
        var (actorId, actorNombre) = Actor();
        var lote = new LoteCarga(Guid.CreateVersion7(), empresaId, tipo, Path.GetFileName(nombre), huella, fechaCorte,
            JsonSerializer.Serialize(mapeo, Json), JsonSerializer.Serialize(filas, Json), filas.Count, actorId, actorNombre, reloj.UtcNow);
        var erroresLectura = tabla.Errores.Select(e => new ErrorCarga(e.Renglon <= 1 ? null : e.Renglon, e.Columna, e.Codigo, e.Mensaje)).ToList();
        await ValidarAsync(lote, p, filas, erroresLectura, ct);
        Historial(lote, "Subido", $"{filas.Count} renglones, {lote.TotalErrores} errores");
        db.LotesCarga.Add(lote);
        await db.SaveChangesAsync(ct);
        return await DetalleAsync(lote, p, ct);
    }

    public async Task<IReadOnlyList<LoteCargaResumen>> ListarAsync(string tipo, CancellationToken ct)
    {
        var p = Procesador(tipo);
        if (!await permisos.TieneAsync(p.PermisoSubir, ct)) await ExigirAsync(p.PermisoAutorizar, ct);
        var lotes = await db.LotesCarga.AsNoTracking().Where(l => l.Tipo == tipo)
            .OrderByDescending(l => l.SubidoEn).Take(100).ToListAsync(ct);
        return [.. lotes.Select(Resumen)];
    }

    public async Task<LoteCargaDetalle> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var lote = await Cargar(id, ct);
        var p = Procesador(lote.Tipo);
        if (!await permisos.TieneAsync(p.PermisoSubir, ct)) await ExigirAsync(p.PermisoAutorizar, ct);
        return await DetalleAsync(lote, p, ct);
    }

    /// <summary>Otra persona autoriza; se revalida contra los datos de hoy y se aplica en el mismo paso.</summary>
    public async Task<LoteCargaDetalle> AutorizarAsync(Guid id, CancellationToken ct)
    {
        var lote = await Cargar(id, ct);
        var p = Procesador(lote.Tipo);
        await ExigirAsync(p.PermisoAutorizar, ct);
        var (actorId, actorNombre) = Actor();
        if (actorId == lote.SubidoPorId)
            throw new BusinessRuleException("LOTE_CARGA_MISMA_PERSONA", "Debe autorizar otra persona: quien subió el lote no puede autorizarlo.");
        var filas = Filas(lote);
        await ValidarAsync(lote, p, filas, [], ct);
        if (!lote.PuedeAplicar)
        {
            Historial(lote, "Validado al autorizar", $"{lote.TotalErrores} errores; no se autorizó");
            await db.SaveChangesAsync(ct);
            throw new BusinessRuleException("LOTE_CARGA_CON_ERRORES", "El lote tiene errores de validación; corríjalos y suba el archivo de nuevo.");
        }
        lote.Autorizar(actorId, actorNombre, reloj.UtcNow);
        Historial(lote, "Autorizado", null);
        await db.SaveChangesAsync(ct);
        await AplicarAsync(lote, p, filas, ct);
        return await DetalleAsync(lote, p, ct);
    }

    /// <summary>Reintenta la aplicación de un lote autorizado que el módulo detuvo.</summary>
    public async Task<LoteCargaDetalle> ReintentarAsync(Guid id, CancellationToken ct)
    {
        var lote = await Cargar(id, ct);
        var p = Procesador(lote.Tipo);
        await ExigirAsync(p.PermisoAutorizar, ct);
        if (lote.Estado != EstadoLoteCarga.Autorizado)
            throw new ConflictException("LOTE_CARGA_ESTADO", $"El lote está {lote.Estado}; esta acción ya no aplica.");
        await AplicarAsync(lote, p, Filas(lote), ct);
        return await DetalleAsync(lote, p, ct);
    }

    public async Task<LoteCargaDetalle> RechazarAsync(Guid id, string motivo, CancellationToken ct)
    {
        var lote = await Cargar(id, ct);
        var p = Procesador(lote.Tipo);
        await ExigirAsync(p.PermisoAutorizar, ct);
        var (actorId, actorNombre) = Actor();
        lote.Rechazar(actorId, actorNombre, motivo, reloj.UtcNow);
        Historial(lote, "Rechazado", lote.MotivoRechazo);
        await db.SaveChangesAsync(ct);
        return await DetalleAsync(lote, p, ct);
    }

    // ─── Internos ───────────────────────────────────────────────────────────

    private async Task AplicarAsync(LoteCarga lote, IProcesadorCarga p, IReadOnlyList<FilaCarga> filas, CancellationToken ct)
    {
        try
        {
            var r = await p.AplicarAsync(Contexto(lote), filas, ct);
            lote.MarcarAplicado(r.Referencia, r.Folio, reloj.UtcNow);
            Historial(lote, r.YaAplicada ? "Aplicado (ya estaba aplicado)" : "Aplicado", r.Folio);
        }
        catch (DomainException e) when (e is BusinessRuleException or ConflictException)
        {
            // El módulo detuvo la aplicación: el lote sigue Autorizado con el motivo, para corregir y reintentar.
            lote.RegistrarErrorAplicacion(e.Message);
            Historial(lote, "Aplicación detenida", e.Message);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task ValidarAsync(LoteCarga lote, IProcesadorCarga p, IReadOnlyList<FilaCarga> filas,
        List<ErrorCarga> erroresLectura, CancellationToken ct)
    {
        var v = erroresLectura.Count > 0
            ? new ValidacionCarga(false, erroresLectura, new Dictionary<string, string>())
            : await p.ValidarAsync(Contexto(lote), filas, ct);
        lote.RegistrarValidacion(JsonSerializer.Serialize(v, Json), v.PuedeAplicar && v.Errores.Count == 0, v.Errores.Count);
    }

    private async Task<LoteCargaDetalle> DetalleAsync(LoteCarga lote, IProcesadorCarga p, CancellationToken ct)
    {
        var v = JsonSerializer.Deserialize<ValidacionCarga>(lote.ValidacionJson, Json)
            ?? new ValidacionCarga(false, [], new Dictionary<string, string>());
        var porFila = v.Errores.Where(e => e.Fila is not null).ToLookup(e => e.Fila!.Value);
        var filas = Filas(lote);
        // Primero los renglones con error, luego el resto, hasta el límite de la pantalla.
        var mostradas = filas.OrderBy(f => porFila[f.Fila].Any() ? 0 : 1).ThenBy(f => f.Fila).Take(FilasEnDetalle)
            .OrderBy(f => f.Fila).Select(f => new FilaLoteDetalle(f.Fila, f.Valores, [.. porFila[f.Fila]])).ToList();
        var (actorId, _) = Actor();
        var autoriza = await permisos.TieneAsync(p.PermisoAutorizar, ct);
        return new(Resumen(lote), lote.Huella,
            JsonSerializer.Deserialize<Dictionary<string, string>>(lote.MapeoJson, Json) ?? [],
            v.Resumen, [.. v.Errores.Where(e => e.Fila is null)], mostradas, mostradas.Count, lote.MotivoRechazo, lote.Referencia,
            JsonSerializer.Deserialize<List<HistorialLote>>(lote.HistorialJson, Json) ?? [],
            PuedeAutorizar: autoriza && lote.Estado == EstadoLoteCarga.Subido && lote.PuedeAplicar && actorId != lote.SubidoPorId,
            PuedeRechazar: autoriza && lote.Estado is EstadoLoteCarga.Subido or EstadoLoteCarga.Autorizado,
            PuedeReintentar: autoriza && lote.Estado == EstadoLoteCarga.Autorizado);
    }

    private static LoteCargaResumen Resumen(LoteCarga l) => new(l.Id, l.Tipo, l.NombreArchivo, l.Estado.ToString(), l.FechaCorte,
        l.TotalFilas, l.TotalErrores, l.PuedeAplicar, l.SubidoPorNombre, l.SubidoEn, l.DecididoPorNombre, l.DecididoEn,
        l.FolioResultado, l.UltimoError);

    private static List<FilaCarga> Filas(LoteCarga lote) =>
        JsonSerializer.Deserialize<List<FilaCarga>>(lote.FilasJson, Json) ?? [];

    private static LoteCargaContexto Contexto(LoteCarga l) =>
        new(l.Id, l.EmpresaId, l.Huella, l.FechaCorte, l.SubidoPorId, l.DecididoPorId);

    private void Historial(LoteCarga lote, string accion, string? detalle)
    {
        var lista = JsonSerializer.Deserialize<List<HistorialLote>>(lote.HistorialJson, Json) ?? [];
        lista.Add(new(accion, Actor().Nombre, reloj.UtcNow, detalle));
        lote.AgregarHistorial(JsonSerializer.Serialize(lista, Json));
    }

    private async Task<LoteCarga> Cargar(Guid id, CancellationToken ct) =>
        await db.LotesCarga.FirstOrDefaultAsync(l => l.Id == id, ct)
        ?? throw new EntityNotFoundException("LOTE_CARGA_NO_ENCONTRADO", "El lote de carga no existe.");

    private IProcesadorCarga Procesador(string tipo) => procesadores.SingleOrDefault(p => p.Tipo == tipo)
        ?? throw new EntityNotFoundException("LOTE_CARGA_TIPO_NO_EXISTE", $"El tipo de carga «{tipo}» no existe.");

    private async Task ExigirAsync(string permiso, CancellationToken ct)
    {
        if (!await permisos.TieneAsync(permiso, ct))
            throw new ForbiddenException("LOTE_CARGA_SIN_PERMISO", "No tiene permiso para esta carga.");
    }

    private (Guid Id, string Nombre) Actor() =>
        (usuario.UserId ?? throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se identificó al usuario."),
         usuario.UserName ?? usuario.Email ?? "Usuario");
}
