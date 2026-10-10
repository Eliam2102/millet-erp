using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Facturacion.Application.Timbrado;

public sealed record LineaFiscalAValidar(int Linea,Guid? ProductoId,string Descripcion,string? ClaveProdServSat,string? ClaveUnidadSat,string? ObjetoImp,decimal? TasaIvaTraslado);
public sealed record ProductoFiscalFaltante(int Linea,Guid? ProductoId,string Producto,IReadOnlyList<string> Campos);
public sealed class ProductosFiscalesIncompletosException(IReadOnlyList<ProductoFiscalFaltante> productos)
    : BusinessRuleException("PRODUCTOS_FISCALES_INCOMPLETOS", "No se puede timbrar. "+string.Join("; ",productos.Select(p=>$"Línea {p.Linea}, producto {p.Producto}: falta {string.Join(", ",p.Campos)}"))+". Completa la ficha fiscal en Datos maestros → Productos A+W.")
{
    public IReadOnlyList<ProductoFiscalFaltante> Productos { get; } = productos;
}
public static class ValidadorProductoFiscal
{
    public static IReadOnlyList<string> Faltantes(string? producto,string? unidad,string? objeto,decimal? iva) => new[]{
        string.IsNullOrWhiteSpace(producto)?"Clave de producto SAT":null,
        string.IsNullOrWhiteSpace(unidad)?"Clave de unidad SAT":null,
        string.IsNullOrWhiteSpace(objeto)?"Objeto de impuesto":null,
        iva is null?"Tasa de IVA":null }.OfType<string>().ToArray();
    public static async Task ValidarAsync(IEnumerable<LineaFiscalAValidar> lineas,IProductosReadPort? productos,CancellationToken ct)
    {
        var faltantes=new List<ProductoFiscalFaltante>(); var cache=new Dictionary<Guid,ProductoFiscalLectura?>();
        foreach(var l in lineas)
        {
            var campos=Faltantes(l.ClaveProdServSat,l.ClaveUnidadSat,l.ObjetoImp,l.TasaIvaTraslado).ToList(); var nombre=l.Descripcion;
            if(l.ProductoId is {} id && productos is not null)
            {
                if(!cache.TryGetValue(id,out var p)) { p=await productos.ObtenerAsync(id,ct); cache[id]=p; }
                if(p is null) campos.Add("Producto en el maestro fiscal");
                else
                {
                    nombre=p.Descripcion; campos.AddRange(Faltantes(p.ClaveProdServSat,p.ClaveUnidadSat,p.ObjetoImp,p.TasaIvaTraslado));
                    if(p.EstadoFiscal=="FiscalPendiente" && campos.Count==0) campos.Add("Confirmación de la ficha fiscal");
                }
            }
            if(campos.Count>0) faltantes.Add(new(l.Linea,l.ProductoId,nombre,campos.Distinct().ToArray()));
        }
        if(faltantes.Count>0) throw new ProductosFiscalesIncompletosException(faltantes);
    }
}
