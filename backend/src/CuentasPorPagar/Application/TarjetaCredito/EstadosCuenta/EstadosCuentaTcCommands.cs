using System.Security.Cryptography;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.Ports.TarjetaCredito;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.TarjetaCredito.EstadosCuenta;

// ============================================================================
// F7-PR5: comandos del ciclo de EstadoCuentaTc.
// 1. CrearEstadoCuentaTc: Auxiliar selecciona periodo + tarjeta.
// 2. SubirArchivoEstadoCuenta: SHA-256 dedup, parser, persiste líneas.
// 3. ConciliarAutomatico: algoritmo de match §7, marca líneas + movimientos.
// ============================================================================

// --------------------------------------------------- Crear

public sealed record CrearEstadoCuentaTcCommand(
    Guid TarjetaId,
    DateOnly PeriodoDesde,
    DateOnly PeriodoHasta,
    DateOnly FechaCorte,
    DateOnly FechaLimitePago) : IRequest<EstadoCuentaTcResponse>;

public sealed record EstadoCuentaTcResponse(
    Guid Id,
    Guid TarjetaId,
    DateOnly PeriodoDesde,
    DateOnly PeriodoHasta,
    DateOnly FechaCorte,
    DateOnly FechaLimitePago,
    EstadoCuentaTcStatus Estado,
    decimal? TotalBancoMxn,
    decimal? TotalConciliadoMxn,
    decimal? DiferenciaMxn,
    string? PerfilParserUsado,
    int LineasCount,
    int Version);

public sealed class CrearEstadoCuentaTcValidator : AbstractValidator<CrearEstadoCuentaTcCommand>
{
    public CrearEstadoCuentaTcValidator()
    {
        RuleFor(c => c.TarjetaId).NotEmpty();
        RuleFor(c => c.PeriodoHasta).GreaterThanOrEqualTo(c => c.PeriodoDesde);
        RuleFor(c => c.FechaLimitePago).GreaterThanOrEqualTo(c => c.FechaCorte);
    }
}

public sealed class CrearEstadoCuentaTcHandler : IRequestHandler<CrearEstadoCuentaTcCommand, EstadoCuentaTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public CrearEstadoCuentaTcHandler(CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<EstadoCuentaTcResponse> Handle(
        CrearEstadoCuentaTcCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        var existe = await _db.EstadosCuentaTc
            .AsNoTracking()
            .AnyAsync(e =>
                e.TarjetaId == command.TarjetaId
                && e.PeriodoDesde == command.PeriodoDesde
                && e.PeriodoHasta == command.PeriodoHasta, cancellationToken);
        if (existe)
        {
            throw new BusinessRuleException(
                "EC_PERIODO_DUPLICADO",
                $"Ya existe un estado de cuenta para tarjeta={command.TarjetaId} periodo {command.PeriodoDesde}..{command.PeriodoHasta}.");
        }

        var ec = EstadoCuentaTc.Crear(
            empresaId: empresaId,
            tarjetaId: command.TarjetaId,
            periodoDesde: command.PeriodoDesde,
            periodoHasta: command.PeriodoHasta,
            fechaCorte: command.FechaCorte,
            fechaLimitePago: command.FechaLimitePago);

        _db.EstadosCuentaTc.Add(ec);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(ec);
    }

    internal static EstadoCuentaTcResponse ToResponse(EstadoCuentaTc e) =>
        new(e.Id, e.TarjetaId, e.PeriodoDesde, e.PeriodoHasta,
            e.FechaCorte, e.FechaLimitePago, e.Estado,
            e.TotalBancoMxn, e.TotalConciliadoMxn, e.DiferenciaMxn,
            e.PerfilParserUsado, e.Lineas.Count, e.Version);
}

// --------------------------------------------------- Subir archivo

/// <summary>
/// Sube un archivo del banco, calcula SHA-256, dedupea, identifica el
/// perfil del parser desde la tarjeta, parsea, persiste las líneas
/// crudas. El archivo se guarda referenciado por su blob ref (en MVP
/// vive en filesystem local — F10-PR3 lo moverá a Azure Blob).
/// </summary>
public sealed record SubirArchivoEstadoCuentaTcCommand(
    Guid Id,
    int VersionEsperada,
    string NombreArchivo,
    string ContentType,
    byte[] Contenido) : IRequest<SubirArchivoEstadoCuentaTcResponse>;

public sealed record SubirArchivoEstadoCuentaTcResponse(
    Guid Id,
    string PerfilParserUsado,
    string ArchivoBancoHash,
    int LineasParseadas,
    int ErroresParseo,
    decimal? TotalDeclaradoMxn,
    int Version);

