using Millet.Compras.Domain.Oc.Impuestos;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Entidad hija del agregado <see cref="OrdenCompra"/> (diseño §4.2).
/// Representa una línea de la OC con artículo, cantidad, precio,
/// descuento, impuestos calculados y cantidades acumuladas de
/// recepción/facturación.
///
/// Implementa <see cref="IAuditable"/> (cuidado §6.2 [P0]). NO implementa
/// <see cref="IPerteneceAEmpresa"/> ni <see cref="IFiscalmenteRelevante"/>:
/// el acceso es siempre vía la <see cref="OrdenCompra"/> raíz, que sí los
/// implementa, y la línea hereda los filters por la collection navigation.
/// La FK física <c>orden_compra_lineas → ordenes_compra</c> con
/// <c>ON DELETE CASCADE</c> garantiza que las líneas no quedan huérfanas.
///
/// Mutaciones NO se hacen directamente sobre esta entidad: se invocan a
/// través de métodos del agregado raíz (<see cref="OrdenCompra.AgregarLineaManual"/>,
/// futuros <c>ActualizarLinea</c>, <c>EliminarLinea</c>, etc.) que validan
/// estado del agregado y autorización. Setters son <c>internal</c> —
/// accesibles desde el dominio del módulo pero no desde Application/Api.
///
/// El monto en <see cref="PrecioUnitario"/>, <see cref="IvaImporte"/> y
/// <see cref="RetencionIsr"/> es <see cref="decimal"/> simple — la moneda
/// vive en <see cref="OrdenCompra.Moneda"/> (cabecera), única para toda
/// la OC. Esto evita splittear cada monto en (amount, currency).
/// </summary>
public sealed class LineaOrdenCompra : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid OrdenCompraId { get; private set; }

    /// <summary>
    /// B.2: para que <c>AuditSaveChangesInterceptor</c> agrupe esta fila
    /// con la <see cref="OrdenCompra"/> raíz en <c>core.audit_log</c>.
    /// </summary>
    public Guid AggregateRootId => OrdenCompraId;

    public int Posicion { get; private set; }

    public Guid ArticuloId { get; private set; }

    /// <summary>
    /// GAP-9: <c>true</c> si el artículo de la línea es de naturaleza
    /// <c>Servicio</c> (snapshot al capturar la línea). Los servicios no
    /// se reciben en Almacén, así que estas líneas se excluyen del cálculo
    /// del sub-estado de Recepción de la OC (ver
    /// <see cref="OrdenCompra.RecalcularSubEstados"/>).
    /// </summary>
    public bool EsServicio { get; private set; }

    public string? DescripcionExtendida { get; private set; }

    public decimal Cantidad { get; private set; }

    public string UnidadMedida { get; private set; } = string.Empty;

    public decimal PrecioUnitario { get; private set; }

    public DescuentoLinea Descuento { get; private set; }

    public IndicadorImpuestos IndicadorImpuestos { get; private set; }

    /// <summary>IVA calculado por el motor (v0 = 16% del subtotal post-descuento).</summary>
    public decimal IvaImporte { get; private set; }

    /// <summary>Retención ISR si aplica. v0 siempre <c>null</c>.</summary>
    public decimal? RetencionIsr { get; private set; }

    public Guid DepartamentoSolicitanteId { get; private set; }

    /// <summary>FK a la requisición de origen. Null si <c>SinRequisicionPrevia</c>.</summary>
    public Guid? RequisicionId { get; private set; }

    public Guid? LineaRequisicionId { get; private set; }

    /// <summary>
    /// CC-Máquina (Dim3) de la línea (Fase E PR3). Heredado 1:1 de la línea de
    /// RQ de origen cuando <see cref="LineaRequisicionId"/> no es null; elegido
    /// por el comprador (proxy) cuando la línea es manual. Nullable (opcional en
    /// PR3; obligatorio en PR3.1). No filtra por alcance en lectura (ADR-0050).
    /// </summary>
    public Guid? CentroCostoId { get; private set; }

    public DateTimeOffset? FechaEntregaLinea { get; private set; }

    /// <summary>Acumulado de recepciones registradas. Default 0. Máx <see cref="Cantidad"/>.</summary>
    public decimal CantidadRecibida { get; private set; }

    /// <summary>Acumulado de facturas registradas. Default 0. Máx <see cref="Cantidad"/>.</summary>
    public decimal CantidadFacturada { get; private set; }

    public string? TextoAdicional { get; private set; }

    /// <summary>
    /// Subtotal post-descuento (computed). Fórmula §4.2:
    /// <c>(Cantidad * PrecioUnitario) - Descuento.Aplicar(...)</c>. Redondeado
    /// a 2 decimales banker's rounding.
    /// </summary>
    public decimal SubtotalLinea
    {
        get
        {
            var bruto = Math.Round(Cantidad * PrecioUnitario, 2, MidpointRounding.ToEven);
            var descontado = Descuento.Aplicar(bruto);
            return bruto - descontado;
        }
    }

    /// <summary>Constructor para EF Core.</summary>
    private LineaOrdenCompra() { }

    /// <summary>
    /// Construye una línea nueva con cantidades recibidas/facturadas en 0
    /// e IVA calculado por el motor v0. Solo invocable desde el agregado
    /// raíz (<see cref="OrdenCompra.AgregarLineaManual"/> y futuros métodos
    /// de Fase 4 para crear desde RQ).
    /// </summary>
    internal LineaOrdenCompra(
        Guid id,
        Guid ordenCompraId,
        int posicion,
        Guid articuloId,
        decimal cantidad,
        string unidadMedida,
        decimal precioUnitario,
        Guid departamentoSolicitanteId,
        DescuentoLinea? descuento = null,
        IndicadorImpuestos? indicadorImpuestos = null,
        Guid? requisicionId = null,
        Guid? lineaRequisicionId = null,
        Guid? centroCostoId = null,
        string? descripcionExtendida = null,
        DateTimeOffset? fechaEntregaLinea = null,
        string? textoAdicional = null,
        bool esServicio = false) : base(id)
    {
        if (ordenCompraId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "LINEA_OC_ORDEN_COMPRA_ID_VACIO",
                "La línea debe pertenecer a una orden de compra.");
        }
        if (articuloId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "LINEA_OC_ARTICULO_REQUERIDO",
                "El artículo es requerido.");
        }
        if (departamentoSolicitanteId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "LINEA_OC_DEPTO_REQUERIDO",
                "El departamento solicitante es requerido.");
        }
        if (posicion <= 0)
        {
            throw new BusinessRuleException(
                "LINEA_OC_POSICION_INVALIDA",
                $"La posición debe ser mayor a cero; recibida: {posicion}.");
        }
        if (cantidad <= 0m)
        {
            throw new BusinessRuleException(
                "LINEA_OC_CANTIDAD_INVALIDA",
                "La cantidad debe ser mayor a cero.");
        }
        if (precioUnitario < 0m)
        {
            throw new BusinessRuleException(
                "LINEA_OC_PRECIO_NEGATIVO",
                "El precio unitario no puede ser negativo.");
        }
        if (string.IsNullOrWhiteSpace(unidadMedida) || unidadMedida.Length > 20)
        {
            throw new BusinessRuleException(
                "LINEA_OC_UNIDAD_MEDIDA_INVALIDA",
                "Unidad de medida inválida (1–20 caracteres).");
        }
        if (descripcionExtendida is { Length: > 1000 })
        {
            throw new BusinessRuleException(
                "LINEA_OC_DESCRIPCION_DEMASIADO_LARGA",
                "La descripción extendida no puede exceder 1000 caracteres.");
        }
        if (textoAdicional is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "LINEA_OC_TEXTO_ADICIONAL_DEMASIADO_LARGO",
                "El texto adicional no puede exceder 500 caracteres.");
        }

        // CHECK ck_oc_lineas_rq_coherente: ambos null o ambos no-null.
        if ((requisicionId is null) != (lineaRequisicionId is null))
        {
            throw new BusinessRuleException(
                "LINEA_OC_RQ_INCOHERENTE",
                "Si la línea viene de una RQ, requisicionId y lineaRequisicionId deben venir juntos.");
        }

        OrdenCompraId = ordenCompraId;
        Posicion = posicion;
        ArticuloId = articuloId;
        EsServicio = esServicio;
        DescripcionExtendida = descripcionExtendida;
        Cantidad = cantidad;
        UnidadMedida = unidadMedida;
        PrecioUnitario = precioUnitario;
        Descuento = descuento ?? DescuentoLinea.Cero;
        IndicadorImpuestos = indicadorImpuestos ?? IndicadorImpuestos.Iva16Default;
        DepartamentoSolicitanteId = departamentoSolicitanteId;
        RequisicionId = requisicionId;
        LineaRequisicionId = lineaRequisicionId;
        CentroCostoId = centroCostoId;
        FechaEntregaLinea = fechaEntregaLinea;
        TextoAdicional = textoAdicional;
        CantidadRecibida = 0m;
        CantidadFacturada = 0m;

        // Calcular impuestos v0 al construir. F3-PR2 reemplaza el motor;
        // RecalcularImpuestos puede invocarse después si cambia el régimen
        // fiscal aplicable.
        RecalcularImpuestos();
    }

    /// <summary>
    /// Recalcula <see cref="IvaImporte"/> y <see cref="RetencionIsr"/>
    /// con el motor v0 (IVA 16% fijo). En F3-PR2 se reemplaza el motor por
    /// el real basado en regímenes fiscales (decisión C3). Se invoca en el
    /// ctor y por el agregado raíz cuando se reaplique el motor.
    /// </summary>
    internal void RecalcularImpuestos()
    {
        IvaImporte = CalculadorImpuestosV0.CalcularIvaLinea(SubtotalLinea);
        RetencionIsr = CalculadorImpuestosV0.CalcularRetencionIsrLinea(SubtotalLinea);
    }

    /// <summary>
    /// Actualiza los campos estructurales de la línea (artículo, cantidad,
    /// precio, descuento, etc.). El agregado raíz valida estado y
    /// post-recepción; este método aplica los cambios y recalcula
    /// impuestos. PATCH parcial: nullables = no tocar.
    /// </summary>
    internal void ActualizarEstructural(
        Guid? articuloId,
        decimal? cantidad,
        string? unidadMedida,
        decimal? precioUnitario,
        DescuentoLinea? descuento,
        IndicadorImpuestos? indicadorImpuestos,
        Guid? departamentoSolicitanteId,
        string? descripcionExtendida,
        DateTimeOffset? fechaEntregaLinea,
        bool limpiarDescripcionExtendida = false,
        bool limpiarFechaEntregaLinea = false,
        bool? esServicio = null,
        Guid? centroCostoId = null)
    {
        if (CantidadRecibida > 0m || CantidadFacturada > 0m)
        {
            throw new BusinessRuleException(
                "LINEA_OC_CON_RECEPCION_NO_ESTRUCTURAL",
                "No se pueden modificar campos estructurales de una línea con recepción o facturación.");
        }

        if (articuloId is Guid a)
        {
            if (a == Guid.Empty)
            {
                throw new BusinessRuleException(
                    "LINEA_OC_ARTICULO_REQUERIDO",
                    "El artículo es requerido.");
            }
            ArticuloId = a;
        }
        // GAP-9: si cambió el artículo, el caller (handler) re-resuelve la
        // naturaleza y manda el snapshot actualizado de EsServicio.
        if (esServicio is bool es) EsServicio = es;
        if (cantidad is decimal c)
        {
            if (c <= 0m)
            {
                throw new BusinessRuleException(
                    "LINEA_OC_CANTIDAD_INVALIDA",
                    "La cantidad debe ser mayor a cero.");
            }
            Cantidad = c;
        }
        if (unidadMedida is not null)
        {
            if (string.IsNullOrWhiteSpace(unidadMedida) || unidadMedida.Length > 20)
            {
                throw new BusinessRuleException(
                    "LINEA_OC_UNIDAD_MEDIDA_INVALIDA",
                    "Unidad de medida inválida (1–20 caracteres).");
            }
            UnidadMedida = unidadMedida;
        }
        if (precioUnitario is decimal p)
        {
            if (p < 0m)
            {
                throw new BusinessRuleException(
                    "LINEA_OC_PRECIO_NEGATIVO",
                    "El precio unitario no puede ser negativo.");
            }
            PrecioUnitario = p;
        }
        if (descuento is DescuentoLinea d) Descuento = d;
        if (indicadorImpuestos is IndicadorImpuestos ii) IndicadorImpuestos = ii;
        if (departamentoSolicitanteId is Guid de)
        {
            if (de == Guid.Empty)
            {
                throw new BusinessRuleException(
                    "LINEA_OC_DEPTO_REQUERIDO",
                    "El departamento solicitante es requerido.");
            }
            DepartamentoSolicitanteId = de;
        }

        if (descripcionExtendida is not null)
        {
            if (descripcionExtendida.Length > 1000)
            {
                throw new BusinessRuleException(
                    "LINEA_OC_DESCRIPCION_DEMASIADO_LARGA",
                    "La descripción extendida no puede exceder 1000 caracteres.");
            }
            DescripcionExtendida = descripcionExtendida;
        }
        else if (limpiarDescripcionExtendida) DescripcionExtendida = null;

        if (fechaEntregaLinea is not null) FechaEntregaLinea = fechaEntregaLinea;
        else if (limpiarFechaEntregaLinea) FechaEntregaLinea = null;

        if (centroCostoId is Guid cc)
        {
            // Fase E PR3: el CC-Máquina de una línea HEREDADA (con FK a RQ) es
            // inmutable — viene 1:1 de la RQ (ADR-0050 "heredado, solo lectura").
            // Solo la línea manual lo edita.
            if (LineaRequisicionId is not null)
            {
                throw new BusinessRuleException(
                    "LINEA_OC_CC_HEREDADO_INMUTABLE",
                    "El CC-Máquina de una línea heredada de una requisición no es editable.");
            }
            CentroCostoId = cc;
        }

        // Fase E PR3.1: invariante de POST-ESTADO — una línea MANUAL no puede
        // quedar sin CC-Máquina después de una edición. Se evalúa sobre el
        // estado resultante (no sobre el input) para no romper la semántica
        // PATCH parcial: quien solo cambia el precio de una línea que ya trae
        // CC manda centroCostoId=null ("no tocar") y sigue pasando. El único
        // caso que bloquea es editar una línea manual LEGADA (anterior a
        // PR3.1) que aún lo tiene en null → se completa por demanda, sin
        // sembrado masivo. La heredada queda fuera: su CC viene 1:1 de la RQ.
        if (LineaRequisicionId is null && CentroCostoId is null)
        {
            throw new BusinessRuleException(
                "LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO",
                "El CC-Máquina es requerido en una línea manual de OC.");
        }

        // Recalcular impuestos: si cambió cantidad, precio o descuento, el
        // subtotal cambió → IVA cambia.
        RecalcularImpuestos();
    }

    /// <summary>
    /// Actualiza solo <see cref="TextoAdicional"/>. Permitido en cualquier
    /// estado (no terminal — el agregado valida) — útil para anotaciones
    /// operativas. NO recalcula impuestos.
    /// </summary>
    internal void ActualizarTextoAdicional(string? textoAdicional)
    {
        if (textoAdicional is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "LINEA_OC_TEXTO_ADICIONAL_DEMASIADO_LARGO",
                "El texto adicional no puede exceder 500 caracteres.");
        }
        TextoAdicional = textoAdicional;
    }

    /// <summary>
    /// Asigna (no incrementa) el acumulado de recepción para esta línea
    /// (F5-PR1). La idempotencia es del caller: el payload de los
    /// listeners trae el set acumulado, no el delta. Valida rango
    /// [0, <see cref="Cantidad"/>].
    /// </summary>
    internal void SetCantidadRecibida(decimal cantidadAcumulada)
    {
        if (cantidadAcumulada < 0m)
        {
            throw new BusinessRuleException(
                "LINEA_OC_CANTIDAD_RECIBIDA_NEGATIVA",
                "La cantidad recibida acumulada no puede ser negativa.");
        }
        if (cantidadAcumulada > Cantidad)
        {
            throw new BusinessRuleException(
                "LINEA_OC_CANTIDAD_RECIBIDA_EXCEDE",
                $"La cantidad recibida acumulada ({cantidadAcumulada}) excede la cantidad de la línea ({Cantidad}).");
        }
        CantidadRecibida = cantidadAcumulada;
    }

    /// <summary>
    /// Asigna (no incrementa) el acumulado de facturación para esta línea
    /// (F5-PR1). Mismo contrato que <see cref="SetCantidadRecibida"/>.
    /// </summary>
    internal void SetCantidadFacturada(decimal cantidadAcumulada)
    {
        if (cantidadAcumulada < 0m)
        {
            throw new BusinessRuleException(
                "LINEA_OC_CANTIDAD_FACTURADA_NEGATIVA",
                "La cantidad facturada acumulada no puede ser negativa.");
        }
        if (cantidadAcumulada > Cantidad)
        {
            throw new BusinessRuleException(
                "LINEA_OC_CANTIDAD_FACTURADA_EXCEDE",
                $"La cantidad facturada acumulada ({cantidadAcumulada}) excede la cantidad de la línea ({Cantidad}).");
        }
        CantidadFacturada = cantidadAcumulada;
    }
}
