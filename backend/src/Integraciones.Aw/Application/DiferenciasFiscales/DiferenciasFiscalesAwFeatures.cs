using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Application.DiferenciasFiscales;

/// <summary>
/// Fila del reporte. <c>Tipo</c>, <c>EntidadId</c>, <c>Campo</c> y <c>Huella</c> son para las acciones de la UI
/// (no son columnas). <c>Fuente</c> dice contra qué se comparó: "A+W" en clientes; regla o mapeo en productos.
/// </summary>
public sealed record FilaDiferenciaFiscalAw(
    string Tipo,
    string TipoEtiqueta,
    Guid EntidadId,
    string Referencia,
    string Nombre,
    string Campo,
    string CampoEtiqueta,
    string Fuente,
    string? ValorOrigen,
    string? ValorErp,
    string Estado,
    string EstadoEtiqueta,
    string Huella,
    bool Revisada,
    DateTimeOffset? RevisadaEn,
    string? RevisadaPor,
    string? Nota);

public sealed record CorridaDiferenciasFiscalesAwResumen(
    Guid Id, string Origen, string Actor, DateTimeOffset IniciadaEn, DateTimeOffset? TerminadaEn, bool Exitosa,
    int ClientesComparados, int ProductosComparados, int Diferencias, string? Error, IReadOnlyList<string> Avisos);

public sealed record UltimaCorridaDiferenciasFiscalesAw(
    CorridaDiferenciasFiscalesAwResumen? Corrida,
    ReporteResponse<FilaDiferenciaFiscalAw>? Reporte,
    CorridaDiferenciasFiscalesAwResumen? UltimaProgramada);

public sealed record RevisionDiferenciaFiscalAwDto(
    Guid Id, string Tipo, Guid EntidadId, string Campo, string Huella, DateTimeOffset RevisadoEn, string? RevisadoPor, int Version);

/// <summary>Armado del reporte (ADR-0036) a partir de diferencias + revisiones. Compartido por vivo y corrida.</summary>
internal static class ReporteDiferenciasFiscalesAw
{
    public const string Titulo = "Diferencias fiscales A+W vs ERP";

    public static FilaDiferenciaFiscalAw Fila(DiferenciaFiscalDetectada d, DiferenciaFiscalAwRevision? r)
    {
        var revisada = r is not null && r.Huella == d.Huella;
        return new(d.Tipo.ToString(), TipoEtiqueta(d.Tipo), d.EntidadId, d.Referencia, d.Nombre, d.Campo,
            CamposDiferenciaFiscal.Etiqueta(d.Campo), d.Fuente, d.ValorOrigen, d.ValorErp, d.Estado.ToString(),
            EstadoDiferenciaFiscalTexto.Etiqueta(d.Estado), d.Huella, revisada,
            revisada ? r!.RevisadoEn : null, revisada ? r!.RevisadoPorNombre : null, revisada ? r!.Nota : null);
    }

    public static string TipoEtiqueta(TipoEntidadDiferenciaFiscal t) =>
        t == TipoEntidadDiferenciaFiscal.Cliente ? "Cliente" : "Producto A+W";

    public static ReporteResponse<FilaDiferenciaFiscalAw> Construir(
        DateTimeOffset generadoEn, IReadOnlyDictionary<string, string?> filtros, IReadOnlyList<FilaDiferenciaFiscalAw> todas,
        bool incluirRevisadas, IReadOnlyList<string> avisos, int clientes, int productos)
    {
        var columnas = new List<ColumnaDescriptor>
        {
            new("tipoEtiqueta", "Tipo", "texto", AnchoPx: 110),
            new("referencia", "Clave A+W", "texto", AnchoPx: 100),
            new("nombre", "Nombre", "texto"),
            new("campoEtiqueta", "Campo", "texto", AnchoPx: 150),
            new("fuente", "Comparado contra", "texto", AnchoPx: 190),
            new("valorOrigen", "Valor A+W o regla", "texto"),
            new("valorErp", "Valor en el ERP", "texto"),
            new("estadoEtiqueta", "Estado", "texto", AnchoPx: 120),
        };
        if (incluirRevisadas)
        {
            columnas.Add(new("revisadaEn", "Revisada el", "fecha", AnchoPx: 110));
            columnas.Add(new("revisadaPor", "Revisada por", "texto", AnchoPx: 160));
        }
        var filas = todas.Where(f => incluirRevisadas || !f.Revisada)
            .OrderBy(f => f.Tipo).ThenBy(f => f.Referencia, StringComparer.Ordinal).ThenBy(f => f.Campo).ToList();
        var revisadas = todas.Count(f => f.Revisada);
        return new(Titulo, generadoEn, filtros, columnas, filas, null, avisos,
            new ResumenDiferenciasFiscales(clientes, productos, todas.Count - revisadas, revisadas));
    }

