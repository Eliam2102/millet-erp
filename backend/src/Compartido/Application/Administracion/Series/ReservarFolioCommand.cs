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
    private readonly SerieSucursalScope _scope;
    private readonly Millet.SharedKernel.Application.IClock _clock;

    public ReservarFolioHandler(CompartidoDbContext db, SerieSucursalScope scope, Millet.SharedKernel.Application.IClock clock)
    {
        _db = db;
        _scope = scope;
        _clock = clock;
    }

    public async Task<ReservarFolioResponse> Handle(
        ReservarFolioCommand command, CancellationToken cancellationToken)
    {
        _scope.VerificarEmpresa(command.EmpresaId);

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        // Resolver la Serie activa. Estrategia: buscar primero con
        // SucursalId del request; si no hay, caer al match cross-sucursal
        // (SucursalId IS NULL). Permite que una empresa tenga una serie
        // específica por sucursal y un fallback general.
        Guid? serieId = null;
        if (command.SucursalId is Guid sucId)
        {
            const string sqlSucursal = """
                SELECT id AS "Value"
                FROM compartido.series
                WHERE empresa_id = {0}
                  AND sucursal_id = {1}
                  AND tipo_documento = {2}
                  AND activa
                ORDER BY id
                LIMIT 2
                FOR UPDATE
                """;
            var candidatas = await _db.Database.SqlQueryRaw<Guid>(
                sqlSucursal, command.EmpresaId, sucId, (short)command.TipoDocumento).ToListAsync(cancellationToken);
            if (candidatas.Count > 1) throw new BusinessRuleException("SERIE_AMBIGUA", "Hay varias series activas. Corrige el catálogo antes de reservar.");
            serieId = candidatas.Count == 1 ? candidatas[0] : null;
        }
        if (serieId is null || serieId == Guid.Empty)
        {
            const string sqlGlobal = """
                SELECT id AS "Value"
                FROM compartido.series
                WHERE empresa_id = {0}
                  AND sucursal_id IS NULL
                  AND tipo_documento = {1}
                  AND activa
                ORDER BY id
                LIMIT 2
                FOR UPDATE
                """;
            var candidatas = await _db.Database.SqlQueryRaw<Guid>(
                sqlGlobal, command.EmpresaId, (short)command.TipoDocumento).ToListAsync(cancellationToken);
            if (candidatas.Count > 1) throw new BusinessRuleException("SERIE_AMBIGUA", "Hay varias series globales activas. Corrige el catálogo antes de reservar.");
            serieId = candidatas.Count == 1 ? candidatas[0] : null;
        }

        var serie = serieId is Guid id && id != Guid.Empty
            ? await _db.Series.AsNoTracking().SingleAsync(s => s.Id == id, cancellationToken)
            : null;

        if (serie is null)
        {
            throw new BusinessRuleException(
                "SERIE_NO_CONFIGURADA",
                $"No existe una serie activa para EmpresaId={command.EmpresaId}, " +
                $"SucursalId={command.SucursalId?.ToString() ?? "null"}, " +
                $"TipoDocumento={command.TipoDocumento}. " +
                "Configúrala en Administración → Series.");
        }

        if (Serie.EsFiscal(serie.TipoDocumento) && serie.ReinicioPeriodo != ReinicioPeriodo.None)
            throw new BusinessRuleException("SERIE_FISCAL_CONTINUIDAD_PENDIENTE",
                "La serie fiscal heredada tiene reinicio. Fiscal debe conciliar su continuidad y configurar un reemplazo antes de reservar.");

        var periodoClave = Serie.CalcularPeriodoClave(serie.ReinicioPeriodo, command.FechaReferencia);

        // Reserva atómica vía UPSERT con RETURNING (mismo patrón que
        // CrearOrdenCompraVaciaHandler.GetNextFolioSequenceAsync). El
        // índice UNIQUE (serie_id, periodo_clave) garantiza que el
        // ON CONFLICT enrute al UPDATE bajo concurrencia. Si la fila
        // no existe, la INSERT crea con ultimo_numero=1 y retorna 1; si
        // ya existe, la UPDATE incrementa y retorna el nuevo valor.
        var nuevoId = Guid.CreateVersion7();
        var nowUtc = _clock.UtcNow;
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
                ({{0}}, {{1}}, {{2}}, {{5}}, 1, {{3}}, {{3}}, {{4}}, {{4}}, NULL)
            ON CONFLICT (serie_id, periodo_clave) DO UPDATE
              SET ultimo_numero = compartido.secuencias_folio.ultimo_numero + 1,
                  updated_at = {{3}},
                  version = compartido.secuencias_folio.version + 1
            RETURNING ultimo_numero AS ""Value""
        ";

        var result = await _db.Database
            .SqlQueryRaw<long>(sql, nuevoId, serie.Id, periodoClave, nowUtc, (object)seedBy, serie.FolioInicial)
            .ToListAsync(cancellationToken);
        var numero = result.Single();

        var folio = FormatearFolio(serie, periodoClave, numero);
        await transaction.CommitAsync(cancellationToken);
        return new ReservarFolioResponse(folio, numero, periodoClave, serie.Id);
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
