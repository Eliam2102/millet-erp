using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Catalogo;

/// <summary>
/// Guard compartido por los handlers de ENTRADA que capturan bin explícito
/// (ADR-0047 C7.2b): recepción con factura, recepción con packing list,
/// devolución interna y reincorporación tras revisión. En un solo lookup
/// resuelve las cuatro reglas del modelo de negocio para una línea de entrada:
///
/// <list type="number">
///   <item>La ubicación existe (<c>UBICACION_NO_ENCONTRADA</c>).</item>
///   <item>Pertenece al sub-almacén del movimiento —solo si se pasa uno
///   (dev-interna)— (<c>UBICACION_NO_PERTENECE_AL_SUBALMACEN</c>). Recepción no
///   lo pasa: deriva el sub del bin y valida el invariante entre líneas.</item>
///   <item>No es la ÚNICA/default — las entradas exigen ubicación real
///   (<c>ENTRADA_A_UBICACION_UNICA</c>).</item>
///   <item>El artículo está asignado y activo a esa ubicación
///   (<c>ENTRADA_SIN_ASIGNACION</c>), molde de
///   <c>ConfiguracionReordenCommands.ValidarAsignacionExisteAsync</c>.</item>
/// </list>
///
/// <para>El trigger PG <c>tg_movimientos_actualizar_saldo</c> blinda 1–3 como
/// backstop (ERRCODE 23514); este guard las adelanta con Problem Details limpio
/// (RFC 7807) antes de tocar la BD. La regla de asignación (4) vive solo aquí:
/// es política de negocio, no del ledger.</para>
/// </summary>
public static class UbicacionEntradaGuard
{
    /// <summary>
    /// Valida el bin de una línea de entrada y RETORNA el sub-almacén al que
    /// pertenece (derivado del bin). Si <paramref name="subEsperado"/> se pasa
    /// (dev-interna / reincorporación), además exige que el bin pertenezca a ese
    /// sub (regla <c>UBICACION_NO_PERTENECE_AL_SUBALMACEN</c>, en su posición
    /// original). Recepción NO lo pasa: no hay sub de cabecera; el invariante
    /// "un movimiento = un sub" lo valida el handler comparando el sub derivado
    /// de todas las líneas (<c>RECEPCION_MULTI_SUBALMACEN</c>).
    /// </summary>
    public static async Task<Guid> ValidarEntradaYDerivarSubAsync(
        AlmacenDbContext db,
        Guid ubicacionId,
        Guid articuloId,
        CancellationToken ct,
        Guid? subEsperado = null)
    {
        var ubicacion = await db.Ubicaciones.AsNoTracking()
            .Where(u => u.Id == ubicacionId)
            .Select(u => new { u.SubAlmacenId, u.EsDefault })
            .FirstOrDefaultAsync(ct);

        if (ubicacion is null)
            throw new EntityNotFoundException(
                "UBICACION_NO_ENCONTRADA",
                $"No existe ubicación con id '{ubicacionId}'.");

        // Regla 2 (solo dev-interna): pertenencia al sub explícito. En su
        // posición original (tras existencia) para no alterar su comportamiento.
        if (subEsperado is Guid sub && ubicacion.SubAlmacenId != sub)
            throw new BusinessRuleException(
                "UBICACION_NO_PERTENECE_AL_SUBALMACEN",
                "La ubicación elegida no pertenece al sub-almacén del movimiento.");

        if (ubicacion.EsDefault)
            throw new BusinessRuleException(
                "ENTRADA_A_UBICACION_UNICA",
                "Las entradas exigen una ubicación real; la ÚNICA solo se drena por salidas.");

        var tieneAsignacion = await db.AsignacionesArticuloUbicacion.AsNoTracking()
            .AnyAsync(
                a => a.UbicacionId == ubicacionId
                  && a.ArticuloId == articuloId
                  && a.Estatus == EstatusCatalogo.Activo,
                ct);

        if (!tieneAsignacion)
            throw new BusinessRuleException(
                "ENTRADA_SIN_ASIGNACION",
                "El artículo no está asignado a la ubicación elegida. Asígnalo primero.");

        return ubicacion.SubAlmacenId;
    }

    /// <summary>
    /// Camino con sub explícito (dev-interna / reincorporación): valida los 4,
    /// incluida la pertenencia al sub. Firma intacta para no tocar sus callers.
    /// </summary>
    public static Task ValidarUbicacionEntradaAsync(
        AlmacenDbContext db,
        Guid ubicacionId,
        Guid subAlmacenId,
        Guid articuloId,
        CancellationToken ct)
        => ValidarEntradaYDerivarSubAsync(db, ubicacionId, articuloId, ct, subEsperado: subAlmacenId);
}
