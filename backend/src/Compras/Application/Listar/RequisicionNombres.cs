using Millet.Compras.Domain.Ports.Administracion;
using Millet.Compras.Domain.Ports.Identidad;

namespace Millet.Compras.Application.Listar;

/// <summary>
/// Enriquece una página de <see cref="RequisicionListItemResponse"/> con los
/// nombres del requisitante y del departamento resueltos en el backend
/// (ADR-0042). Dos consultas batch (usuarios + departamentos) por página,
/// sin N+1; fallback al id cuando una clave no se resuelve.
/// </summary>
internal static class RequisicionNombres
{
    public static async Task<IReadOnlyList<RequisicionListItemResponse>> EnriquecerAsync(
        IReadOnlyList<RequisicionListItemResponse> items,
        IUsuarioReadPort usuarios,
        IDepartamentoReadPort departamentos,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var nombres = await usuarios.ObtenerNombresAsync(
            items.Select(i => i.RequisitanteId).ToArray(), cancellationToken);
        var deptos = await departamentos.ObtenerAsync(
            items.Select(i => i.DepartamentoId).ToArray(), cancellationToken);

        return items.Select(i =>
        {
            deptos.TryGetValue(i.DepartamentoId, out var depto);
            return i with
            {
                RequisitanteNombre = nombres.GetValueOrDefault(i.RequisitanteId),
                DepartamentoNombre = depto?.Nombre,
                DepartamentoClave = depto?.Clave,
            };
        }).ToList();
    }
}
