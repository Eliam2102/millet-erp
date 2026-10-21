using System.Linq.Expressions;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain;

/// <summary>
/// Qué artículos admite cada <see cref="Clasificacion"/> de requisición
/// (86e3tfedd, sesión Millet 19-oct: "sólo los artículos dados de alta para
/// ese tipo").
///
/// <para>Regla:</para>
/// <list type="bullet">
///   <item>Categoría mapeada → sólo su clasificación.</item>
///   <item>Sin categoría mapeada: <see cref="Naturaleza.Servicio"/> → sólo
///   <see cref="Clasificacion.Servicio"/>; cualquier otra → sólo
///   <see cref="Clasificacion.OrdenCompra"/>.</item>
///   <item>Materia prima y Pinturas sólo aceptan categorías mapeadas.</item>
/// </list>
/// </summary>
public static class ArticulosPorClasificacion
{
    // PLATFORM-TODO(<ClasificacionArticulosM3>): mapeo DEMO por nombre de
    // categoría hasta que Millet mande su clasificación (M3). Si Millet lo
    // quiere administrable, pasa a columna en compartido.categorias_articulo.
    private static readonly Dictionary<string, Clasificacion> CategoriasDemo = new()
    {
        ["INSUMOS PRODUCCION"] = Clasificacion.MateriaPrima,
        ["Químicos"] = Clasificacion.MateriaPrima,
        ["Pinturas"] = Clasificacion.Pinturas,
        ["Servicios técnicos"] = Clasificacion.Servicio,
        ["Servicios profesionales"] = Clasificacion.Servicio,
        ["MATER. DE OFICINA"] = Clasificacion.OrdenCompra,
        ["PRENDAS SEGURIDAD"] = Clasificacion.OrdenCompra,
        ["MAT DE LIMPIEZA"] = Clasificacion.OrdenCompra,
        ["SUMI. LABORATORIO"] = Clasificacion.OrdenCompra,
        ["MEDICINAS Y FARMACIA"] = Clasificacion.OrdenCompra,
        ["IMPRESIÓN Y DIGITAL"] = Clasificacion.OrdenCompra,
        ["MAT DE TI Y COMUNIC"] = Clasificacion.OrdenCompra,
        ["Refacciones"] = Clasificacion.OrdenCompra,
        ["Equipo eléctrico"] = Clasificacion.OrdenCompra,
        ["Materiales peligrosos"] = Clasificacion.OrdenCompra,
    };

    private static readonly string[] Mapeadas = CategoriasDemo.Keys.ToArray();

    public static bool Aplica(Clasificacion clasificacion, string? categoria, Naturaleza naturaleza)
    {
        if (categoria is not null && CategoriasDemo.TryGetValue(categoria, out var mapeada))
            return mapeada == clasificacion;

        return clasificacion switch
        {
            Clasificacion.Servicio => naturaleza == Naturaleza.Servicio,
            Clasificacion.OrdenCompra => naturaleza != Naturaleza.Servicio,
            _ => false,
        };
    }

    /// <summary>422 <c>ARTICULO_NO_APLICA_A_CLASIFICACION</c> si el artículo no es del tipo de la RQ.</summary>
    public static void Validar(Clasificacion clasificacion, Articulo articulo)
    {
        if (!Aplica(clasificacion, articulo.Categoria, articulo.Naturaleza))
            throw new BusinessRuleException(
                "ARTICULO_NO_APLICA_A_CLASIFICACION",
                $"El artículo '{articulo.Clave}' no corresponde a una requisición de {Nombre(clasificacion)}.");
    }

    public static string Nombre(Clasificacion clasificacion) => clasificacion switch
    {
        Clasificacion.Servicio => "servicio",
        Clasificacion.OrdenCompra => "orden de compra",
        Clasificacion.MateriaPrima => "materia prima",
        Clasificacion.Pinturas => "pinturas",
        _ => clasificacion.ToString(),
    };

    /// <summary>Misma regla que <see cref="Aplica"/>, traducible a SQL por EF.</summary>
    public static Expression<Func<Articulo, bool>> Filtro(Clasificacion clasificacion)
    {
        var propias = CategoriasDemo.Where(kv => kv.Value == clasificacion).Select(kv => kv.Key).ToArray();
        var mapeadas = Mapeadas;

        return clasificacion switch
        {
            Clasificacion.Servicio => a =>
                (a.Categoria != null && propias.Contains(a.Categoria))
                || ((a.Categoria == null || !mapeadas.Contains(a.Categoria)) && a.Naturaleza == Naturaleza.Servicio),
            Clasificacion.OrdenCompra => a =>
                (a.Categoria != null && propias.Contains(a.Categoria))
                || ((a.Categoria == null || !mapeadas.Contains(a.Categoria)) && a.Naturaleza != Naturaleza.Servicio),
            _ => a => a.Categoria != null && propias.Contains(a.Categoria),
        };
    }
}
