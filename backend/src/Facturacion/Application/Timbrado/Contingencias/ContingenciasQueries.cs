using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Reportes;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Facturacion.Application.Timbrado.Contingencias;

public static class ContingenciasPermisos
{
    /// <summary>Fiscal y contabilidad ven todas las sucursales con el permiso corporativo de reportes.</summary>
    public const string TodasSucursales = "facturacion.reportes.leer-todas-sucursales";
}

public sealed record ContingenciaItem(
    Guid Id,
    Guid SucursalId,
    string Tipo,
    string Origen,
    string Estado,
    string Motivo,
    string? ErrorCodigo,
    DateTimeOffset OcurridaAt,
    DateOnly FechaLimite,
    bool PorVencer,
    bool Vencida,
    Guid? ComprobanteId,
    string? ComprobanteFolio,
    string? ComprobanteEstado,
    string? RegistradaPor,
    DateTimeOffset? CerradaAt,
    string? UuidFinal,
    string? NotaCierre,
    string? CerradaPor,
    int Version);

public sealed record ContingenciasResponse(
    IReadOnlyList<ContingenciaItem> Items,
    int Abiertas,
    int PorVencer,
    int Vencidas);

/// <summary>
/// Bitácora de contingencias (F6.8). Filtra por mes del evento; las abiertas de
/// cualquier mes se cuentan siempre en <c>PorVencer</c>/<c>Vencidas</c> para el aviso.
/// </summary>
public sealed record ContingenciasQuery(int? Anio = null, int? Mes = null, string? Estado = null)
    : IRequest<ContingenciasResponse>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => ContingenciasPermisos.TodasSucursales;
    [JsonIgnore] public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class ContingenciasQueryValidator : AbstractValidator<ContingenciasQuery>
{
    public ContingenciasQueryValidator()
    {
        RuleFor(q => q.Mes).InclusiveBetween(1, 12).When(q => q.Mes.HasValue);
        RuleFor(q => q.Anio).InclusiveBetween(2020, 2100).When(q => q.Anio.HasValue);
        RuleFor(q => q.Anio).NotNull().When(q => q.Mes.HasValue).WithMessage("Indica el año del mes.");
        RuleFor(q => q.Estado).Must(e => Enum.TryParse<EstadoContingenciaTimbrado>(e, true, out _))
            .When(q => !string.IsNullOrWhiteSpace(q.Estado)).WithMessage("Estado no válido.");
    }
}

public sealed class ContingenciasQueryHandler(FacturacionDbContext db, IClock clock)
    : IRequestHandler<ContingenciasQuery, ContingenciasResponse>
{
    public async Task<ContingenciasResponse> Handle(ContingenciasQuery request, CancellationToken cancellationToken)
    {
        var hoy = ZonaMillet.Hoy(clock.UtcNow);
        var baseQuery = ContingenciasLectura.Alcance(db, request.SucursalesPermitidas);

        var abiertas = await baseQuery.Where(c => c.Estado == EstadoContingenciaTimbrado.Abierta)
            .Select(c => c.FechaLimite).ToListAsync(cancellationToken);
        var vencidas = abiertas.Count(f => hoy > f);
        var porVencer = abiertas.Count(f => hoy <= f && hoy >= new DateOnly(f.Year, f.Month, 1));

        var filtrada = baseQuery;
        if (request.Anio is int anio)
        {
            var (desde, hasta) = ContingenciasLectura.Rango(anio, request.Mes);
            filtrada = filtrada.Where(c => c.OcurridaAt >= desde && c.OcurridaAt < hasta);
        }
        if (!string.IsNullOrWhiteSpace(request.Estado))
        {
            var estado = Enum.Parse<EstadoContingenciaTimbrado>(request.Estado, true);
            filtrada = filtrada.Where(c => c.Estado == estado);
        }

        var items = await ContingenciasLectura.ProyectarAsync(db, filtrada.OrderByDescending(c => c.OcurridaAt).Take(500), hoy, cancellationToken);
        return new ContingenciasResponse(items, abiertas.Count, porVencer, vencidas);
    }
}

