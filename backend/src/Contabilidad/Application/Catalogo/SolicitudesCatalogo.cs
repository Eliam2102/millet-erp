using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Contabilidad.Application.Importacion;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Npgsql;

namespace Millet.Contabilidad.Application.Catalogo;

public sealed record CambioCuentaDto(CuentaResponse? Antes, CuentaResponse Despues);
public sealed record SolicitudCatalogoDto(Guid Id, string Operacion, string Estado, Guid PreparadaPorId,
    string PreparadaPor, DateTimeOffset PreparadaEn, Guid? ResueltaPorId, string? ResueltaPor,
    DateTimeOffset? ResueltaEn, string? MotivoRechazo, int Version, IReadOnlyList<CambioCuentaDto> Cambios, ImportacionLoteDto? Lote = null)
{
    internal static SolicitudCatalogoDto De(SolicitudCatalogo s) => new(s.Id, s.Operacion, s.Estado,
        s.PreparadaPorId, s.PreparadaPor, s.PreparadaEn, s.ResueltaPorId, s.ResueltaPor, s.ResueltaEn,
        s.MotivoRechazo, s.Version, JsonSerializer.Deserialize<List<CambioCuentaDto>>(s.CambiosJson, SolicitudesCatalogo.Json)!);
}

/// <summary>Ejecuta las mismas reglas en memoria para preparar; solo persiste el catálogo al resolver.</summary>
public sealed class SolicitudesCatalogo(ContabilidadDbContext db, ICurrentUserContext usuario, IClock clock)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal Guid ActorId => usuario.UserId is { } id && id != Guid.Empty ? id
        : throw new BusinessRuleException("CONTAB_ACTOR_REQUERIDO", "No se pudo identificar al usuario de la solicitud.");
    internal string Actor => usuario.UserName ?? usuario.Email ?? ActorId.ToString();

    public async Task<CuentaResponse> PrepararCuentaAsync<T>(T comando, Func<Task<CuentaResponse>> preparar, CancellationToken cancellationToken)
    {
        var (cuenta, id) = await PrepararAsync(comando, preparar, cancellationToken);
        return cuenta with { SolicitudId = id };
    }

    internal async Task<(TResultado Resultado, Guid SolicitudId)> PrepararAsync<T, TResultado>(
        T comando, Func<Task<TResultado>> preparar, CancellationToken cancellationToken, string? huellaImportacion = null)
    {
        var actorId = ActorId;
        var antes = await db.Cuentas.AsNoTracking().OrderBy(c => c.Codigo).ToListAsync(cancellationToken);
        var foto = antes.ToDictionary(c => c.Id, c => CuentaResponse.De(c));
        var huella = Huella(antes);
        TResultado resultado;
        List<CambioCuentaDto> cambios;
        try
        {
            resultado = await preparar();
            db.ChangeTracker.DetectChanges();
            cambios = db.ChangeTracker.Entries<CuentaContable>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified)
                .Select(e => new CambioCuentaDto(foto.GetValueOrDefault(e.Entity.Id), CuentaResponse.De(e.Entity)))
                .OrderBy(c => c.Despues.Codigo).ToList();
        }
        finally
        {
            // Este contexto es del módulo: descarta la propuesta antes de guardar su expediente.
            db.ChangeTracker.Clear();
        }
        var operacion = comando switch
        {
            CrearCuentaCommand => "Alta", EditarCuentaCommand => "Cambio", DesactivarCuentaCommand => "Baja",
            ReactivarCuentaCommand => "Reactivacion", AplicarImportacionCommand => "Importacion",
            _ => throw new InvalidOperationException("Operación de catálogo no admitida."),
        };
        var solicitud = new SolicitudCatalogo(Guid.CreateVersion7(), operacion, JsonSerializer.Serialize(comando, Json),
            huella, JsonSerializer.Serialize(cambios, Json), actorId, Actor, clock.UtcNow, huellaImportacion,
            comando is CrearCuentaCommand alta ? FormatoCatalogo.Codigo(alta.Codigo) : null);
        db.SolicitudesCatalogo.Add(solicitud);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException e) when (PoliticaCatalogo.EsViolacionUnica(e, "ux_p9_alta_pendiente"))
        {
            throw new ConflictException("CONTAB_CUENTA_CODIGO_DUPLICADO",
                $"Ya existe una solicitud de alta pendiente para el código '{solicitud.CodigoAlta}'. Revísela antes de preparar otra.");
        }
        catch (DbUpdateException e) when (huellaImportacion is not null && PoliticaCatalogo.EsViolacionUnica(e, "ux_p9_importacion_pendiente"))
        {
            db.ChangeTracker.Clear();
            var previa = await db.SolicitudesCatalogo.SingleAsync(s => s.HuellaImportacion == huellaImportacion && s.Estado == "Pendiente", cancellationToken);
            return (resultado, previa.Id);
        }
        return (resultado, solicitud.Id);
    }

    internal static string Huella(IEnumerable<CuentaContable> cuentas) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cuentas.OrderBy(c => c.Codigo).Select(c => CuentaResponse.De(c)), Json))));
}

