using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Contabilidad.Application.Catalogo;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Importacion;

public sealed record FilaResultado(int Fila, string Accion, IReadOnlyList<ErrorFila> Errores);

public sealed record ResumenImportacion(
    int Leidas, int Vacias, int Crear, int Actualizar, int SinCambios, int Rechazadas, int Errores, int Advertencias, int Omitidas);

public sealed record VistaPreviaResponse(
    ResumenImportacion Resumen, string Huella, bool PuedeAplicar, IReadOnlyList<ErrorFila> Archivo, IReadOnlyList<FilaResultado> Filas);

public sealed record AplicarImportacionResultado(
    bool Aplicado, bool Idempotente, ImportacionLoteDto? Lote, IReadOnlyList<ErrorFila> Errores, Guid? SolicitudId = null);

/// <summary>Carga (solo lectura) y análisis comunes a vista previa, perfilado y aplicar.</summary>
internal static class AnalisisImportacion
{
    public static async Task<(ResultadoAnalisis Resultado, FormatoCatalogo Formato)> EjecutarAsync(
        ContabilidadDbContext db, FormatoCatalogo formato, ImportacionRequest request, CancellationToken cancellationToken)
    {
        var tabla = LectorTabla.Leer(request);
        var cuentas = await db.Cuentas.AsNoTracking().ToListAsync(cancellationToken);
        var porId = cuentas.ToDictionary(c => c.Id, c => c.Codigo);
        var usadas = (await db.Usos.AsNoTracking().Select(u => u.CuentaId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var origenes = (await db.Origenes.AsNoTracking().ToListAsync(cancellationToken))
            .Where(o => porId.ContainsKey(o.CuentaId))
            .ToDictionary(o => (o.Fuente, o.CodigoOrigen), o => porId[o.CuentaId]);
        var ex = new ExistenteCatalogo(
            [.. cuentas.Select(c => new CuentaExistente(c.Id, c.Codigo, c.Nombre, c.PadreId is { } p ? porId[p] : null,
                c.Naturaleza, c.Tipo, c.CuentaControl, c.CodigoAgrupador, c.GrupoReporte, c.Activa, usadas.Contains(c.Id),
                c.Clase, c.RubroId is { } r ? porId[r] : null, c.NoAfectableManual))],
            origenes);
        var fuente = FormatoCatalogo.Texto(request.Fuente)?.ToUpperInvariant();
        return (new ImportadorCatalogo(formato).Analizar(tabla, fuente, ex), formato);
    }
}

// ─── Vista previa (no escribe) ───────────────────────────────────────────────

public sealed record VistaPreviaImportacionCommand(ImportacionRequest Cuerpo) : IRequest<VistaPreviaResponse>;

public sealed class VistaPreviaImportacionHandler(
    ContabilidadDbContext db, FormatoCatalogo formato, ILogger<VistaPreviaImportacionHandler> log)
    : IRequestHandler<VistaPreviaImportacionCommand, VistaPreviaResponse>
{
    public async Task<VistaPreviaResponse> Handle(VistaPreviaImportacionCommand request, CancellationToken cancellationToken)
    {
        var (a, _) = await AnalisisImportacion.EjecutarAsync(db, formato, request.Cuerpo, cancellationToken);
        var hallazgos = a.Hallazgos.ToList();
        var resumen = new ResumenImportacion(
            a.Filas.Count + a.Vacias, a.Vacias,
            a.Filas.Count(f => f.Accion == Accion.Crear), a.Filas.Count(f => f.Accion == Accion.Actualizar),
            a.Filas.Count(f => f.Accion == Accion.SinCambios), a.Filas.Count(f => f.Accion == Accion.Rechazar),
            hallazgos.Count(h => h.Severidad == "Error"), hallazgos.Count(h => h.Severidad == "Advertencia"),
            a.Filas.Count(f => f.Accion == Accion.Omitir));
        log.LogInformation("Vista previa de catálogo: filas={Filas} errores={Errores} advertencias={Advertencias}",
            resumen.Leidas, resumen.Errores, resumen.Advertencias);
        return new VistaPreviaResponse(resumen, a.Huella, a.PuedeAplicar, a.Archivo,
            [.. a.Filas.Select(f => new FilaResultado(f.Fila, f.Accion, f.Errores))]);
    }
}

// ─── Perfilado (solo lectura, §20.3) ─────────────────────────────────────────

public sealed record PerfilarImportacionCommand(ImportacionRequest Cuerpo) : IRequest<PerfilImportacion>;

public sealed class PerfilarImportacionHandler(
    ContabilidadDbContext db, FormatoCatalogo formato, ILogger<PerfilarImportacionHandler> log)
    : IRequestHandler<PerfilarImportacionCommand, PerfilImportacion>
{
    public async Task<PerfilImportacion> Handle(PerfilarImportacionCommand request, CancellationToken cancellationToken)
    {
        var (a, _) = await AnalisisImportacion.EjecutarAsync(db, formato, request.Cuerpo, cancellationToken);
        log.LogInformation("Perfilado de catálogo: filas={Filas} hallazgos={Hallazgos}", a.Filas.Count, a.Hallazgos.Count());
        return new Perfilador(formato).Perfilar(a);
    }
}

// ─── Aplicar (transaccional e idempotente) ───────────────────────────────────

public sealed record AplicarImportacionCommand(ImportacionRequest Cuerpo) : IRequest<AplicarImportacionResultado>;

public sealed class AplicarImportacionHandler(
    ContabilidadDbContext db, FormatoCatalogo formato, ICurrentUserContext usuario, IClock clock,
    ILogger<AplicarImportacionHandler> log, SolicitudesCatalogo solicitudes)
    : IRequestHandler<AplicarImportacionCommand, AplicarImportacionResultado>
{
    public async Task<AplicarImportacionResultado> Handle(AplicarImportacionCommand request, CancellationToken cancellationToken)
    {
        var (a, _) = await AnalisisImportacion.EjecutarAsync(db, formato, request.Cuerpo, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Cuerpo.Huella) && !string.Equals(request.Cuerpo.Huella, a.Huella, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("CONTAB_IMPORT_HUELLA_NO_COINCIDE",
                "El contenido cambió entre la vista previa y la aplicación; vuelva a generar la vista previa.");

        var previo = await LoteAsync(a.Huella, cancellationToken);
        if (previo is not null) return new(false, true, previo, []);
        if (!a.PuedeAplicar)
        {
            log.LogInformation("Importación de catálogo rechazada: errores={Errores}", a.Hallazgos.Count(h => h.Severidad == "Error"));
            return new(false, false, null, [.. a.Hallazgos.Where(h => h.Severidad == "Error")]);
        }
        var pendiente = await db.SolicitudesCatalogo.AsNoTracking().FirstOrDefaultAsync(s => s.HuellaImportacion == a.Huella && s.Estado == "Pendiente", cancellationToken);
        if (pendiente is not null) return new(false, false, null, [], pendiente.Id);
        var (resultado, solicitudId) = await solicitudes.PrepararAsync(request,
            () => PersistirAsync(a, request.Cuerpo, cancellationToken), cancellationToken, a.Huella);
        return resultado with { Aplicado = false, Lote = null, SolicitudId = solicitudId };
    }

    /// <summary>Una sola transacción (un SaveChanges). Visible para pruebas con análisis "viejo" (carrera).</summary>
    internal async Task<AplicarImportacionResultado> PersistirAsync(ResultadoAnalisis a, ImportacionRequest request, CancellationToken cancellationToken, IReadOnlyDictionary<string, Guid>? idsPropuestos = null)
    {
        var entidades = await db.Cuentas.ToDictionaryAsync(x => x.Codigo, cancellationToken);
        var ids = entidades.ToDictionary(e => e.Key, e => e.Value.Id);
        var lote = new ImportacionCatalogo(Guid.CreateVersion7(),
            a.Filas.Select(f => f.Fuente).FirstOrDefault() ?? "ARCHIVO", FormatoCatalogo.Texto(request.ArchivoNombre), a.Huella,
            a.Filas.Count, a.Filas.Count(f => f.Accion == Accion.Crear), a.Filas.Count(f => f.Accion == Accion.Actualizar),
            a.Filas.Count(f => f.Accion == Accion.SinCambios), clock.UtcNow, usuario.UserName ?? usuario.Email);

        foreach (var f in a.Filas.Where(f => f.Accion == Accion.Crear)) ids[f.Codigo!] = idsPropuestos?.GetValueOrDefault(f.Codigo!) ?? Guid.CreateVersion7();
        var enArchivo = a.Filas.Select(f => f.Codigo!).ToHashSet();
        foreach (var f in a.Filas.Where(f => f.Accion is Accion.Crear or Accion.Actualizar))
        {
            Guid? padre = f.PadreCodigo is null ? null : ids[f.PadreCodigo];
            var cuenta = f.Accion == Accion.Crear
                ? db.Cuentas.Add(new CuentaContable(ids[f.Codigo!], f.Codigo!, f.Nombre!, padre, f.Nivel, f.Naturaleza, f.Tipo,
                    f.Control, f.Agrupador, f.Grupo, f.Clase, f.NoAfectableManual)).Entity
                : entidades[f.Codigo!];
            if (f.Accion == Accion.Actualizar) cuenta.Editar(f.Nombre!, padre, f.Nivel, f.Naturaleza, f.Tipo, f.Control, f.Agrupador, f.Grupo, f.NoAfectableManual);
            cuenta.AsignarRubro(f.RubroCodigo is null ? null : ids[f.RubroCodigo]);
        }
        // P20: cuentas existentes afectables que reciben hijas en este archivo pasan a acumular.
        foreach (var codigo in a.PadresAConvertir) entidades[codigo].ConvertirEnAcumulativa();
        foreach (var f in a.Filas.Where(f => f.OrigenNuevo && f.Accion != Accion.Rechazar))
            db.Origenes.Add(new CuentaContableOrigen(Guid.CreateVersion7(), ids[f.Codigo!], f.Fuente, f.CodigoOrigen!, lote.Id));
        // Niveles de descendientes existentes que no vienen en el archivo pero cambiaron de padre.
        foreach (var (codigo, entidad) in entidades.Where(e => !enArchivo.Contains(e.Key)))
            if (a.NivelesCalculados.TryGetValue(codigo, out var nivel) && nivel > 0 && nivel != entidad.Nivel) entidad.FijarNivel(nivel);
        db.Importaciones.Add(lote);

        return new(true, false, Dto(lote), []);
    }

    private async Task<ImportacionLoteDto?> LoteAsync(string huella, CancellationToken cancellationToken)
    {
        var l = await db.Importaciones.AsNoTracking().FirstOrDefaultAsync(x => x.HuellaSha256 == huella, cancellationToken);
        return l is null ? null : Dto(l);
    }

    private static ImportacionLoteDto Dto(ImportacionCatalogo l) => new(
        l.Id, l.Fuente, l.ArchivoNombre, l.HuellaSha256, l.TotalFilas, l.Creadas, l.Actualizadas, l.SinCambios, l.AplicadoEn, l.AplicadoPor);
}