/// <summary>Reporte mensual de contingencias (ADR-0036): todo lo ocurrido en el mes y cómo quedó.</summary>
public sealed record ReporteContingenciasQuery(int Anio, int Mes)
    : IRequest<ReporteResponse<ReporteContingenciaFila>>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => ContingenciasPermisos.TodasSucursales;
    [JsonIgnore] public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed record ReporteContingenciaFila(
    string Ocurrida,
    string Tipo,
    string Origen,
    string Documento,
    string Motivo,
    string RegistradaPor,
    string Estado,
    string FechaLimite,
    string Regularizada,
    string CfdiFinal);

public sealed class ReporteContingenciasQueryValidator : AbstractValidator<ReporteContingenciasQuery>
{
    public ReporteContingenciasQueryValidator()
    {
        RuleFor(q => q.Mes).InclusiveBetween(1, 12);
        RuleFor(q => q.Anio).InclusiveBetween(2020, 2100);
    }
}

public sealed class ReporteContingenciasQueryHandler(FacturacionDbContext db, IClock clock)
    : IRequestHandler<ReporteContingenciasQuery, ReporteResponse<ReporteContingenciaFila>>
{
    public async Task<ReporteResponse<ReporteContingenciaFila>> Handle(
        ReporteContingenciasQuery request, CancellationToken cancellationToken)
    {
        var hoy = ZonaMillet.Hoy(clock.UtcNow);
        var (desde, hasta) = ContingenciasLectura.Rango(request.Anio, request.Mes);
        var query = ContingenciasLectura.Alcance(db, request.SucursalesPermitidas)
            .Where(c => c.OcurridaAt >= desde && c.OcurridaAt < hasta)
            .OrderBy(c => c.OcurridaAt);
        var items = await ContingenciasLectura.ProyectarAsync(db, query, hoy, cancellationToken);

        static string Fecha(DateTimeOffset? f) => f is { } v ? ZonaMillet.ALocal(v).ToString("dd/MM/yyyy HH:mm") : "—";
        var filas = items.Select(i => new ReporteContingenciaFila(
            Fecha(i.OcurridaAt),
            ContingenciasLectura.EtiquetaTipo(i.Tipo),
            i.Origen == nameof(OrigenContingenciaTimbrado.Automatica) ? "Automática" : "Manual",
            i.ComprobanteFolio ?? "Sin documento",
            i.Motivo,
            i.RegistradaPor ?? "Sistema",
            i.Vencida ? "Abierta (vencida)" : ContingenciasLectura.EtiquetaEstado(i.Estado),
            i.FechaLimite.ToString("dd/MM/yyyy"),
            Fecha(i.CerradaAt),
            i.UuidFinal ?? "—")).ToList();

        return new ReporteResponse<ReporteContingenciaFila>(
            Titulo: "Bitácora de contingencias de timbrado",
            GeneradoEn: clock.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?> { ["Mes"] = $"{request.Mes:00}/{request.Anio}" },
            Columnas:
            [
                new("ocurrida", "Ocurrida", "texto"),
                new("tipo", "Tipo", "texto"),
                new("origen", "Origen", "texto"),
                new("documento", "Documento", "texto"),
                new("motivo", "Motivo", "texto"),
                new("registradaPor", "Registró", "texto"),
                new("estado", "Estado", "texto"),
                new("fechaLimite", "Regularizar a más tardar", "texto"),
                new("regularizada", "Cerrada", "texto"),
                new("cfdiFinal", "CFDI final", "texto"),
            ],
            Filas: filas,
            Totales: new Dictionary<string, decimal>
            {
                ["Contingencias"] = items.Count,
                ["Abiertas"] = items.Count(i => i.Estado == nameof(EstadoContingenciaTimbrado.Abierta)),
                ["Regularizadas"] = items.Count(i => i.Estado == nameof(EstadoContingenciaTimbrado.Regularizada)),
                ["Descartadas"] = items.Count(i => i.Estado == nameof(EstadoContingenciaTimbrado.Descartada)),
            });
    }
}

