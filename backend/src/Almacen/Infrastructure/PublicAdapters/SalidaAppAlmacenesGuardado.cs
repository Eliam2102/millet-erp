using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
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
    private readonly CancelarRecepcionHandler _cancelar;

    public SalidaAppAlmacenesGuardado(AlmacenDbContext db, ICurrentEmpresaContext empresa, IIntegrationEventPublisher events,
        IComprasOcReadPort ocs, IConversionUnidadPort conversion, IPeriodoContableReadPort periodo, CancelarRecepcionHandler cancelar)
    {
        _db = db; _empresa = empresa; _events = events; _cancelar = cancelar;
        _entrada = new EntradaAppPuertos(ocs, conversion, periodo, events);
    }

    /// <summary>
    /// Registra las salidas y el mensaje en una transacción (solo Npgsql: el folio del movimiento la exige).
    /// <c>Duplicado</c> = carrera con otro
    /// envío del mismo mensajeId (23505): se revierte y el llamador responde DUPLICADO.
    /// </summary>
    public Task<(string Estado, string? Folio, string? Error, bool Duplicado)> GuardarAsync(
        string mensajeId, string tipo, string? referencia, string payload,
        Guid empresaId, Guid usuarioId, IReadOnlyList<SalidaApp> salidas, CancellationToken ct, OrigenApp? origen = null) =>
        GuardarAsync(mensajeId, tipo, referencia, payload,
            () => SalidaAppAlmacenes.RegistrarAsync(_db, _events, empresaId, usuarioId, salidas, ct, origen), ct);

    /// <summary>
    /// ENTRADA_ALMACEN (M-159) y ENTRADA_MANUAL (v9, <paramref name="tipoMovimiento"/> = AjustePositivo): mismo guardado atómico
    /// que las salidas; un ERROR deja el mensaje con folio de revisión. Contra una OC del ERP es su recepción (movimiento, saldo y
    /// evento al outbox en la misma transacción).
    /// </summary>
    public Task<(string Estado, string? Folio, string? Error, bool Duplicado)> GuardarEntradaAsync(
        string mensajeId, string tipo, string? referencia, string payload, Guid empresaId, Guid usuarioId, Guid sucursalId,
        string motivo, string? compra, string? entrada, IReadOnlyList<EntradaApp> entradas, CancellationToken ct,
        TipoMovimiento tipoMovimiento = TipoMovimiento.EntradaCompra, OrigenApp? origen = null, string? motivoClave = null) =>
        GuardarAsync(mensajeId, tipo, referencia, payload,
            () => EntradaAppAlmacenes.RegistrarAsync(_db, _entrada, empresaId, usuarioId, sucursalId, motivo,
                compra, entrada ?? mensajeId, entradas, ct, tipoMovimiento, origen, motivoClave), ct);

    /// <summary>
    /// SALIDA_MANUAL con <c>origen.tipo = CANCELACION_ENTRADA</c> (v9): si la entrada <paramref name="entrada"/> (EN-…) fue una recepción
    /// de una OC del ERP (se marcó con <see cref="EntradaAppAlmacenes.ReferenciaEntrada"/>), la cancela con la misma lógica que la pantalla
    /// (<see cref="CancelarRecepcionHandler.CancelarAsync"/>): contramovimientos CEN al mismo costo, entradas Canceladas y evento
    /// <c>almacen.recepcion_oc.cancelada.v1</c> (Compras devuelve lo pendiente a la OC, CxP desliga la factura, Contabilidad anula la póliza).
    /// Devuelve null si el ERP no tiene esa recepción: el llamador sigue con la salida manual de siempre. Ya cancelada = DUPLICADO con los
    /// folios de la cancelación. Una regla rota (salidas posteriores, factura pagada, periodo de hoy cerrado…) es ERROR sin mover nada.
    /// </summary>
    public async Task<(string Estado, string? Folio, string? Error, bool Duplicado)?> CancelarEntradaAsync(
        string mensajeId, string tipo, string? referencia, string payload, Guid empresaId, Guid usuarioId,
        string entrada, string? motivo, CancellationToken ct)
    {
        using var bypass = _empresa.Bypass();
        var marca = EntradaAppAlmacenes.ReferenciaEntrada(entrada.Trim());
        var recepciones = await _db.Movimientos.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimiento.EntradaCompra && m.OcId != null && m.RecepcionOrigenId == null && m.PackingListBlobRef == marca)
            .Select(m => new { m.Id, m.Estado }).ToListAsync(ct);
        if (recepciones.Count == 0) return null;

        var activa = recepciones.FirstOrDefault(r => r.Estado == EstadoMovimiento.Registrado);
        var texto = string.IsNullOrWhiteSpace(motivo) ? "Cancelación de entrada desde la app de Almacenes" : motivo.Trim();
        if (texto.Length > MotivosCancelacionRecepcion.TextoMaximo) texto = texto[..MotivosCancelacionRecepcion.TextoMaximo];

        await using var tx = _db.Database.IsNpgsql() ? await _db.Database.BeginTransactionAsync(ct) : null;
        string estado, folio; string? error = null;
        try
        {
            if (activa is null) throw new BusinessRuleException("RECEPCION_YA_CANCELADA", "ya cancelada");
            var r = await _cancelar.CancelarAsync(activa.Id, empresaId, usuarioId, "OTRO", texto, desdeApp: true, ct);
            (estado, folio) = (SalidaAppAlmacenes.Recibido, string.Join(",", r.FoliosCancelacion));
        }
        catch (BusinessRuleException ex) when (ex.Code == "RECEPCION_YA_CANCELADA")
        {
            // Reenvío con otro mensajeId (o carrera): idempotente, con los folios de la cancelación que ya existe.
            if (tx is not null) await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var ids = recepciones.Select(x => (Guid?)x.Id).ToList();
            var folios = await _db.Movimientos.AsNoTracking()
                .Where(m => m.Tipo == TipoMovimiento.CancelacionEntradaCompra && ids.Contains(m.RecepcionOrigenId)).Select(m => m.Folio!).ToListAsync(ct);
            (estado, folio) = ("DUPLICADO", string.Join(",", folios));
            return await GuardarMensajeAsync(mensajeId, tipo, referencia, payload, estado, folio, null, null, ct);
        }
        catch (BusinessRuleException ex)
        {
            // Lo ya guardado dentro de la cancelación (saveChanges parciales) se descarta: ERROR no mueve existencia.
            if (tx is not null) await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return await GuardarMensajeAsync(mensajeId, tipo, referencia, payload, "ERROR",
                SalidaAppAlmacenes.FolioRevision(mensajeId), ex.Message, null, ct);
        }
        return await GuardarMensajeAsync(mensajeId, tipo, referencia, payload, estado, folio, error, tx, ct);
    }

    private async Task<(string Estado, string? Folio, string? Error, bool Duplicado)> GuardarMensajeAsync(
        string mensajeId, string tipo, string? referencia, string payload, string estado, string? folio, string? error,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx, CancellationToken ct)
    {
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

    /// <summary>CAMBIO_ALMACEN (M-446): el par salida/entrada del traslado y el mensaje en una transacción; sin evento contable.</summary>
    public Task<(string Estado, string? Folio, string? Error, bool Duplicado)> GuardarTrasladoAsync(
        string mensajeId, string tipo, string? referencia, string payload, Guid empresaId, Guid usuarioId,
        IReadOnlyList<SalidaApp> origen, LlaveAlmacenApp destino, CancellationToken ct) =>
        GuardarAsync(mensajeId, tipo, referencia, payload,
            () => TrasladoAppAlmacenes.RegistrarAsync(_db, empresaId, usuarioId, origen, destino, ct), ct);

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