public sealed class SubirArchivoEstadoCuentaTcValidator : AbstractValidator<SubirArchivoEstadoCuentaTcCommand>
{
    public SubirArchivoEstadoCuentaTcValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
        RuleFor(c => c.NombreArchivo).NotEmpty().MaximumLength(255);
        RuleFor(c => c.Contenido).NotNull().Must(b => b.Length > 0 && b.Length <= 50 * 1024 * 1024)
            .WithMessage("El archivo debe pesar entre 1 byte y 50 MB.");
    }
}

public sealed class SubirArchivoEstadoCuentaTcHandler
    : IRequestHandler<SubirArchivoEstadoCuentaTcCommand, SubirArchivoEstadoCuentaTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;
    private readonly IEstadoCuentaTcParserPort _parser;

    public SubirArchivoEstadoCuentaTcHandler(
        CuentasPorPagarDbContext db,
        ICurrentUserContext currentUser,
        IClock clock,
        IEstadoCuentaTcParserPort parser)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _parser = parser;
    }

    public async Task<SubirArchivoEstadoCuentaTcResponse> Handle(
        SubirArchivoEstadoCuentaTcCommand command, CancellationToken cancellationToken)
    {
        var ec = await _db.EstadosCuentaTc
            .Include(e => e.Lineas)
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("EC_NO_ENCONTRADO",
                $"No se encontró el estado de cuenta '{command.Id}'.");
        if (ec.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(EstadoCuentaTc), ec.Id);

        var tarjeta = await _db.TarjetasCredito
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ec.TarjetaId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{ec.TarjetaId}'.");

        // SHA-256 + dedupe.
        var hashBytes = SHA256.HashData(command.Contenido);
        var sha256Hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var yaCargado = await _db.EstadosCuentaTc
            .AsNoTracking()
            .AnyAsync(e =>
                e.TarjetaId == tarjeta.Id
                && e.ArchivoBancoHash == sha256Hex
                && e.Id != ec.Id, cancellationToken);
        if (yaCargado)
        {
            throw new BusinessRuleException(
                "EC_ARCHIVO_DUPLICADO",
                "Este archivo ya fue procesado en otro estado de cuenta de la misma tarjeta.");
        }

        // Identificar perfil desde la tarjeta.
        var perfil = await _db.PerfilesParserBanco
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Codigo == tarjeta.PerfilParser, cancellationToken)
            ?? throw new BusinessRuleException(
                "EC_PERFIL_NO_REGISTRADO",
                $"No hay perfil de parser registrado con código '{tarjeta.PerfilParser}'.");
        if (!perfil.Activo)
        {
            throw new BusinessRuleException(
                "EC_PERFIL_INACTIVO",
                $"El perfil '{perfil.Codigo}' está desactivado.");
        }

        // Parsear (en MVP el blob se guarda con un nombre derivado del Id;
        // F10-PR3 lo mueve a Azure Blob Storage real).
        using var stream = new MemoryStream(command.Contenido);
        var parseResult = await _parser.ParseAsync(stream, perfil, cancellationToken);

        var ahora = _clock.UtcNow;
        var blobRef = $"estados-cuenta-tc/{ec.TarjetaId}/{ec.Id}/{command.NombreArchivo}";

        // TC-B4: recarga de archivo corregido — la primera versión
        // tronaba con 23505 (ux_linea_archivo_posicion) al re-subir. El
        // agregado limpia las líneas previas (solo sin matches) y aquí se
        // eliminan físicamente antes de re-insertar.
        if (ec.Lineas.Count > 0)
        {
            var removidas = ec.LimpiarLineasParaRecarga();
            _db.RemoveRange(removidas);
        }

        ec.RegistrarArchivoBanco(
            blobRef: blobRef,
            sha256Hex: sha256Hex,
            cargadoBy: _currentUser.UserId ?? Guid.Empty,
            perfilParserUsado: perfil.Codigo,
            totalDeclaradoMxn: parseResult.TotalDeclaradoMxn,
            ahora: ahora);

        // Insertar líneas parseadas dentro del periodo + las que estén fuera
        // (el parser ya devuelve solo las filas de datos; el periodo lo valida
        // operativamente desde la UI). El agregado valida unicidad de posición.
        foreach (var linea in parseResult.Lineas)
        {
            ec.AgregarLinea(
                posicionArchivo: linea.PosicionArchivo,
                fechaAplicacion: linea.FechaAplicacion,
                monto: linea.Monto,
                moneda: linea.Moneda,
                montoMxn: linea.MontoMxn,
                merchantRaw: linea.MerchantRaw,
                referenciaBanco: linea.ReferenciaBanco,
                tipoSegunBanco: linea.TipoSegunBanco);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new SubirArchivoEstadoCuentaTcResponse(
            Id: ec.Id,
            PerfilParserUsado: perfil.Codigo,
            ArchivoBancoHash: sha256Hex,
            LineasParseadas: parseResult.Lineas.Count,
            ErroresParseo: parseResult.Errores.Count,
            TotalDeclaradoMxn: parseResult.TotalDeclaradoMxn,
            Version: ec.Version);
    }
}

// --------------------------------------------------- Conciliar automático

