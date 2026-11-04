namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Resuelve los maestros (sucursal, artículo por clave A+W y medida) de las salidas de la app de
/// Almacenes. Lo implementa un adapter fuera de Almacén (Almacén no puede referenciar Compartido).
/// </summary>
public interface IAppSalidaResolverPort
{
    Task<ResolverSalidaResultado> ResolverAsync(
        string sucursalCodigo, IReadOnlyList<GrupoSalidaApp> grupos, CancellationToken ct);
}

/// <summary>Un grupo de la salida: artículo A+W y, opcionalmente, la medida de la hoja (la girada es la misma).</summary>
public sealed record GrupoSalidaApp(string ArticuloAw, decimal? AnchoMm, decimal? AltoMm);

/// <summary>Maestros resueltos de un grupo; ancho/alto son los canónicos de la variante.</summary>
public sealed record GrupoSalidaResuelto(
    Guid ArticuloId, string UnidadMedida, Guid? ProductoAwVarianteId, decimal? AnchoMm, decimal? AltoMm);

/// <summary>Resultado: <see cref="Error"/> no nulo = falló (no se movió nada); si no, un resuelto por grupo, en el mismo orden.</summary>
public sealed record ResolverSalidaResultado(
    string? Error, Guid SucursalId, IReadOnlyList<GrupoSalidaResuelto> Grupos);
