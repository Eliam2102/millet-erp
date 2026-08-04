using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Catalogo;

/// <summary>
/// Configuración de reorden por artículo a Nivel 1 (Sucursal) o Nivel 2
/// (Almacén) (ADR-0047, enmienda 2026-07-06). Tabla
/// <c>almacen.configuraciones_reorden</c>. Porta la política que <b>dispara</b>
/// el reabasto: mínimo, máximo, punto de reorden, bandera de auto-requisición y
/// objetivo de reposición.
///
/// <para>
/// Un artículo puede tener VARIAS configuraciones —una por cada sucursal/almacén
/// donde se reabastece (p. ej. el mismo artículo en Cancún a N1 y en dos
/// almacenes de Conkal a N2)—, todas válidas y distintas. Clave única
/// <c>(articulo_id, nivel, entidad_id)</c>. La exclusión N1⊕N2 (un artículo no
/// puede tener config N1 y N2 <b>activas</b> en la misma sucursal) NO sale del
/// unique: se valida en el handler resolviendo la sucursal de un almacén N2 vía
/// <c>Almacen.SucursalId</c>.
/// </para>
/// <para>
/// <see cref="ArticuloId"/> es referencia <b>lógica</b> a
/// <c>compartido.articulos</c>; <see cref="EntidadId"/> es referencia
/// <b>lógica</b> a <c>compartido.sucursales</c> (si <see cref="Nivel"/> =
/// <see cref="NivelReorden.Sucursal"/>) o a <c>almacen.almacenes</c> (si =
/// <see cref="NivelReorden.Almacen"/>) — sin FK física, como
/// <c>saldos.articulo_id</c>; la existencia se valida en el handler según el
/// nivel.
/// </para>
/// </summary>
public sealed class ConfiguracionReorden : BaseEntity, IAuditable
{
    public Guid ArticuloId { get; private set; }
    public NivelReorden Nivel { get; private set; }
    public Guid EntidadId { get; private set; }
    public decimal Minimo { get; private set; }
    public decimal Maximo { get; private set; }
    public decimal PuntoReorden { get; private set; }
    public bool AutoRequisicion { get; private set; }
    public ObjetivoReposicion Objetivo { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private ConfiguracionReorden() { }

    public ConfiguracionReorden(
        Guid id,
        Guid articuloId,
        NivelReorden nivel,
        Guid entidadId,
        decimal minimo,
        decimal maximo,
        decimal puntoReorden,
        bool autoRequisicion,
        ObjetivoReposicion objetivo,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (articuloId == Guid.Empty)
            throw new BusinessRuleException("REORDEN_SIN_ARTICULO",
                "La configuración de reorden requiere artículo.");
        if (entidadId == Guid.Empty)
            throw new BusinessRuleException("REORDEN_SIN_ENTIDAD",
                "La configuración de reorden requiere la entidad (sucursal o almacén).");
        ValidarNiveles(minimo, maximo, puntoReorden);

        ArticuloId = articuloId;
        Nivel = nivel;
        EntidadId = entidadId;
        Minimo = minimo;
        Maximo = maximo;
        PuntoReorden = puntoReorden;
        AutoRequisicion = autoRequisicion;
        Objetivo = objetivo;
        Estatus = estatus;
    }

    /// <summary>
    /// Cambia la política (min/máx/reorden/bandera/objetivo). Los campos llave
    /// (<see cref="ArticuloId"/>, <see cref="Nivel"/>, <see cref="EntidadId"/>)
    /// son inmutables — cambiar de nivel/entidad = desactivar + crear otra.
    /// </summary>
    public void EditarPolitica(
        decimal minimo, decimal maximo, decimal puntoReorden,
        bool autoRequisicion, ObjetivoReposicion objetivo)
    {
        ValidarNiveles(minimo, maximo, puntoReorden);
        Minimo = minimo;
        Maximo = maximo;
        PuntoReorden = puntoReorden;
        AutoRequisicion = autoRequisicion;
        Objetivo = objetivo;
    }

    public void CambiarEstatus(EstatusCatalogo estatus) => Estatus = estatus;

    /// <summary>
    /// Cantidad objetivo de reposición según <see cref="Objetivo"/> (ADR-0047 PR5.B):
    /// el motor repone hasta este nivel. Minimo→<see cref="Minimo"/>,
    /// Maximo→<see cref="Maximo"/>, Reorden→<see cref="PuntoReorden"/>.
    /// </summary>
    public decimal ResolverObjetivo() => Objetivo switch
    {
        ObjetivoReposicion.Minimo => Minimo,
        ObjetivoReposicion.Maximo => Maximo,
        ObjetivoReposicion.Reorden => PuntoReorden,
        _ => throw new BusinessRuleException("REORDEN_OBJETIVO_INVALIDO",
            $"Objetivo de reposición no soportado: {Objetivo}."),
    };

    private static void ValidarNiveles(decimal minimo, decimal maximo, decimal puntoReorden)
    {
        if (minimo < 0 || maximo < 0 || puntoReorden < 0)
            throw new BusinessRuleException("REORDEN_NIVELES_NEGATIVOS",
                "Los niveles (mínimo, máximo, punto de reorden) no pueden ser negativos.");
        if (maximo < minimo)
            throw new BusinessRuleException("REORDEN_MAX_MENOR_MIN",
                "El máximo no puede ser menor que el mínimo.");
    }
}