public sealed record ListarSolicitudesCatalogoQuery(string? Estado, int Offset = 0, int Limit = 20)
    : IRequest<PagedResponse<SolicitudCatalogoDto>>;

public sealed class ListarSolicitudesCatalogoValidator : AbstractValidator<ListarSolicitudesCatalogoQuery>
{
    public ListarSolicitudesCatalogoValidator()
    {
        RuleFor(x => x.Offset).GreaterThanOrEqualTo(0).WithMessage("El inicio de la página no puede ser negativo.");
        RuleFor(x => x.Limit).InclusiveBetween(1, 100).WithMessage("La página debe contener entre 1 y 100 solicitudes.");
        RuleFor(x => x.Estado).Must(e => e is null or "Pendiente" or "Autorizada" or "Rechazada")
            .WithMessage("El estado debe ser Pendiente, Autorizada o Rechazada.");
    }
}

public sealed class ListarSolicitudesCatalogoHandler(ContabilidadDbContext db)
    : IRequestHandler<ListarSolicitudesCatalogoQuery, PagedResponse<SolicitudCatalogoDto>>
{
    public async Task<PagedResponse<SolicitudCatalogoDto>> Handle(ListarSolicitudesCatalogoQuery request, CancellationToken cancellationToken)
    {
        var q = db.SolicitudesCatalogo.AsNoTracking();
        if (request.Estado is { } estado) q = q.Where(s => s.Estado == estado);
        var total = await q.CountAsync(cancellationToken);
        var filas = await q.OrderByDescending(s => s.PreparadaEn).ThenBy(s => s.Id).Skip(request.Offset).Take(request.Limit).ToListAsync(cancellationToken);
        return new([.. filas.Select(SolicitudCatalogoDto.De)], total, request.Offset, request.Limit);
    }
}

public sealed record ResolverSolicitudCatalogoCommand(Guid Id, int VersionEsperada, bool Autorizar, string? Motivo)
    : IRequest<SolicitudCatalogoDto>;

public sealed class ResolverSolicitudCatalogoValidator : AbstractValidator<ResolverSolicitudCatalogoCommand>
{
    public ResolverSolicitudCatalogoValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Indique la solicitud que desea resolver.");
        RuleFor(x => x.VersionEsperada).GreaterThan(0).WithMessage("Recargue la solicitud para obtener su versión vigente.");
        RuleFor(x => x.Motivo).MaximumLength(1000).WithMessage("El motivo del rechazo admite hasta 1000 caracteres.");
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Indique el motivo del rechazo.").When(x => !x.Autorizar);
    }
}

