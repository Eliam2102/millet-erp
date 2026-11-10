using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>
/// Guarda de forma atómica las salidas de la app de Almacenes (movimientos + <see cref="MensajeApp"/>).
/// El endpoint es anónimo (llave de API) y no hay usuario autenticado, por lo que no existe
/// <c>EmpresaContext.Current</c>: aquí vive el <c>Bypass()</c>, acotado a este guardado y con la empresa y el
/// usuario del usuario de servicio pasados de forma explícita (ADR-0039, categoría adapters; mismo precedente
/// que <c>ComprasCrearRqAppAdapter</c>). Los endpoints no pueden usar <c>Bypass()</c> (BypassUsageGuardTest).
/// </summary>
public sealed class SalidaAppAlmacenesGuardado
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IIntegrationEventPublisher _events;
    private readonly EntradaAppPuertos _entrada;

    public SalidaAppAlmacenesGuardado(AlmacenDbContext db, ICurrentEmpresaContext empresa, IIntegrationEventPublisher events,
        IComprasOcReadPort ocs, IConversionUnidadPort conversion, IPeriodoContableReadPort periodo)
    {
        _db = db; _empresa = empresa; _events = events;
        _entrada = new EntradaAppPuertos(ocs, conversion, periodo, events);
    }

    /// <summary>
    /// Registra las salidas y el mensaje en una transacción (solo Npgsql: el folio del movimiento la exige).
    /// <c>Duplicado</c> = carrera con otro
    /// envío del mismo mensajeId (23505): se revierte y el llamador responde DUPLICADO.
    /// </summary>
    public Task<(string Estado, string? Folio, string? Error, bool Duplicado)> GuardarAsync(
        string mensajeId, string tipo, string? referencia, string payload,
        Guid empresaId, Guid usuarioId, IReadOnlyList<SalidaApp> salidas, CancellationToken ct) =>
        GuardarAsync(mensajeId, tipo, referencia, payload,
            () => SalidaAppAlmacenes.RegistrarAsync(_db, _events, empresaId, usuarioId, salidas, ct), ct);

    /// <summary>
    /// ENTRADA_ALMACEN (M-159): mismo guardado atómico que las salidas; un ERROR deja el mensaje con folio de revisión.
    /// Contra una OC del ERP es su recepción (movimiento, saldo y evento al outbox en la misma transacción).
    /// </summary>
    public Task<(string Estado, string? Folio, string? Error, bool Duplicado)> GuardarEntradaAsync(
        string mensajeId, string tipo, string? referencia, string payload, Guid empresaId, Guid usuarioId, Guid sucursalId,
        string motivo, string? compra, string? entrada, IReadOnlyList<EntradaApp> entradas, CancellationToken ct) =>
        GuardarAsync(mensajeId, tipo, referencia, payload,
            () => EntradaAppAlmacenes.RegistrarAsync(_db, _entrada, empresaId, usuarioId, sucursalId, motivo,
                compra, entrada ?? mensajeId, entradas, ct), ct);

    private async Task<(string Estado, string? Folio, string? Error, bool Duplicado)> GuardarAsync(
        string mensajeId, string tipo, string? referencia, string payload,
        Func<Task<(string Estado, string? Folio, string? Error)>> registrar, CancellationToken ct)
    {
        using var bypass = _empresa.Bypass();
        await using var tx = _db.Database.IsNpgsql() ? await _db.Database.BeginTransactionAsync(ct) : null;

        var (estado, folio, error) = await registrar();
        if (estado is SalidaAppAlmacenes.EnRevision or "ERROR") folio = SalidaAppAlmacenes.FolioRevision(mensajeId);

        _db.MensajesApp.Add(new MensajeApp(mensajeId, tipo,
            referencia?.Length > 100 ? referencia[..100] : referencia, payload, estado, folio, error));
        try
        {
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return (estado, folio, error, false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return (estado, folio, error, true);
        }
    }
}
