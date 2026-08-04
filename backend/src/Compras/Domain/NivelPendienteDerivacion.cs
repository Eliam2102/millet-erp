namespace Millet.Compras.Domain;

/// <summary>
/// Fuente ÚNICA del nivel de autorización pendiente de una RQ (PR-A). Función
/// pura: dado el estado y si existen las firmas N1/N2, devuelve el siguiente
/// nivel que falta firmar, o <c>null</c> si la RQ no está
/// <see cref="EstadoRequisicion.EnAutorizacion"/>.
///
/// <para>NO es un estado de la máquina (no toca <see cref="EstadoRequisicion"/>
/// ni las transiciones): el estado se queda en <c>EnAutorizacion</c> mientras
/// falte N1 o N2; este derivado solo dice CUÁL falta, para la bandeja de
/// pendientes (mostrar + filtrar). La firma es única por nivel (UNIQUE
/// RequisicionId+Nivel), así que basta saber si cada nivel existe.</para>
///
/// <para>La consume el list-query de pendientes (los 2 flags se resuelven en
/// SQL vía EXISTS escalar sobre <c>Autorizaciones</c>, sin materializar la
/// colección). Mismo patrón que <see cref="SituacionSurtidoDerivacion"/>.</para>
/// </summary>
public static class NivelPendienteDerivacion
{
    /// <summary>
    /// Deriva el nivel pendiente. Reglas: fuera de <c>EnAutorizacion</c> ⇒
    /// <c>null</c>; sin N1 ⇒ falta N1; con N1 y sin N2 ⇒ falta N2; con ambos ⇒
    /// <c>null</c> (defensivo: con N1+N2 la RQ ya no estaría EnAutorizacion).
    /// </summary>
    /// <param name="estado">Estado actual de la RQ.</param>
    /// <param name="tieneN1">¿Existe una firma de Nivel 1 (de cualquiera)?</param>
    /// <param name="tieneN2">¿Existe una firma de Nivel 2 (de cualquiera)?</param>
    public static NivelAutorizacion? Derivar(
        EstadoRequisicion estado,
        bool tieneN1,
        bool tieneN2)
    {
        // El nivel pendiente solo aplica dentro de EnAutorizacion.
        if (estado != EstadoRequisicion.EnAutorizacion)
        {
            return null;
        }

        if (!tieneN1)
        {
            return NivelAutorizacion.Nivel1;
        }

        return !tieneN2 ? NivelAutorizacion.Nivel2 : null;
    }
}
