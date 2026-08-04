using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain;

/// <summary>
/// Entidad hija del agregado <see cref="Requisicion"/>. Representa una
/// línea de la requisición con artículo, cantidad, precio estimado y
/// estado de cubrimiento. Diseño §4.2.
///
/// Implementa <see cref="IAuditable"/> (cuidado §6.2 [P0]). NO implementa
/// <see cref="IPerteneceAEmpresa"/> ni <see cref="IFiscalmenteRelevante"/>:
/// el acceso es siempre vía la <see cref="Requisicion"/> raíz, que sí los
/// implementa, y la línea hereda los filters por la collection navigation.
/// La FK física <c>requisicion_lineas → requisiciones</c> con
/// <c>ON DELETE CASCADE</c> garantiza que las líneas no quedan huérfanas.
///
/// Mutaciones de líneas NO se hacen directamente sobre esta entidad: se
/// invocan a través de los métodos del agregado raíz
/// (<see cref="Requisicion.AgregarLinea"/>,
/// <see cref="Requisicion.ActualizarLineaEstructural"/>, etc.) que
/// validan estado y autorización. Por eso las propiedades tienen
/// setter <c>internal</c> — accesibles desde el dominio del módulo
/// pero no desde Application/Api.
/// </summary>
public sealed class LineaRequisicion : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid RequisicionId { get; private set; }

    /// <summary>
    /// B.2: para que <c>AuditSaveChangesInterceptor</c> agrupe esta fila
    /// con la <c>Requisicion</c> raíz en <c>core.audit_log</c>.
    /// </summary>
    public Guid AggregateRootId => RequisicionId;

    public short Posicion { get; private set; }

    public Guid ArticuloId { get; private set; }

    public decimal Cantidad { get; private set; }

    public string UnidadMedida { get; private set; } = string.Empty;

    public Money PrecioEstimado { get; private set; }

    public Guid? CuentaContableId { get; private set; }

    public Guid? CentroCostoId { get; private set; }

    public string? Proyecto { get; private set; }

    public DateOnly? FechaRequerida { get; private set; }

    public string? Notas { get; private set; }

    public decimal CantidadDeAlmacen { get; private set; }

    public decimal CantidadDeCompra { get; private set; }

    public decimal CantidadRecibida { get; private set; }

    /// <summary>
    /// Cuánto de esta línea ya se <b>entregó físicamente al solicitante</b>
    /// vía Salidas de Almacén (ADR-0043). Es el egreso al departamento, una
    /// dimensión distinta del cubrimiento (que modela el <i>origen</i> de la
    /// demanda: almacén/compra/recibido). Acumulador alimentado por
    /// <see cref="Requisicion.RegistrarEntrega"/> (PR #2 del canal); en el
    /// PR #1 solo se declara la columna (queda en 0 hasta que el consumidor
    /// del evento de salida la incremente).
    /// </summary>
    public decimal CantidadEntregada { get; private set; }

    /// <summary>
    /// VO computed a partir de las cantidades persistidas. No se almacena
    /// duplicado en BD (la <c>CantidadOriginal</c> es <see cref="Cantidad"/>).
    /// </summary>
    public Cubrimiento Cubrimiento => new(Cantidad, CantidadDeAlmacen, CantidadDeCompra, CantidadRecibida);

    /// <summary>
    /// <b>Pendiente de entregar al solicitante</b> (ADR-0043 #3) — fuente
    /// ÚNICA de la fórmula: lo físicamente disponible para entregar
    /// (<c>CantidadDeAlmacen + CantidadRecibida</c>) menos lo ya entregado
    /// (<see cref="CantidadEntregada"/>), con piso en 0. Incluye el material
    /// recibido por compra (antes el cálculo solo miraba <c>CantidadDeAlmacen</c>
    /// y dejaba inentregable lo comprado). Computed: no se persiste; el DTO de
    /// lectura y el FE leen de aquí (no recalculan).
    /// </summary>
    public decimal CantidadPendienteEntregar =>
        Math.Max(0m, (CantidadDeAlmacen + CantidadRecibida) - CantidadEntregada);

    /// <summary>Constructor para EF Core.</summary>
    private LineaRequisicion() { }

    /// <summary>
    /// Construye una línea nueva con cubrimiento inicial en cero. Solo
    /// invocable desde el agregado raíz (<see cref="Requisicion.AgregarLinea"/>).
    /// </summary>
    internal LineaRequisicion(
        Guid id,
        Guid requisicionId,
        short posicion,
        Guid articuloId,
        decimal cantidad,
        string unidadMedida,
        Money precioEstimado,
        Guid? cuentaContableId = null,
        Guid? centroCostoId = null,
        string? proyecto = null,
        DateOnly? fechaRequerida = null,
        string? notas = null) : base(id)
    {
        if (requisicionId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "LINEA_REQUISICION_ID_VACIO",
                "La línea debe pertenecer a una requisición.");
        }

        if (articuloId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "LINEA_ARTICULO_REQUERIDO",
                "El artículo es requerido.");
        }

        if (cantidad <= 0)
        {
            throw new BusinessRuleException(
                "LINEA_CANTIDAD_INVALIDA",
                "La cantidad debe ser mayor a cero.");
        }

        if (string.IsNullOrWhiteSpace(unidadMedida))
        {
            throw new BusinessRuleException(
                "LINEA_UNIDAD_MEDIDA_REQUERIDA",
                "La unidad de medida es requerida.");
        }

        if (unidadMedida.Length > 20)
        {
            throw new BusinessRuleException(
                "LINEA_UNIDAD_MEDIDA_DEMASIADO_LARGA",
                "La unidad de medida no puede exceder 20 caracteres.");
        }

        if (precioEstimado.Amount < 0)
        {
            throw new BusinessRuleException(
                "LINEA_PRECIO_NEGATIVO",
                "El precio estimado no puede ser negativo.");
        }

        if (proyecto is { Length: > 200 })
        {
            throw new BusinessRuleException(
                "LINEA_PROYECTO_DEMASIADO_LARGO",
                "El proyecto no puede exceder 200 caracteres.");
        }

        if (notas is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "LINEA_NOTAS_DEMASIADO_LARGAS",
                "Las notas no pueden exceder 500 caracteres.");
        }

        RequisicionId = requisicionId;
        Posicion = posicion;
        ArticuloId = articuloId;
        Cantidad = cantidad;
        UnidadMedida = unidadMedida;
        PrecioEstimado = precioEstimado;
        CuentaContableId = cuentaContableId;
        CentroCostoId = centroCostoId;
        Proyecto = proyecto;
        FechaRequerida = fechaRequerida;
        Notas = notas;
        CantidadDeAlmacen = 0m;
        CantidadDeCompra = 0m;
        CantidadRecibida = 0m;
        CantidadEntregada = 0m;
    }

    /// <summary>
    /// Actualiza los campos estructurales (artículo, cantidad, precio,
    /// etc.). El agregado raíz valida que (a) la requisición esté en
    /// <see cref="EstadoRequisicion.Borrador"/> y (b) la línea aún no
    /// tenga cubrimiento. Este método solo aplica los cambios.
    /// </summary>
    internal void ActualizarEstructural(
        Guid articuloId,
        decimal cantidad,
        string unidadMedida,
        Money precioEstimado,
        Guid? cuentaContableId,
        Guid? centroCostoId,
        string? proyecto,
        DateOnly? fechaRequerida)
    {
        if (Cubrimiento.TieneCubrimiento)
        {
            throw new BusinessRuleException(
                "LINEA_CON_CUBRIMIENTO_NO_ESTRUCTURAL",
                "No se pueden modificar campos estructurales de una línea con cubrimiento.");
        }

        if (articuloId == Guid.Empty)
        {
            throw new BusinessRuleException("LINEA_ARTICULO_REQUERIDO", "El artículo es requerido.");
        }
        if (cantidad <= 0)
        {
            throw new BusinessRuleException("LINEA_CANTIDAD_INVALIDA", "La cantidad debe ser mayor a cero.");
        }
        if (string.IsNullOrWhiteSpace(unidadMedida) || unidadMedida.Length > 20)
        {
            throw new BusinessRuleException("LINEA_UNIDAD_MEDIDA_INVALIDA", "Unidad de medida inválida.");
        }
        if (precioEstimado.Amount < 0)
        {
            throw new BusinessRuleException("LINEA_PRECIO_NEGATIVO", "El precio estimado no puede ser negativo.");
        }
        if (proyecto is { Length: > 200 })
        {
            throw new BusinessRuleException("LINEA_PROYECTO_DEMASIADO_LARGO", "Proyecto demasiado largo.");
        }

        ArticuloId = articuloId;
        Cantidad = cantidad;
        UnidadMedida = unidadMedida;
        PrecioEstimado = precioEstimado;
        CuentaContableId = cuentaContableId;
        CentroCostoId = centroCostoId;
        Proyecto = proyecto;
        FechaRequerida = fechaRequerida;
    }

    /// <summary>
    /// Actualiza solo las notas. Permitido en cualquier estado no terminal
    /// (validado por el agregado raíz). No exige ausencia de cubrimiento.
    /// </summary>
    internal void ActualizarNotas(string? notas)
    {
        if (notas is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "LINEA_NOTAS_DEMASIADO_LARGAS",
                "Las notas no pueden exceder 500 caracteres.");
        }

        Notas = notas;
    }

    /// <summary>
    /// Aplica el cubrimiento calculado por la bifurcación stock-aware
    /// (F4-PR1). Solo invocable desde <see cref="Requisicion.RegistrarCubrimiento"/>.
    /// La validación de invariantes (no-negativos, suma &lt;= original) se
    /// delega al ctor de <see cref="Domain.Cubrimiento"/>.
    /// </summary>
    internal void RegistrarCubrimiento(decimal cantidadDeAlmacen, decimal cantidadDeCompra)
    {
        // Construye un Cubrimiento como guard: si los valores violan las
        // reglas (negativos, suma > original, etc.), el ctor lanza
        // BusinessRuleException antes de mutar el state local.
        _ = new Cubrimiento(
            cantidadOriginal: Cantidad,
            cantidadDeAlmacen: cantidadDeAlmacen,
            cantidadDeCompra: cantidadDeCompra,
            cantidadRecibida: CantidadRecibida);

        CantidadDeAlmacen = cantidadDeAlmacen;
        CantidadDeCompra = cantidadDeCompra;
    }

    /// <summary>
    /// Registra una recepción de material desde OC (F5-PR1). Acumula
    /// sobre <see cref="CantidadRecibida"/> y delega la validación de
    /// la invariante <c>CantidadRecibida ≤ CantidadDeCompra</c> al
    /// ctor de <see cref="Domain.Cubrimiento"/>.
    ///
    /// <para>
    /// El parámetro es la cantidad <b>incremental</b> recibida (no la
    /// cantidad total). Recepciones múltiples (parciales) suman.
    /// </para>
    /// </summary>
    internal void RegistrarRecepcion(decimal cantidadRecibida)
    {
        if (cantidadRecibida <= 0)
        {
            throw new BusinessRuleException(
                "RECEPCION_CANTIDAD_INVALIDA",
                "La cantidad recibida debe ser mayor a cero.");
        }

        var nuevaCantidadRecibida = CantidadRecibida + cantidadRecibida;

        // Construye un Cubrimiento como guard: si la suma excede
        // CantidadDeCompra (recepción mayor a lo solicitado), el ctor
        // lanza CUBRIMIENTO_RECIBIDA_EXCEDE_COMPRA antes de mutar.
        _ = new Cubrimiento(
            cantidadOriginal: Cantidad,
            cantidadDeAlmacen: CantidadDeAlmacen,
            cantidadDeCompra: CantidadDeCompra,
            cantidadRecibida: nuevaCantidadRecibida);

        CantidadRecibida = nuevaCantidadRecibida;
    }

    /// <summary>
    /// Registra una entrega incremental al solicitante (ADR-0043). Acumula
    /// sobre <see cref="CantidadEntregada"/>. A diferencia de
    /// <see cref="RegistrarRecepcion"/>, es <b>robusta</b>: NO lanza si el
    /// total entregado excede el techo físico disponible
    /// (<c>CantidadDeAlmacen + CantidadRecibida</c>) — solo lo señala en el
    /// valor de retorno para que el handler emita una advertencia. Esto evita
    /// dead-letters en el canal de entrega, que en el PR #1 está dormido y
    /// recibe datos de convivencia imperfectos (salidas históricas sobre RQs
    /// ya <see cref="EstadoRequisicion.Cerrada"/> por el cierre viejo).
    ///
    /// <para>El parámetro es la cantidad <b>incremental</b> entregada (no el
    /// total). Una entrega no-positiva sí es un error de contrato y lanza.</para>
    /// </summary>
    /// <returns>
    /// <c>Excedio</c>: si el nuevo total supera el techo disponible.
    /// <c>Total</c>: el acumulado post-entrega. <c>Techo</c>: el máximo
    /// físicamente entregable al momento.
    /// </returns>
    internal (bool Excedio, decimal Total, decimal Techo) RegistrarEntrega(decimal cantidadEntregada)
    {
        if (cantidadEntregada <= 0)
        {
            throw new BusinessRuleException(
                "ENTREGA_CANTIDAD_INVALIDA",
                "La cantidad entregada debe ser mayor a cero.");
        }

        var nuevoTotal = CantidadEntregada + cantidadEntregada;
        var techo = CantidadDeAlmacen + CantidadRecibida;

        // Robusto: se acumula incluso si excede el techo (no se lanza). El
        // CHECK de BD es solo cant_entregada >= 0, coherente con esto.
        CantidadEntregada = nuevoTotal;

        return (Excedio: nuevoTotal > techo, Total: nuevoTotal, Techo: techo);
    }
}