    public static CorridaDiferenciasFiscalesAwResumen Resumen(DiferenciaFiscalAwCorrida c) => new(
        c.Id, c.Origen.ToString(), c.Actor, c.IniciadaEn, c.TerminadaEn, c.Exitosa, c.ClientesComparados,
        c.ProductosComparados, c.Diferencias, c.Error, c.Avisos?.Split('\n') ?? []);

    public static TipoEntidadDiferenciaFiscal? Tipo(string? v) =>
        string.IsNullOrWhiteSpace(v) ? null : Enum.Parse<TipoEntidadDiferenciaFiscal>(v, ignoreCase: true);
}

// ───────────── Reporte en vivo ─────────────

public sealed record ObtenerDiferenciasFiscalesAwQuery(string? Tipo, string? Campo, string? Estado, bool IncluirRevisadas)
    : IRequest<ReporteResponse<FilaDiferenciaFiscalAw>>;

public sealed class ObtenerDiferenciasFiscalesAwValidator : AbstractValidator<ObtenerDiferenciasFiscalesAwQuery>
{
    public ObtenerDiferenciasFiscalesAwValidator()
    {
        RuleFor(q => q.Tipo).Must(t => t is null || Enum.TryParse<TipoEntidadDiferenciaFiscal>(t, true, out _))
            .WithMessage("El tipo debe ser Cliente o Producto.");
        RuleFor(q => q.Campo).Must(c => c is null || CamposDiferenciaFiscal.DeCliente.Contains(c) || CamposDiferenciaFiscal.DeProducto.Contains(c))
            .WithMessage("El campo no es uno de los campos fiscales comparados.");
        RuleFor(q => q.Estado).Must(e => e is null || Enum.TryParse<EstadoDiferenciaFiscal>(e, true, out _))
            .WithMessage("El estado debe ser Diferente, Faltante o SinDatoEnOrigen.");
    }
}

public sealed class ObtenerDiferenciasFiscalesAwHandler(DiferenciasFiscalesAwService servicio, TimeProvider time)
    : IRequestHandler<ObtenerDiferenciasFiscalesAwQuery, ReporteResponse<FilaDiferenciaFiscalAw>>
{
    public async Task<ReporteResponse<FilaDiferenciaFiscalAw>> Handle(ObtenerDiferenciasFiscalesAwQuery request, CancellationToken cancellationToken)
    {
        var tipo = ReporteDiferenciasFiscalesAw.Tipo(request.Tipo);
        var estado = string.IsNullOrWhiteSpace(request.Estado) ? (EstadoDiferenciaFiscal?)null : Enum.Parse<EstadoDiferenciaFiscal>(request.Estado, true);
        var resultado = await servicio.CompararAsync(new(tipo, request.Campo), cancellationToken);
        var revisiones = await servicio.RevisionesAsync(cancellationToken);
        var filas = resultado.Diferencias
            .Where(d => estado is null || d.Estado == estado)
            .Select(d => ReporteDiferenciasFiscalesAw.Fila(d, revisiones.GetValueOrDefault((d.Tipo, d.EntidadId, d.Campo))))
            .ToList();
        var filtros = new Dictionary<string, string?>
        {
            ["Tipo"] = tipo is null ? "Todos" : ReporteDiferenciasFiscalesAw.TipoEtiqueta(tipo.Value),
            ["Campo"] = request.Campo is null ? "Todos" : CamposDiferenciaFiscal.Etiqueta(request.Campo),
            ["Estado"] = estado is null ? "Todos" : EstadoDiferenciaFiscalTexto.Etiqueta(estado.Value),
            ["Revisadas"] = request.IncluirRevisadas ? "Incluidas" : "Ocultas",
        };
        return ReporteDiferenciasFiscalesAw.Construir(time.GetUtcNow(), filtros, filas, request.IncluirRevisadas,
            resultado.Avisos, resultado.ClientesComparados, resultado.ProductosComparados);
    }
}

// ───────────── Marcar revisado ─────────────

/// <summary>
/// Marca revisada la diferencia vigente de (tipo, entidad, campo). <c>Huella</c> es la que el usuario vio: si
/// los valores cambiaron desde entonces, se rechaza (409) para no revisar algo distinto de lo que se leyó.
/// </summary>
public sealed record MarcarDiferenciaFiscalAwRevisadaCommand(string Tipo, Guid EntidadId, string Campo, string Huella, string? Nota)
    : IRequest<RevisionDiferenciaFiscalAwDto>;