/// <summary>Registro manual: SAT, internet u otra causa (también PAC/certificado sin intento fallido).</summary>
public sealed record RegistrarContingenciaCommand(
    Guid SucursalId,
    string Tipo,
    string Motivo,
    DateTimeOffset OcurridaAt,
    Guid? ComprobanteId = null)
    : IRequest<ContingenciaItem>, ISucursalSeleccionadaQuery
{
    public string PermisoTodasSucursales => ContingenciasPermisos.TodasSucursales;
    [JsonIgnore] public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    Guid? ISucursalSeleccionadaQuery.SucursalId => SucursalId;
}

public sealed class RegistrarContingenciaValidator : AbstractValidator<RegistrarContingenciaCommand>
{
    public RegistrarContingenciaValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.Tipo).Must(t => Enum.TryParse<TipoContingenciaTimbrado>(t, true, out var v) && Enum.IsDefined(v))
            .WithMessage("Tipo no válido (Pac, Sat, Internet, Certificado u Otra).");
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(2000);
        RuleFor(c => c.OcurridaAt).NotEmpty();
        RuleFor(c => c.ComprobanteId).NotEqual(Guid.Empty).When(c => c.ComprobanteId.HasValue);
    }
}

public sealed class RegistrarContingenciaHandler(
    FacturacionDbContext db, IClock clock, ICurrentUserContext user, ICurrentEmpresaContext empresa)
    : IRequestHandler<RegistrarContingenciaCommand, ContingenciaItem>
{
    public async Task<ContingenciaItem> Handle(RegistrarContingenciaCommand command, CancellationToken cancellationToken)
    {
        var ahora = clock.UtcNow;
        if (command.OcurridaAt > ahora.AddMinutes(5))
            throw new BusinessRuleException("CONTINGENCIA_FECHA_FUTURA", "La contingencia no puede ocurrir en el futuro.");

        string? folio = null;
        if (command.ComprobanteId is Guid comprobanteId)
        {
            var comprobante = await db.Comprobantes.FirstOrDefaultAsync(c => c.Id == comprobanteId, cancellationToken)
                ?? throw new EntityNotFoundException("COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante {comprobanteId}.");
            if (comprobante.SucursalId != command.SucursalId)
                throw new BusinessRuleException("CONTINGENCIA_SUCURSAL_DISTINTA", "El documento es de otra sucursal.");
            if (comprobante.Estado is not (EstadoTimbrado.TimbradoFallido or EstadoTimbrado.TimbradoEnProceso))
                throw new BusinessRuleException(
                    "CONTINGENCIA_DOCUMENTO_NO_PENDIENTE",
                    "Solo se liga un documento con timbrado fallido o en proceso.");
            if (await db.ContingenciasTimbrado.AnyAsync(c => c.ComprobanteId == comprobanteId
                    && c.Estado == EstadoContingenciaTimbrado.Abierta, cancellationToken))
                throw new BusinessRuleException(
                    "CONTINGENCIA_YA_ABIERTA", $"El documento {comprobante.Folio} ya tiene una contingencia abierta.");
            folio = comprobante.Folio;
        }

        var contingencia = ContingenciaTimbrado.Registrar(
            empresa.Current ?? Guid.Empty, command.SucursalId, Enum.Parse<TipoContingenciaTimbrado>(command.Tipo, true),
            OrigenContingenciaTimbrado.Manual, command.Motivo, null, command.OcurridaAt,
            command.ComprobanteId, folio, user.UserId, user.UserName);
        db.ContingenciasTimbrado.Add(contingencia);
        await db.SaveChangesAsync(cancellationToken);

        return (await ContingenciasLectura.ProyectarAsync(
            db, db.ContingenciasTimbrado.AsNoTracking().Where(c => c.Id == contingencia.Id),
            ZonaMillet.Hoy(ahora), cancellationToken)).Single();
    }
}

/// <summary>Cierre manual de una contingencia sin documento afectado.</summary>
public sealed record RegularizarContingenciaCommand(Guid Id, string Nota)
    : IRequest<ContingenciaItem>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => ContingenciasPermisos.TodasSucursales;
    [JsonIgnore] public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class RegularizarContingenciaValidator : AbstractValidator<RegularizarContingenciaCommand>
{
    public RegularizarContingenciaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nota).NotEmpty().MaximumLength(2000);
    }
}

