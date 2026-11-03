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
    // Mapa por grupo de artículo de SAP (OITB, carga 3-nov) más las categorías
    // DEMO que siguen en las semillas locales. Supuestos por validar con Compras
    // (noche/2026-11-03/sap-proveedores-y-grupos.md): los grupos (AF) quedan en
    // OrdenCompra por la regla por defecto; 111 PINTURAS Y QUIMICOS → Pinturas;
    // los excluidos (null) no entran a ninguna RQ.
    // PLATFORM-TODO(<ClasificacionArticulosM3>): si Millet lo quiere
    // administrable, pasa a columna en compartido.categorias_articulo.
    // Claves con espacios normalizados (SAP trae dobles espacios, p.ej. «INSUMOS  PRODUCCION»).
    private static readonly Dictionary<string, Clasificacion?> CategoriasDemo = new()
    {
        // Materia prima
        ["INSUMOS PRODUCCION"] = Clasificacion.MateriaPrima,
        ["ALUMINIO"] = Clasificacion.MateriaPrima,
        ["HERRAJES IMPORTACIÓN"] = Clasificacion.MateriaPrima,
        ["HERRAJES NACIONALES"] = Clasificacion.MateriaPrima,
        ["ALUCOM"] = Clasificacion.MateriaPrima,
        ["REYNOBON"] = Clasificacion.MateriaPrima,
        ["ACCESORIOS"] = Clasificacion.MateriaPrima,
        ["EMPAQUE Y EMBALAJE"] = Clasificacion.MateriaPrima,
        ["VIDRIO FLOTADO"] = Clasificacion.MateriaPrima,
        ["VIDRIO TEMPLADO"] = Clasificacion.MateriaPrima,
        ["VIDRIO LAMINADO"] = Clasificacion.MateriaPrima,
        ["VIDRIO INSULADO"] = Clasificacion.MateriaPrima,
        ["SERIGRAFIA TINT/DIG."] = Clasificacion.MateriaPrima,
        ["Químicos"] = Clasificacion.MateriaPrima,
        // Pinturas
        ["PINT.ELEC.QUÍM. (MP)"] = Clasificacion.Pinturas,
        ["PINTURAS Y QUIMICOS"] = Clasificacion.Pinturas,
        ["PRODUCTOS QUÍMICOS"] = Clasificacion.Pinturas,
        ["Pinturas"] = Clasificacion.Pinturas,
        // Servicio
        ["SERVICIOS"] = Clasificacion.Servicio,
        ["FLETES"] = Clasificacion.Servicio,
        ["OBRAS"] = Clasificacion.Servicio,
        ["SERV PUBLICITARIO MX"] = Clasificacion.Servicio,
        ["SERV PUBLICITARIO US"] = Clasificacion.Servicio,
        ["Servicios técnicos"] = Clasificacion.Servicio,
        ["Servicios profesionales"] = Clasificacion.Servicio,
        // Orden de compra explícita (el resto de grupos cae aquí por regla por defecto)
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
        // Fuera de toda RQ
        ["ANTICIPOS"] = null,
        ["VIDRIO DEL CLIENTE"] = null,
        ["CORTE DE HOJA"] = null,
        ["VID FLOTADO AL CORTE"] = null,
        ["VID AL CORTE C/PROC"] = null,
    };

    private static readonly string[] Mapeadas = CategoriasDemo.Keys.ToArray();

    /// <summary>Colapsa espacios repetidos y recorta, igual que <see cref="Filtro"/> en SQL.</summary>
    public static string? Normalizar(string? categoria)
    {
        if (categoria is null) return null;
        var t = categoria.Trim();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t;
    }

    public static bool Aplica(Clasificacion clasificacion, string? categoria, Naturaleza naturaleza)
    {
        var cat = Normalizar(categoria);
        if (cat is not null && CategoriasDemo.TryGetValue(cat, out var mapeada))
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

        // ponytail: dos Replace cubren hasta 4 espacios seguidos (SAP trae 2); EF lo traduce a replace() en SQL.
        return clasificacion switch
        {
            Clasificacion.Servicio => a =>
                (a.Categoria != null && propias.Contains(a.Categoria.Trim().Replace("  ", " ").Replace("  ", " ")))
                || ((a.Categoria == null || !mapeadas.Contains(a.Categoria.Trim().Replace("  ", " ").Replace("  ", " "))) && a.Naturaleza == Naturaleza.Servicio),
            Clasificacion.OrdenCompra => a =>
                (a.Categoria != null && propias.Contains(a.Categoria.Trim().Replace("  ", " ").Replace("  ", " ")))
                || ((a.Categoria == null || !mapeadas.Contains(a.Categoria.Trim().Replace("  ", " ").Replace("  ", " "))) && a.Naturaleza != Naturaleza.Servicio),
            _ => a => a.Categoria != null && propias.Contains(a.Categoria.Trim().Replace("  ", " ").Replace("  ", " ")),
        };
    }
}