public sealed record ConciliarAutomaticoEstadoCuentaTcCommand(Guid Id, int VersionEsperada)
    : IRequest<ConciliarAutomaticoResponse>;

public sealed record ConciliarAutomaticoResponse(
    Guid Id,
    int LineasTotal,
    int LineasMatchedAuto,
    int LineasSugerencia,
    int LineasSinMatch,
    int LineasOmitidasNoBuscanMatch,
    decimal TotalConciliadoMxn,
    decimal? DiferenciaMxn,
    int Version);

public sealed class ConciliarAutomaticoValidator : AbstractValidator<ConciliarAutomaticoEstadoCuentaTcCommand>
{
    public ConciliarAutomaticoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

public sealed class ConciliarAutomaticoHandler
    : IRequestHandler<ConciliarAutomaticoEstadoCuentaTcCommand, ConciliarAutomaticoResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IConciliacionAutomaticaService _service;

    public ConciliarAutomaticoHandler(
        CuentasPorPagarDbContext db, IConciliacionAutomaticaService service)
    {
        _db = db; _service = service;
    }

    public async Task<ConciliarAutomaticoResponse> Handle(
        ConciliarAutomaticoEstadoCuentaTcCommand command, CancellationToken cancellationToken)
    {
        var ec = await _db.EstadosCuentaTc
            .Include(e => e.Lineas)
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("EC_NO_ENCONTRADO",
                $"No se encontró el estado de cuenta '{command.Id}'.");
        if (ec.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(EstadoCuentaTc), ec.Id);

        if (ec.Lineas.Count == 0)
        {
            throw new BusinessRuleException(
                "EC_SIN_LINEAS",
                "No se puede conciliar un estado de cuenta sin líneas — sube el archivo primero.");
        }

        // Candidatos: movimientos Registrado de la misma tarjeta en ventana
        // [periodoDesde - 3 días, periodoHasta + 3 días] (compras) o
        // 90 días hacia atrás para refunds; tomamos el rango amplio y el
        // algoritmo filtra por ventana específica por línea.
        var desde = ec.PeriodoDesde.AddDays(-90);
        var hasta = ec.PeriodoHasta.AddDays(3);

        var candidatos = await _db.MovimientosTarjetaCredito
            .Where(m => m.TarjetaId == ec.TarjetaId
                        && m.Estado == EstadoMovimientoTc.Registrado
                        && m.FechaMovimiento >= desde
                        && m.FechaMovimiento <= hasta)
            .ToListAsync(cancellationToken);

        var resultado = await _service.ConciliarAsync(ec, candidatos, cancellationToken);

        // Promover los movs matched a ConciliadoConEstadoCuenta + vincular
        // a la línea y al estado de cuenta.
        var movsMatched = ec.Lineas
            .Where(l => l.EstadoMatch == EstadoMatchLineaBanco.Matched && l.MovimientoTcId is not null)
            .ToList();

        foreach (var linea in movsMatched)
        {
            var mov = candidatos.First(c => c.Id == linea.MovimientoTcId!.Value);
            mov.MarcarConciliadoConEstadoCuenta(ec.Id, linea.Id, linea.FechaAplicacion);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new ConciliarAutomaticoResponse(
            Id: ec.Id,
            LineasTotal: resultado.LineasTotal,
            LineasMatchedAuto: resultado.LineasMatchedAuto,
            LineasSugerencia: resultado.LineasSugerencia,
            LineasSinMatch: resultado.LineasSinMatch,
            LineasOmitidasNoBuscanMatch: resultado.LineasOmitidasNoBuscanMatch,
            TotalConciliadoMxn: resultado.TotalConciliadoMxn,
            DiferenciaMxn: ec.DiferenciaMxn,
            Version: ec.Version);
    }
}

// --------------------------------------------------- Queries

public sealed record ListarEstadosCuentaTcQuery(
    Guid? TarjetaId = null,
    EstadoCuentaTcStatus? Estado = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<EstadoCuentaTcResponse>>;

public sealed class ListarEstadosCuentaTcHandler
    : IRequestHandler<ListarEstadosCuentaTcQuery, PagedResponse<EstadoCuentaTcResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    public ListarEstadosCuentaTcHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<PagedResponse<EstadoCuentaTcResponse>> Handle(
        ListarEstadosCuentaTcQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        IQueryable<EstadoCuentaTc> q = _db.EstadosCuentaTc.AsNoTracking()
            .Include(e => e.Lineas);
        if (query.TarjetaId is Guid t) q = q.Where(e => e.TarjetaId == t);
        if (query.Estado is EstadoCuentaTcStatus s) q = q.Where(e => e.Estado == s);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(e => e.FechaCorte)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        return new PagedResponse<EstadoCuentaTcResponse>(
            items.Select(CrearEstadoCuentaTcHandler.ToResponse).ToList(),
            offset, limit, total);
    }
}
