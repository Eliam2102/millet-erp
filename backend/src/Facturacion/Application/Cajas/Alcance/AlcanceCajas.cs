using System.Linq.Expressions;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;

namespace Millet.Facturacion.Application.Cajas.Alcance;

/// <summary>Resultado de resolver la Capa A para el usuario actual (12-cajas.md §4.1, §9).</summary>
public enum TipoAlcanceCaja : short
{
    /// <summary>Permiso <c>facturacion.caja.leer-todas</c>: sin filtro + bucket "Sin asignar".</summary>
    Total = 1,

    /// <summary>Unión de combinaciones (sucursal, canal) de cajas activas + concesiones.</summary>
    Combinaciones = 2,

    /// <summary>Sin cajas activas ni concesiones: no ve ningún documento.</summary>
    Ninguno = 3,
}

/// <summary>
/// Una combinación (sucursal?, canal?) del alcance. <c>null</c> = comodín
/// (todas las sucursales / todos los canales del catálogo). El comodín de
/// canal NO alcanza documentos con canal <c>NULL</c>: esos son "Sin asignar"
/// y solo los ve <c>leer-todas</c> (`[Decisión 12-D]`).
/// </summary>
public readonly record struct CombinacionAlcance(Guid? SucursalId, short? CanalVentaId);

/// <summary>
/// Alcance de datos resuelto para el usuario actual. La regla vive una sola
/// vez: cada query handler hace <c>q = alcance.AplicarA(q)</c> (12-cajas.md §9).
/// </summary>
public sealed class AlcanceCajas
{
    public TipoAlcanceCaja Tipo { get; }
    public IReadOnlyList<CombinacionAlcance> Combinaciones { get; }

    /// <summary>True con <c>facturacion.caja.leer-todas</c> — habilita también el bucket "Sin asignar".</summary>
    public bool EsTotal => Tipo == TipoAlcanceCaja.Total;

    private AlcanceCajas(TipoAlcanceCaja tipo, IReadOnlyList<CombinacionAlcance> combinaciones)
    {
        Tipo = tipo;
        Combinaciones = combinaciones;
    }

    public static AlcanceCajas Total() => new(TipoAlcanceCaja.Total, []);

    public static AlcanceCajas Ninguno() => new(TipoAlcanceCaja.Ninguno, []);

    /// <summary>Alcance por combinaciones; un set vacío degrada a <see cref="TipoAlcanceCaja.Ninguno"/>.</summary>
    public static AlcanceCajas De(IReadOnlyList<CombinacionAlcance> combinaciones) =>
        combinaciones.Count == 0 ? Ninguno() : new(TipoAlcanceCaja.Combinaciones, combinaciones);

    /// <summary>Filtra cualquier subtipo de <see cref="Comprobante"/> (facturas, REPP, anticipos, NC).</summary>
    public IQueryable<T> AplicarA<T>(IQueryable<T> query) where T : Comprobante => Tipo switch
    {
        TipoAlcanceCaja.Total => query,
        TipoAlcanceCaja.Ninguno => query.Where(_ => false),
        _ => query.Where(AlcancePredicados.Coincide<T>(Combinaciones)),
    };

    /// <summary>Filtra pedidos facturables (canal no-nullable en el pedido).</summary>
    public IQueryable<PedidoFacturable> AplicarA(IQueryable<PedidoFacturable> query) => Tipo switch
    {
        TipoAlcanceCaja.Total => query,
        TipoAlcanceCaja.Ninguno => query.Where(_ => false),
        _ => query.Where(AlcancePredicados.Coincide<PedidoFacturable>(Combinaciones)),
    };
}

/// <summary>
/// Bucket "Sin asignar" (`[Decisión 12-B]`): documentos cuya (sucursal, canal)
/// no mapea a ninguna caja <b>activa</b> — NOT-IN de las combinaciones de todas
/// las cajas activas de la empresa (las concesiones de <c>usuario_alcance</c>
/// no cuentan: son visibilidad de usuarios, no asignación a cajas). Incluye
/// los comprobantes con canal <c>NULL</c> (históricos pre-caja).
/// </summary>
public sealed class AlcanceSinAsignar
{
    private readonly IReadOnlyList<CombinacionAlcance> _combinacionesCajasActivas;

    public AlcanceSinAsignar(IReadOnlyList<CombinacionAlcance> combinacionesCajasActivas) =>
        _combinacionesCajasActivas = combinacionesCajasActivas;

    public IQueryable<T> AplicarA<T>(IQueryable<T> query) where T : Comprobante =>
        query.Where(AlcancePredicados.NoCoincide<T>(_combinacionesCajasActivas));

    public IQueryable<PedidoFacturable> AplicarA(IQueryable<PedidoFacturable> query) =>
        query.Where(AlcancePredicados.NoCoincide<PedidoFacturable>(_combinacionesCajasActivas));
}

/// <summary>
/// Predicate-builder del alcance: OR de pares (sucursal, canal) sobre las
/// propiedades <c>SucursalId</c>/<c>CanalVentaId</c>, que existen con esos
/// nombres tanto en <see cref="Comprobante"/> (canal <c>short?</c>) como en
/// <see cref="PedidoFacturable"/> (canal <c>short</c>). Se construye por
/// expresión para que EF lo traduzca a SQL.
/// </summary>
internal static class AlcancePredicados
{
    internal static Expression<Func<T, bool>> Coincide<T>(IReadOnlyList<CombinacionAlcance> combinaciones)
    {
        var (parametro, cuerpo) = ConstruirOr<T>(combinaciones);
        return Expression.Lambda<Func<T, bool>>(cuerpo, parametro);
    }

    internal static Expression<Func<T, bool>> NoCoincide<T>(IReadOnlyList<CombinacionAlcance> combinaciones)
    {
        var (parametro, cuerpo) = ConstruirOr<T>(combinaciones);
        return Expression.Lambda<Func<T, bool>>(Expression.Not(cuerpo), parametro);
    }

    private static (ParameterExpression Parametro, Expression Cuerpo) ConstruirOr<T>(
        IReadOnlyList<CombinacionAlcance> combinaciones)
    {
        var parametro = Expression.Parameter(typeof(T), "d");
        var sucursal = Expression.Property(parametro, "SucursalId");
        var canal = Expression.Property(parametro, "CanalVentaId");
        var canalNullable = Nullable.GetUnderlyingType(canal.Type) is not null;

        Expression? cuerpo = null;
        foreach (var combinacion in combinaciones)
        {
            Expression clausula = combinacion.SucursalId is Guid sucursalId
                ? Expression.Equal(sucursal, Expression.Constant(sucursalId, typeof(Guid)))
                : Expression.Constant(true);

            Expression clausulaCanal;
            if (combinacion.CanalVentaId is short canalId)
            {
                clausulaCanal = canalNullable
                    ? Expression.Equal(canal, Expression.Constant((short?)canalId, typeof(short?)))
                    : Expression.Equal(canal, Expression.Constant(canalId, typeof(short)));
            }
            else
            {
                // Comodín de canal = todos los canales del catálogo; un canal
                // NULL no es un canal → queda fuera ([Decisión 12-D]).
                clausulaCanal = canalNullable
                    ? Expression.NotEqual(canal, Expression.Constant(null, typeof(short?)))
                    : Expression.Constant(true);
            }

            var pareja = Expression.AndAlso(clausula, clausulaCanal);
            cuerpo = cuerpo is null ? pareja : Expression.OrElse(cuerpo, pareja);
        }

        return (parametro, cuerpo ?? Expression.Constant(false));
    }
}
