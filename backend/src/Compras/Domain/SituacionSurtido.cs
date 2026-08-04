namespace Millet.Compras.Domain;

/// <summary>
/// Situación calculada de la RQ dentro de <see cref="EstadoRequisicion.EnSurtido"/>
/// (ADR-0043, "Decisiones de modelado #1": el badge calculado). NO es un estado
/// de la máquina (no toca <see cref="EstadoRequisicion"/> ni las transiciones):
/// se deriva server-side de los agregados de las líneas y solo tiene valor cuando
/// la RQ está <c>EnSurtido</c> (en cualquier otro estado la situación es null).
///
/// <para>Tres valores, con precedencia
/// <c>SurtidoParcial &gt; ListoParaSurtir &gt; EsperandoCompra</c>
/// (ver <see cref="SituacionSurtidoDerivacion.Derivar"/>).</para>
/// </summary>
public enum SituacionSurtido : short
{
    /// <summary>Nada entregado y nada disponible para entregar: todo está en compra
    /// (esperando OC / recepción). Es también el caso por defecto.</summary>
    EsperandoCompra = 0,

    /// <summary>Nada entregado todavía, pero hay material físicamente disponible
    /// para entregar al solicitante (stock reservado y/o recibido sin entregar).</summary>
    ListoParaSurtir = 1,

    /// <summary>Ya se entregó algo al solicitante pero falta (la RQ no está cerrada;
    /// la entrega total auto-cierra a <see cref="EstadoRequisicion.Cerrada"/>).</summary>
    SurtidoParcial = 2,
}

/// <summary>
/// Fuente ÚNICA de la situación de surtido (ADR-0043). Función pura: recibe los
/// agregados por RQ (total entregado, total disponible-sin-entregar, total
/// solicitado) y devuelve la <see cref="SituacionSurtido"/>. La consumen tanto el
/// list-query (sumas calculadas en SQL) como el detalle (sumas en memoria de las
/// líneas del agregado), de modo que no hay drift entre bandeja y detalle.
/// </summary>
public static class SituacionSurtidoDerivacion
{
    /// <summary>
    /// Deriva la situación de surtido de una RQ. Devuelve <c>null</c> si la RQ no
    /// está <see cref="EstadoRequisicion.EnSurtido"/>.
    ///
    /// <para>Reusa el criterio de <c>clasificarEntrega</c> del frontend
    /// (<c>nueva-salida-helpers.ts</c>): <c>entregado &lt;= 0</c> ⇒ sin-entregar;
    /// <c>entregado &gt;= solicitado</c> ⇒ entregada; en medio ⇒ parcial. A nivel
    /// RQ, "entregada" (todo entregado) no debería verse en <c>EnSurtido</c> porque
    /// la entrega total auto-cierra a <c>Cerrada</c>
    /// (<see cref="Requisicion.RegistrarEntrega"/>); se mapea defensivamente a
    /// <see cref="SituacionSurtido.SurtidoParcial"/>.</para>
    /// </summary>
    /// <param name="estado">Estado actual de la RQ.</param>
    /// <param name="sumEntregado">Σ <c>CantidadEntregada</c> de las líneas.</param>
    /// <param name="sumPendienteEntregar">Σ <c>CantidadPendienteEntregar</c> =
    /// Σ max(0, (almacén + recibida) − entregada) de las líneas.</param>
    /// <param name="sumTotal">Σ <c>Cantidad</c> solicitada de las líneas.</param>
    public static SituacionSurtido? Derivar(
        EstadoRequisicion estado,
        decimal sumEntregado,
        decimal sumPendienteEntregar,
        decimal sumTotal)
    {
        // La situación solo aplica dentro de EnSurtido; en otros estados es ausente.
        if (estado != EstadoRequisicion.EnSurtido)
        {
            return null;
        }

        // Eje de entrega (criterio de clasificarEntrega). "entregada"
        // (sumEntregado >= sumTotal) es defensivo: no debería ocurrir en EnSurtido.
        var hayAlgoEntregado = sumEntregado > 0m;
        var todoEntregado = sumTotal > 0m && sumEntregado >= sumTotal;
        if (hayAlgoEntregado || todoEntregado)
        {
            return SituacionSurtido.SurtidoParcial;
        }

        // Sin entregar: ¿hay material físicamente disponible para entregar?
        // (almacén + recibida) − entregada > 0 en alguna línea.
        return sumPendienteEntregar > 0m
            ? SituacionSurtido.ListoParaSurtir
            : SituacionSurtido.EsperandoCompra;
    }
}