public sealed class MarcarDiferenciaFiscalAwRevisadaValidator : AbstractValidator<MarcarDiferenciaFiscalAwRevisadaCommand>
{
    public MarcarDiferenciaFiscalAwRevisadaValidator()
    {
        RuleFor(c => c.Tipo).Must(t => Enum.TryParse<TipoEntidadDiferenciaFiscal>(t, true, out _))
            .WithMessage("El tipo debe ser Cliente o Producto.");
        RuleFor(c => c.EntidadId).NotEmpty().WithMessage("Indica el cliente o producto.");
        RuleFor(c => c).Must(c => Enum.TryParse<TipoEntidadDiferenciaFiscal>(c.Tipo, true, out var t) && CamposDiferenciaFiscal.EsValido(t, c.Campo))
            .WithName("Campo").WithMessage("El campo no corresponde al tipo indicado.");
        RuleFor(c => c.Huella).Matches("^[0-9a-f]{64}$").WithMessage("La huella de la diferencia no es válida.");
        RuleFor(c => c.Nota).MaximumLength(DiferenciaFiscalAwRevision.MaxNota)
            .WithMessage($"La nota no puede exceder {DiferenciaFiscalAwRevision.MaxNota} caracteres.");
    }
}

public sealed class MarcarDiferenciaFiscalAwRevisadaHandler(
    DiferenciasFiscalesAwService servicio, CompartidoDbContext db, ICurrentUserContext user, TimeProvider time)
    : IRequestHandler<MarcarDiferenciaFiscalAwRevisadaCommand, RevisionDiferenciaFiscalAwDto>
{
    public async Task<RevisionDiferenciaFiscalAwDto> Handle(MarcarDiferenciaFiscalAwRevisadaCommand request, CancellationToken cancellationToken)
    {
        var usuario = user.UserId ?? throw new ForbiddenException("USUARIO_REQUERIDO", "Inicia sesión para marcar la diferencia como revisada.");
        var tipo = Enum.Parse<TipoEntidadDiferenciaFiscal>(request.Tipo, true);
        var vigente = (await servicio.CompararAsync(new(tipo, request.Campo, request.EntidadId), cancellationToken)).Diferencias is [var d, ..] ? d : null;
        if (vigente is null || vigente.Huella != request.Huella)
            throw new ConflictException("DIFERENCIA_FISCAL_CAMBIO",
                "La diferencia cambió o ya no existe. Vuelve a ejecutar el reporte antes de marcarla como revisada.");

        var revision = await db.DiferenciasFiscalesAwRevisiones
            .SingleOrDefaultAsync(r => r.Tipo == tipo && r.EntidadId == request.EntidadId && r.Campo == request.Campo, cancellationToken);
        if (revision is null)
        {
            revision = new DiferenciaFiscalAwRevision(Guid.CreateVersion7(), tipo, request.EntidadId, vigente.Referencia, request.Campo);
            db.DiferenciasFiscalesAwRevisiones.Add(revision);
        }
        revision.Revisar(vigente.Huella, vigente.ValorOrigen, vigente.ValorErp, usuario,
            user.UserName ?? user.Email, time.GetUtcNow(), request.Nota);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // Dos revisiones simultáneas de la misma diferencia: el índice único decide.
            throw new ConflictException("DIFERENCIA_FISCAL_CAMBIO", "Otra persona acaba de revisar esta diferencia. Recarga el reporte.");
        }
        return new(revision.Id, revision.Tipo.ToString(), revision.EntidadId, revision.Campo, revision.Huella,
            revision.RevisadoEn, revision.RevisadoPorNombre, revision.Version);
    }
}

// ───────────── Corridas persistidas ─────────────

public sealed record EjecutarCorridaDiferenciasFiscalesAwCommand(OrigenCorridaDiferenciaFiscal Origen, string Actor)
    : IRequest<CorridaDiferenciasFiscalesAwResumen>;

