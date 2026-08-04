namespace Millet.Compras.Domain;

/// <summary>
/// Resultado de <see cref="Requisicion.RegistrarAutorizacion"/>:
/// la autorización registrada y, si la matriz quedó satisfecha,
/// el <see cref="Events.MatrizAprobacionSatisfechaEvent"/> que el
/// handler debe publicar post-commit.
///
/// <para>
/// <see cref="MatrizSatisfecha"/> es <c>null</c> cuando la
/// autorización no completa la matriz (ej. registro de N1 con
/// <c>RequiereN1YN2</c>); en ese caso la requisición sigue en
/// <see cref="EstadoRequisicion.EnAutorizacion"/> esperando N2.
/// </para>
/// </summary>
public sealed record RegistrarAutorizacionResultado(
    Autorizacion Autorizacion,
    Events.MatrizAprobacionSatisfechaEvent? MatrizSatisfecha);