public sealed class ResolverSolicitudCatalogoHandler(ContabilidadDbContext db, FormatoCatalogo formato,
    SolicitudesCatalogo solicitudes, ICurrentUserContext usuario, IClock clock, ILogger<AplicarImportacionHandler> log)
    : IRequestHandler<ResolverSolicitudCatalogoCommand, SolicitudCatalogoDto>
{
    public async Task<SolicitudCatalogoDto> Handle(ResolverSolicitudCatalogoCommand request, CancellationToken cancellationToken)
    {
        try { return await ResolverAsync(request, cancellationToken); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("CONTAB_SOLICITUD_CONFLICTO", "Otra operación modificó el catálogo o la solicitud. Recargue y revise antes de resolver.");
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new ConflictException("CONTAB_SOLICITUD_CONFLICTO", "Otra operación modificó el catálogo o la solicitud. Recargue y revise antes de resolver.");
        }
    }

    private async Task<SolicitudCatalogoDto> ResolverAsync(ResolverSolicitudCatalogoCommand request, CancellationToken cancellationToken)
    {
        // Serializa decisiones y cambios del árbol; el token impide resolver dos veces la misma solicitud.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var s = await db.SolicitudesCatalogo.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_SOLICITUD_NO_ENCONTRADA", "La solicitud no existe.");
        if (s.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(SolicitudCatalogo), s.Id);
        s.Resolver(solicitudes.ActorId, solicitudes.Actor, clock.UtcNow, request.Autorizar, request.Motivo);
        ImportacionLoteDto? lote = null;
        if (request.Autorizar)
        {
            var actuales = await db.Cuentas.AsNoTracking().OrderBy(c => c.Codigo).ToListAsync(cancellationToken);
            if (s.HuellaCatalogo != SolicitudesCatalogo.Huella(actuales))
                throw new ConflictException("CONTAB_SOLICITUD_CATALOGO_CAMBIO",
                    "El catálogo cambió desde que se preparó la solicitud. Rechácela y prepare otra para revisar las diferencias vigentes.");
            var ids = SolicitudCatalogoDto.De(s).Cambios.ToDictionary(c => c.Despues.Codigo, c => c.Despues.Id);
            T Leer<T>() => JsonSerializer.Deserialize<T>(s.ComandoJson, SolicitudesCatalogo.Json)!;
            switch (s.Operacion)
            {
                case "Alta":
                    var alta = Leer<CrearCuentaCommand>();
                    await new CrearCuentaHandler(db, formato, solicitudes).AplicarAsync(alta, cancellationToken, ids[FormatoCatalogo.Codigo(alta.Codigo)!]);
                    break;
                case "Cambio": await new EditarCuentaHandler(db, formato, solicitudes).AplicarAsync(Leer<EditarCuentaCommand>(), cancellationToken); break;
                case "Baja": await new DesactivarCuentaHandler(db, solicitudes).AplicarAsync(Leer<DesactivarCuentaCommand>(), cancellationToken); break;
                case "Reactivacion": await new ReactivarCuentaHandler(db, solicitudes).AplicarAsync(Leer<ReactivarCuentaCommand>(), cancellationToken); break;
                case "Importacion":
                    var importacion = Leer<AplicarImportacionCommand>();
                    var (analisis, _) = await AnalisisImportacion.EjecutarAsync(db, formato, importacion.Cuerpo, cancellationToken);
                    if (!analisis.PuedeAplicar) throw new BusinessRuleException("CONTAB_IMPORT_YA_NO_VALIDA",
                        "La importación ya no cumple las reglas vigentes. Rechace la solicitud y genere una nueva vista previa.");
                    var resultado = await new AplicarImportacionHandler(db, formato, usuario, clock, log, solicitudes)
                        .PersistirAsync(analisis, importacion.Cuerpo, cancellationToken, ids);
                    lote = resultado.Lote;
                    break;
                default: throw new InvalidOperationException("Operación de catálogo no admitida.");
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return SolicitudCatalogoDto.De(s) with { Lote = lote };
    }
}

public sealed record ObtenerSolicitudCatalogoQuery(Guid Id) : IRequest<SolicitudCatalogoDto>;
public sealed class ObtenerSolicitudCatalogoHandler(ContabilidadDbContext db) : IRequestHandler<ObtenerSolicitudCatalogoQuery, SolicitudCatalogoDto>
{
    public async Task<SolicitudCatalogoDto> Handle(ObtenerSolicitudCatalogoQuery request, CancellationToken cancellationToken)
    {
        var solicitud = await db.SolicitudesCatalogo.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_SOLICITUD_NO_ENCONTRADA", "La solicitud no existe.");
        return SolicitudCatalogoDto.De(solicitud);
    }
}