public sealed class EjecutarCorridaDiferenciasFiscalesAwHandler(
    DiferenciasFiscalesAwService servicio, CompartidoDbContext db, TimeProvider time)
    : IRequestHandler<EjecutarCorridaDiferenciasFiscalesAwCommand, CorridaDiferenciasFiscalesAwResumen>
{
    /// <summary>Las corridas son diagnóstico: se conservan 180 días.</summary>
    public static readonly TimeSpan Retencion = TimeSpan.FromDays(180);

    public async Task<CorridaDiferenciasFiscalesAwResumen> Handle(EjecutarCorridaDiferenciasFiscalesAwCommand request, CancellationToken cancellationToken)
    {
        var corrida = new DiferenciaFiscalAwCorrida(Guid.CreateVersion7(), request.Origen, request.Actor, time.GetUtcNow());
        try
        {
            var resultado = await servicio.CompararAsync(new(), cancellationToken);
            var revisiones = await servicio.RevisionesAsync(cancellationToken);
            // Solo lo pendiente: lo revisado con la misma huella no es trabajo de esta corrida.
            var filas = resultado.Diferencias
                .Where(d => revisiones.GetValueOrDefault((d.Tipo, d.EntidadId, d.Campo))?.Huella != d.Huella)
                .Select(d => new DiferenciaFiscalAwCorridaFila(Guid.CreateVersion7(), corrida.Id, d.Tipo, d.EntidadId, d.Referencia,
                    d.Nombre, d.Campo, d.Fuente, d.ValorOrigen, d.ValorErp, d.Estado.ToString(), d.Huella));
            corrida.Completar(resultado.ClientesComparados, resultado.ProductosComparados, filas, resultado.Avisos, time.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            corrida.Fallar("No se pudo calcular el reporte (" + ex.GetType().Name + ").", time.GetUtcNow());
            db.DiferenciasFiscalesAwCorridas.Add(corrida);
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
        db.DiferenciasFiscalesAwCorridas.Add(corrida);
        await db.SaveChangesAsync(cancellationToken);

        var limite = time.GetUtcNow() - Retencion;
        await db.DiferenciasFiscalesAwCorridas.Where(x => x.IniciadaEn < limite).ExecuteDeleteAsync(cancellationToken);
        return ReporteDiferenciasFiscalesAw.Resumen(corrida);
    }
}

public sealed record ObtenerUltimaCorridaDiferenciasFiscalesAwQuery(bool IncluirRevisadas) : IRequest<UltimaCorridaDiferenciasFiscalesAw>;

public sealed class ObtenerUltimaCorridaDiferenciasFiscalesAwHandler(CompartidoDbContext db, DiferenciasFiscalesAwService servicio)
    : IRequestHandler<ObtenerUltimaCorridaDiferenciasFiscalesAwQuery, UltimaCorridaDiferenciasFiscalesAw>
{
    public async Task<UltimaCorridaDiferenciasFiscalesAw> Handle(ObtenerUltimaCorridaDiferenciasFiscalesAwQuery request, CancellationToken cancellationToken)
    {
        var corrida = await db.DiferenciasFiscalesAwCorridas.AsNoTracking().Include(c => c.Filas)
            .OrderByDescending(c => c.IniciadaEn).FirstOrDefaultAsync(cancellationToken);
        var programada = await db.DiferenciasFiscalesAwCorridas.AsNoTracking()
            .Where(c => c.Origen == OrigenCorridaDiferenciaFiscal.Programada)
            .OrderByDescending(c => c.IniciadaEn).FirstOrDefaultAsync(cancellationToken);
        if (corrida is null) return new(null, null, null);

        var revisiones = await servicio.RevisionesAsync(cancellationToken);
        var filas = corrida.Filas.Select(f => ReporteDiferenciasFiscalesAw.Fila(
                new DiferenciaFiscalDetectada(f.Tipo, f.EntidadId, f.ReferenciaExterna, f.Nombre, f.Campo, f.Fuente, f.ValorOrigen,
                    f.ValorErp, Enum.Parse<EstadoDiferenciaFiscal>(f.Estado), f.Huella),
                revisiones.GetValueOrDefault((f.Tipo, f.EntidadId, f.Campo))))
            .ToList();
        var resumen = ReporteDiferenciasFiscalesAw.Resumen(corrida);
        var filtros = new Dictionary<string, string?>
        {
            ["Corrida"] = corrida.Origen == OrigenCorridaDiferenciaFiscal.Programada ? "Programada" : "Manual",
            ["Revisadas"] = request.IncluirRevisadas ? "Incluidas" : "Ocultas",
        };
        var reporte = ReporteDiferenciasFiscalesAw.Construir(corrida.TerminadaEn ?? corrida.IniciadaEn, filtros, filas,
            request.IncluirRevisadas, resumen.Avisos, corrida.ClientesComparados, corrida.ProductosComparados);
        return new(resumen, reporte, programada is null ? null : ReporteDiferenciasFiscalesAw.Resumen(programada));
    }
}