public sealed class RegularizarContingenciaHandler(FacturacionDbContext db, IClock clock, ICurrentUserContext user)
    : IRequestHandler<RegularizarContingenciaCommand, ContingenciaItem>
{
    public async Task<ContingenciaItem> Handle(RegularizarContingenciaCommand command, CancellationToken cancellationToken)
    {
        var contingencia = await ContingenciasLectura.Alcance(db, command.SucursalesPermitidas, tracking: true)
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTINGENCIA_NO_ENCONTRADA", $"No existe la contingencia {command.Id}.");
        var ahora = clock.UtcNow;
        contingencia.RegularizarManual(command.Nota, ahora, user.UserName);
        await db.SaveChangesAsync(cancellationToken);

        return (await ContingenciasLectura.ProyectarAsync(
            db, db.ContingenciasTimbrado.AsNoTracking().Where(c => c.Id == contingencia.Id),
            ZonaMillet.Hoy(ahora), cancellationToken)).Single();
    }
}

internal static class ContingenciasLectura
{
    public static IQueryable<ContingenciaTimbrado> Alcance(
        FacturacionDbContext db, IReadOnlyList<Guid>? sucursales, bool tracking = false)
    {
        var q = tracking ? db.ContingenciasTimbrado : db.ContingenciasTimbrado.AsNoTracking();
        return sucursales is null ? q : q.Where(c => sucursales.Contains(c.SucursalId));
    }

    /// <summary>Rango UTC del mes (o del año) en hora de Mérida.</summary>
    public static (DateTimeOffset Desde, DateTimeOffset Hasta) Rango(int anio, int? mes)
    {
        var inicio = new DateTime(anio, mes ?? 1, 1);
        var fin = mes is null ? inicio.AddYears(1) : inicio.AddMonths(1);
        var zona = TimeZoneInfo.FindSystemTimeZoneById(ZonaMillet.Id);
        return (new DateTimeOffset(inicio, zona.GetUtcOffset(inicio)).ToUniversalTime(),
            new DateTimeOffset(fin, zona.GetUtcOffset(fin)).ToUniversalTime());
    }

    public static async Task<IReadOnlyList<ContingenciaItem>> ProyectarAsync(
        FacturacionDbContext db, IQueryable<ContingenciaTimbrado> query, DateOnly hoy, CancellationToken cancellationToken)
    {
        var filas = await query.ToListAsync(cancellationToken);
        var ids = filas.Where(f => f.ComprobanteId is not null).Select(f => f.ComprobanteId!.Value).Distinct().ToList();
        var estados = ids.Count == 0
            ? new Dictionary<Guid, EstadoTimbrado>()
            : await db.Comprobantes.AsNoTracking().Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Estado, cancellationToken);

        return filas.Select(c => new ContingenciaItem(
            c.Id, c.SucursalId, c.Tipo.ToString(), c.Origen.ToString(), c.Estado.ToString(), c.Motivo, c.ErrorCodigo,
            c.OcurridaAt, c.FechaLimite,
            PorVencer: c.RequiereAlerta(hoy) && !c.EstaVencida(hoy),
            Vencida: c.EstaVencida(hoy),
            c.ComprobanteId, c.ComprobanteFolio,
            c.ComprobanteId is Guid id && estados.TryGetValue(id, out var e) ? e.ToString() : null,
            c.RegistradaPorNombre ?? c.CreatedBy,
            c.CerradaAt, c.UuidFinal, c.NotaCierre,
            c.CerradaAt is null ? null : c.CerradaPorNombre ?? c.UpdatedBy, c.Version)).ToList();
    }

    public static string EtiquetaTipo(string tipo) => tipo switch
    {
        nameof(TipoContingenciaTimbrado.Pac) => "PAC",
        nameof(TipoContingenciaTimbrado.Sat) => "SAT",
        nameof(TipoContingenciaTimbrado.Internet) => "Internet",
        nameof(TipoContingenciaTimbrado.Certificado) => "Certificado (CSD)",
        _ => "Otra",
    };

    public static string EtiquetaEstado(string estado) => estado switch
    {
        nameof(EstadoContingenciaTimbrado.Abierta) => "Abierta",
        nameof(EstadoContingenciaTimbrado.Regularizada) => "Regularizada",
        _ => "Descartada (folio quemado)",
    };
}
