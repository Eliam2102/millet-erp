using System.Data;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Series;

/// <summary>
/// Reserva atómicamente el siguiente folio para una combinación
/// (Empresa, Sucursal?, TipoDocumento) en una fecha de referencia
/// (F-Admin-PR6.1).
///
/// <para>
/// Idempotencia: el caller (endpoint API) pasa por el middleware de
/// <c>Idempotency-Key</c> (ADR-0020). Una reserva consume el folio una
/// sola vez; retries con la misma key devuelven la respuesta cacheada.
/// </para>
///
/// <para>
/// Atomicidad: el handler abre una transacción y usa UPSERT atómico
/// (<c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>) sobre la
/// tabla <c>compartido.secuencias_folio</c>. La unicidad por
/// <c>(SerieId, PeriodoClave)</c> hace que el ON CONFLICT funcione
/// bajo cualquier concurrencia. Equivalente funcional a SELECT FOR
/// UPDATE pero sin la ida-vuelta extra al SELECT cuando la fila no
/// existe.
/// </para>
///
/// <para>
/// Errores:
/// <list type="bullet">
///   <item>422 <c>SERIE_NO_CONFIGURADA</c> si no existe Serie activa para
///         (Empresa, Sucursal, TipoDocumento). Si hay serie pero está
///         inactiva, también se considera "no configurada".</item>
/// </list>
/// </para>
/// </summary>
public sealed record ReservarFolioCommand(
    Guid EmpresaId,
    Guid? SucursalId,
    TipoDocumentoSerie TipoDocumento,
    DateOnly FechaReferencia) : IRequest<ReservarFolioResponse>;

public sealed class ReservarFolioValidator : AbstractValidator<ReservarFolioCommand>
{
    public ReservarFolioValidator()
    {
        RuleFor(c => c.EmpresaId).NotEmpty();
        RuleFor(c => c.TipoDocumento).IsInEnum();
    }
}

public sealed class ReservarFolioHandler
    : IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>
{
    private readonly CompartidoDbContext _db;

    public ReservarFolioHandler(CompartidoDbContext db) => _db = db;

    public async Task<ReservarFolioResponse> Handle(
        ReservarFolioCommand command, CancellationToken cancellationToken)
    {
        // Resolver la Serie activa. Estrategia: buscar primero con
        // SucursalId del request; si no hay, caer al match cross-sucursal
        // (SucursalId IS NULL). Permite que una empresa tenga una serie
        // específica por sucursal y un fallback general.
        Serie? serie = null;
        if (command.SucursalId is Guid sucId)
        {
            serie = await _db.Series.AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.EmpresaId == command.EmpresaId
                    && s.SucursalId == sucId
                    && s.TipoDocumento == command.TipoDocumento
                    && s.Activa,
                    cancellationToken);
        }
        serie ??= await _db.Series.AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.EmpresaId == command.EmpresaId
                && s.SucursalId == null
                && s.TipoDocumento == command.TipoDocumento
                && s.Activa,
                cancellationToken);

        if (serie is null)
        {
            throw new BusinessRuleException(
                "SERIE_NO_CONFIGURADA",
                $"No existe una serie activa para EmpresaId={command.EmpresaId}, " +
                $"SucursalId={command.SucursalId?.ToString() ?? "null"}, " +
                $"TipoDocumento={command.TipoDocumento}. " +
                "Configúrala en Administración → Series.");
        }

        var periodoClave = Serie.CalcularPeriodoClave(serie.ReinicioPeriodo, command.FechaReferencia);

        // Reserva atómica vía UPSERT con RETURNING (mismo patrón que
        // CrearOrdenCompraVaciaHandler.GetNextFolioSequenceAsync). El
        // índice UNIQUE (serie_id, periodo_clave) garantiza que el
        // ON CONFLICT enrute al UPDATE bajo concurrencia. Si la fila
        // no existe, la INSERT crea con ultimo_numero=1 y retorna 1; si
        // ya existe, la UPDATE incrementa y retorna el nuevo valor.
        var nuevoId = Guid.CreateVersion7();
        var nowUtc = DateTimeOffset.UtcNow;
        const string seedBy = "reservar-folio";

        // UPSERT con RETURNING. EF Core trata params como object[]; los
        // nulables tipo Guid? se materializan a object via las
        // conversiones implícitas. Mantenemos los params no-null para
        // satisfacer nullable static analysis.
        var sql = $@"
            INSERT INTO compartido.secuencias_folio
                (id, serie_id, periodo_clave, ultimo_numero,
                 version, created_at, updated_at, created_by, updated_by, deleted_at)
            VALUES
                ({{0}}, {{1}}, {{2}}, 1, 1, {{3}}, {{3}}, {{4}}, {{4}}, NULL)
            ON CONFLICT (serie_id, periodo_clave) DO UPDATE
              SET ultimo_numero = compartido.secuencias_folio.ultimo_numero + 1,
                  updated_at = {{3}},
                  version = compartido.secuencias_folio.version + 1
            RETURNING ultimo_numero AS ""Value""
        ";

        var result = await _db.Database
            .SqlQueryRaw<long>(sql, nuevoId, serie.Id, periodoClave, nowUtc, (object)seedBy)
            .ToListAsync(cancellationToken);
        var numero = result.Single();

        var folio = FormatearFolio(serie, periodoClave, numero);
        return new ReservarFolioResponse(folio, numero, periodoClave);
    }

    internal static string FormatearFolio(Serie serie, string periodoClave, long numero)
    {
        var numStr = numero.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        if (serie.ReinicioPeriodo == ReinicioPeriodo.None)
        {
            var sufijo = serie.Sufijo ?? string.Empty;
            return $"{serie.Prefijo}{sufijo}-{numStr}";
        }
        return $"{serie.Prefijo}-{periodoClave}-{numStr}";
    }
}
