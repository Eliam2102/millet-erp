using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.DatosMaestros.Domain;

/// <summary>
/// Producto de venta manufacturado, catálogo <c>compartido.producto_aw</c>
/// (ADR-0048 D5). Master de lo que la empresa <b>vende</b> (vidrio
/// transformado que A+W libera en pedidos), deliberadamente SEPARADO de
/// <see cref="Articulo"/> (lo que la empresa compra — insumos/refacciones de
/// la triada Compras↔Almacén↔CxP): poblaciones disjuntas, sin inventario en
/// el ERP, y meterlos en <c>compartido.articulos</c> exigiría filtros de
/// naturaleza en toda la triada.
///
/// <para>
/// Nace por <b>auto-provisión</b> al ingestar pedidos A+W
/// (<see cref="ReferenciaExterna"/> = <c>producto_id</c>/<c>PROD_ID</c>) y se
/// completa en <c>/admin/datos-maestros/productos-aw</c>. Los atributos SAT
/// (clave prod/serv, clave unidad, objeto imp, tasas) no existen en A+W —
/// son nullable y, como en Cliente, su ausencia no bloquea el alta pero sí
/// el timbrado (§16.12 levantamiento Facturación). Primer consumidor:
/// <c>IProductosReadPort</c> de Facturación.
/// </para>
/// </summary>
public sealed class ProductoAw : BaseEntity, IAuditable
{
    /// <summary>Llave natural en A+W (<c>PROD_ID</c>). UNIQUE — correlación de la auto-provisión.</summary>
    public string ReferenciaExterna { get; private set; } = string.Empty;

    public string Descripcion { get; private set; } = string.Empty;

    /// <summary>
    /// Código de unidad operativa normalizado desde A+W (M2/PZA/ML/KG).
    /// Snapshot string; el FK opcional al catálogo es
    /// <see cref="UnidadMedidaId"/> (mismo patrón transición que
    /// <see cref="Articulo"/>, ADR-0046).
    /// </summary>
    public string UnidadMedida { get; private set; } = string.Empty;

    public Guid? UnidadMedidaId { get; private set; }
    public Guid? CategoriaId { get; private set; }

    /// <summary>Clave SAT c_ClaveProdServ (8 dígitos), p.ej. 43211701.</summary>
    public string? ClaveProdServSat { get; private set; }

    /// <summary>Clave SAT c_ClaveUnidad, p.ej. MTK (m²), H87 (pieza), KGM.</summary>
    public string? ClaveUnidadSat { get; private set; }

    /// <summary>Objeto de impuesto SAT (c_ObjetoImp, claves 01–08).</summary>
    public string? ObjetoImp { get; private set; }

    /// <summary>Tasa de IVA trasladado (fracción, p.ej. 0.16). 0 = tasa 0%.</summary>
    public decimal? TasaIvaTraslado { get; private set; }

    public decimal? TasaRetencionIva { get; private set; }
    public decimal? TasaRetencionIsr { get; private set; }

    // ── Datos de aduana (Comercio Exterior / CCE) ───────────────────────
    // Intrínsecos del artículo: se fijan una vez y prellenan la línea de la
    // factura de exportación (cero captura por posición). Fuente primaria =
    // A+W (vw_erp_articulo, contrato a extender); si A+W no los trae, el
    // operador los captura aquí en Datos Maestros. Nullable: su ausencia no
    // bloquea el alta, sí el timbrado de un CCE.

    /// <summary>Fracción arancelaria SAT (c_FraccionArancelaria, 8-10 dígitos).</summary>
    public string? FraccionArancelaria { get; private set; }

    /// <summary>
    /// Unidad de medida aduanera (c_UnidadAduana, p.ej. 06). Corresponde a la
    /// UMT de la fracción; FiscalAPI no expone la UMT, por eso se fija en el
    /// artículo (auto-sugerible a futuro con un catálogo fracción→UMT propio).
    /// </summary>
    public string? UnidadAduana { get; private set; }

    /// <summary>Peso unitario en kg — base para <c>CantidadAduana</c> cuando la UMT es de peso.</summary>
    public decimal? PesoUnitarioKg { get; private set; }

    public OrigenMaster Origen { get; private set; } = OrigenMaster.Aw;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    /// <summary>Fecha de la baja controlada (nunca DELETE); null mientras el producto no esté dado de baja.</summary>
    public DateTime? FechaBaja { get; private set; }

    // Clasificación de A+W; dueño A+W, no editable a mano. La familia de negocio es Tipo (BA_PRODUKTART) → Grupo
    // (BA_PRODUKTGRP); Wgr (BA_WGR, jerárquico en KA_WGR) es el grupo de mercancía. CodigoModelo (BA_MCODE) es solo
    // la marca/modelo y se repite entre productos (doc A+W "Familias y clasificación").
    public string? CodigoModelo { get; private set; }
    public string? Grupo { get; private set; }
    public string? Tipo { get; private set; }
    public string? Wgr { get; private set; }
    public string? WgrDescripcion { get; private set; }

    private readonly List<ProductoAwVariante> _variantes = [];
    public IReadOnlyList<ProductoAwVariante> Variantes => _variantes;

    private readonly List<ProductoAwComponente> _componentes = [];
    public IReadOnlyList<ProductoAwComponente> Componentes => _componentes;

    private ProductoAw() { }

    public ProductoAw(
        Guid id,
        string referenciaExterna,
        string descripcion,
        string unidadMedida,
        OrigenMaster origen = OrigenMaster.Aw,
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        Guid? unidadMedidaId = null,
        Guid? categoriaId = null,
        string? claveProdServSat = null,
        string? claveUnidadSat = null,
        string? objetoImp = null,
        decimal? tasaIvaTraslado = null,
        decimal? tasaRetencionIva = null,
        decimal? tasaRetencionIsr = null,
        string? fraccionArancelaria = null,
        string? unidadAduana = null,
        decimal? pesoUnitarioKg = null) : base(id)
    {
        if (string.IsNullOrWhiteSpace(referenciaExterna) || referenciaExterna.Length > 50)
            throw new BusinessRuleException("PRODUCTO_AW_REFERENCIA_INVALIDA",
                "La referencia externa (PROD_ID de A+W) es requerida y no puede exceder 50 caracteres.");
        if (string.IsNullOrWhiteSpace(descripcion) || descripcion.Length > 254)
            throw new BusinessRuleException("PRODUCTO_AW_DESCRIPCION_INVALIDA",
                "La descripción es requerida y no puede exceder 254 caracteres.");
        if (string.IsNullOrWhiteSpace(unidadMedida) || unidadMedida.Length > 20)
            throw new BusinessRuleException("PRODUCTO_AW_UNIDAD_INVALIDA",
                "La unidad de medida es requerida y no puede exceder 20 caracteres.");
        ValidarFiscales(claveProdServSat, claveUnidadSat, objetoImp,
            tasaIvaTraslado, tasaRetencionIva, tasaRetencionIsr);
        ValidarAduana(fraccionArancelaria, unidadAduana, pesoUnitarioKg);

        ReferenciaExterna = referenciaExterna;
        Descripcion = descripcion;
        UnidadMedida = unidadMedida;
        UnidadMedidaId = unidadMedidaId;
        CategoriaId = categoriaId;
        ClaveProdServSat = claveProdServSat;
        ClaveUnidadSat = claveUnidadSat;
        ObjetoImp = objetoImp;
        TasaIvaTraslado = tasaIvaTraslado;
        TasaRetencionIva = tasaRetencionIva;
        TasaRetencionIsr = tasaRetencionIsr;
        FraccionArancelaria = fraccionArancelaria;
        UnidadAduana = unidadAduana;
        PesoUnitarioKg = pesoUnitarioKg;
        Origen = origen;
        Estatus = estatus;
    }

    /// <summary>
    /// Completa/corrige los atributos fiscales SAT (operador, post
    /// auto-provisión). <c>null</c> = no tocar; <c>limpiarX</c> = borrar.
    /// </summary>
    public void AsignarDatosFiscales(
        string? claveProdServSat = null,
        string? claveUnidadSat = null,
        string? objetoImp = null,
        decimal? tasaIvaTraslado = null,
        decimal? tasaRetencionIva = null,
        decimal? tasaRetencionIsr = null,
        bool limpiarTasaIvaTraslado = false,
        bool limpiarTasaRetencionIva = false,
        bool limpiarTasaRetencionIsr = false)
    {
        ValidarFiscales(claveProdServSat, claveUnidadSat, objetoImp,
            tasaIvaTraslado, tasaRetencionIva, tasaRetencionIsr);

        if (claveProdServSat is not null) ClaveProdServSat = claveProdServSat;
        if (claveUnidadSat is not null) ClaveUnidadSat = claveUnidadSat;
        if (objetoImp is not null) ObjetoImp = objetoImp;

        if (tasaIvaTraslado.HasValue) TasaIvaTraslado = tasaIvaTraslado;
        else if (limpiarTasaIvaTraslado) TasaIvaTraslado = null;

        if (tasaRetencionIva.HasValue) TasaRetencionIva = tasaRetencionIva;
        else if (limpiarTasaRetencionIva) TasaRetencionIva = null;

        if (tasaRetencionIsr.HasValue) TasaRetencionIsr = tasaRetencionIsr;
        else if (limpiarTasaRetencionIsr) TasaRetencionIsr = null;
    }

    /// <summary>
    /// Completa/corrige los datos de aduana del artículo (fracción, unidad
    /// aduanera, peso). <c>null</c> = no tocar; <c>limpiarX</c> = borrar.
    /// </summary>
    public void AsignarDatosAduana(
        string? fraccionArancelaria = null,
        string? unidadAduana = null,
        decimal? pesoUnitarioKg = null,
        bool limpiarFraccionArancelaria = false,
        bool limpiarUnidadAduana = false,
        bool limpiarPesoUnitarioKg = false)
    {
        ValidarAduana(fraccionArancelaria, unidadAduana, pesoUnitarioKg);

        if (fraccionArancelaria is not null) FraccionArancelaria = fraccionArancelaria;
        else if (limpiarFraccionArancelaria) FraccionArancelaria = null;

        if (unidadAduana is not null) UnidadAduana = unidadAduana;
        else if (limpiarUnidadAduana) UnidadAduana = null;

        if (pesoUnitarioKg.HasValue) PesoUnitarioKg = pesoUnitarioKg;
        else if (limpiarPesoUnitarioKg) PesoUnitarioKg = null;
    }

    /// <summary>
    /// PATCH parcial de los datos operativos (no fiscales). La
    /// <see cref="ReferenciaExterna"/> y el <see cref="Origen"/> son
    /// inmutables (correlación con A+W).
    /// </summary>
    public void ActualizarDatos(
        string? descripcion = null,
        string? unidadMedida = null,
        Guid? categoriaId = null,
        bool limpiarCategoria = false)
    {
        if (descripcion is not null)
        {
            if (descripcion.Length is 0 or > 254)
                throw new BusinessRuleException("PRODUCTO_AW_DESCRIPCION_INVALIDA",
                    "La descripción es requerida y no puede exceder 254 caracteres.");
            Descripcion = descripcion;
        }
        if (unidadMedida is not null)
        {
            if (unidadMedida.Length is 0 or > 20)
                throw new BusinessRuleException("PRODUCTO_AW_UNIDAD_INVALIDA",
                    "La unidad de medida es requerida y no puede exceder 20 caracteres.");
            UnidadMedida = unidadMedida;
        }
        if (categoriaId is Guid c) CategoriaId = c;
        else if (limpiarCategoria) CategoriaId = null;
    }

    /// <summary>Asigna la unidad del catálogo (ADR-0046): FK + sincroniza el snapshot.</summary>
    public void AsignarUnidadMedida(Guid unidadMedidaId, string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 20)
            throw new BusinessRuleException("PRODUCTO_AW_UNIDAD_INVALIDA",
                "El código de la unidad es requerido y no puede exceder 20 caracteres.");
        UnidadMedidaId = unidadMedidaId;
        UnidadMedida = codigo;
    }

    /// <summary>¿Puede facturarse? (clave prod/serv + clave unidad presentes.)</summary>
    public bool DatosFiscalesCompletos =>
        !string.IsNullOrWhiteSpace(ClaveProdServSat)
        && !string.IsNullOrWhiteSpace(ClaveUnidadSat);

    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;

    /// <summary>Baja controlada: Inactivo + fecha. Idempotente (conserva la fecha original); no borra datos ni variantes.</summary>
    public void DarDeBaja(DateTime fechaBajaUtc)
    {
        if (Estatus == EstatusCatalogo.Inactivo && FechaBaja.HasValue) return;
        Estatus = EstatusCatalogo.Inactivo;
        FechaBaja ??= fechaBajaUtc;
    }

    /// <summary>Reactiva un producto dado de baja: Activo y limpia la fecha de baja. Idempotente.</summary>
    public void Reactivar()
    {
        Estatus = EstatusCatalogo.Activo;
        FechaBaja = null;
    }

    /// <summary>
    /// Upsert de variantes por <c>ClaveVariante</c>. Rechaza claves repetidas en
    /// la lista. Las variantes no incluidas se conservan (la ausencia no es baja).
    /// Valida todo antes de mutar.
    /// </summary>
    public void AplicarVariantes(IEnumerable<ProductoAwVarianteDato> datos)
    {
        var lista = datos.ToList();
        var repetida = lista.GroupBy(d => d.ClaveVariante).FirstOrDefault(g => g.Count() > 1);
        if (repetida is not null)
            throw new BusinessRuleException("PRODUCTO_AW_VARIANTE_DUPLICADA",
                $"La clave de variante '{repetida.Key}' viene repetida para el producto {ReferenciaExterna}.");

        // Fase 1: construir/validar sin tocar el estado.
        var nuevas = new List<ProductoAwVariante>();
        var cambios = new List<(ProductoAwVariante V, ProductoAwVarianteDato D)>();
        foreach (var d in lista)
        {
            var existente = _variantes.FirstOrDefault(v => v.ClaveVariante == d.ClaveVariante);
            if (existente is null)
                nuevas.Add(new ProductoAwVariante(Guid.CreateVersion7(), Id, d.ClaveVariante,
                    d.AltoMm, d.AnchoMm, d.EspesorMm, d.Composicion));
            else
                cambios.Add((existente, d));
        }
        foreach (var (v, d) in cambios)
        {
            if (d.AltoMm is <= 0 || d.AnchoMm is <= 0 || d.EspesorMm is <= 0)
                throw new BusinessRuleException("PRODUCTO_AW_VARIANTE_MEDIDA_INVALIDA",
                    "Las medidas deben ser mayores a 0 (o nulas si no se informan).");
            if (d.Composicion is { Length: > 200 })
                throw new BusinessRuleException("PRODUCTO_AW_VARIANTE_COMPOSICION_INVALIDA",
                    "La composición no puede exceder 200 caracteres.");
        }
        // Fase 2: aplicar.
        foreach (var (v, d) in cambios) v.Asignar(d.AltoMm, d.AnchoMm, d.EspesorMm, d.Composicion);
        _variantes.AddRange(nuevas);
    }

    /// <summary>Reemplaza la clasificación de A+W (null = A+W no informa el dato).</summary>
    public void AplicarClasificacion(string? codigoModelo, string? grupo, string? tipo, string? wgr = null, string? wgrDescripcion = null)
    {
        if (codigoModelo is { Length: > 50 } || grupo is { Length: > 100 } || tipo is { Length: > 50 }
            || wgr is { Length: > 10 } || wgrDescripcion is { Length: > 100 })
            throw new BusinessRuleException("PRODUCTO_AW_CLASIFICACION_INVALIDA",
                "Código de modelo y tipo no pueden exceder 50 caracteres, el grupo y la descripción de mercancía 100, ni la mercancía 10.");
        CodigoModelo = codigoModelo;
        Grupo = grupo;
        Tipo = tipo;
        Wgr = wgr;
        WgrDescripcion = wgrDescripcion;
    }

    /// <summary>
    /// Reemplazo completo del árbol de composición (es posicional). Filas por <c>Orden</c>: las existentes se
    /// actualizan, las nuevas se agregan y las que ya no vienen se quitan. Valida todo antes de mutar.
    /// </summary>
    public void ReemplazarComponentes(IEnumerable<ProductoAwComponenteDato> datos)
    {
        var lista = datos.ToList();
        var repetido = lista.GroupBy(d => d.Orden).FirstOrDefault(g => g.Count() > 1);
        if (repetido is not null)
            throw new BusinessRuleException("PRODUCTO_AW_COMPONENTE_ORDEN_DUPLICADO",
                $"El orden {repetido.Key} viene repetido para el producto {ReferenciaExterna}.");
        foreach (var d in lista) ProductoAwComponente.Validar(d);

        _componentes.RemoveAll(c => lista.TrueForAll(d => d.Orden != c.Orden));
        foreach (var d in lista)
        {
            var existente = _componentes.Find(c => c.Orden == d.Orden);
            if (existente is null) _componentes.Add(new ProductoAwComponente(Guid.CreateVersion7(), Id, d));
            else existente.Asignar(d);
        }
    }

    private static void ValidarFiscales(
        string? claveProdServSat, string? claveUnidadSat, string? objetoImp,
        decimal? tasaIvaTraslado, decimal? tasaRetencionIva, decimal? tasaRetencionIsr)
    {
        if (claveProdServSat is { Length: not 8 })
            throw new BusinessRuleException("PRODUCTO_AW_CLAVE_PRODSERV_INVALIDA",
                "La clave producto/servicio SAT debe tener 8 dígitos.");
        if (claveUnidadSat is { Length: 0 or > 5 })
            throw new BusinessRuleException("PRODUCTO_AW_CLAVE_UNIDAD_INVALIDA",
                "La clave unidad SAT no puede estar vacía ni exceder 5 caracteres.");
        // c_ObjetoImp vigente llega hasta 08; el catálogo se resuelve en
        // vivo contra FiscalAPI (FAC-DET-PR1), aquí solo se valida la forma.
        if (objetoImp is not null && !System.Text.RegularExpressions.Regex.IsMatch(objetoImp, "^0[1-8]$"))
            throw new BusinessRuleException("PRODUCTO_AW_OBJETO_IMP_INVALIDO",
                "El objeto de impuesto debe ser una clave 01–08 del catálogo SAT c_ObjetoImp.");
        if (tasaIvaTraslado is < 0 or > 1)
            throw new BusinessRuleException("PRODUCTO_AW_TASA_INVALIDA",
                "La tasa de IVA trasladado debe ser fracción entre 0 y 1.");
        if (tasaRetencionIva is < 0 or > 1)
            throw new BusinessRuleException("PRODUCTO_AW_TASA_INVALIDA",
                "La tasa de retención de IVA debe ser fracción entre 0 y 1.");
        if (tasaRetencionIsr is < 0 or > 1)
            throw new BusinessRuleException("PRODUCTO_AW_TASA_INVALIDA",
                "La tasa de retención de ISR debe ser fracción entre 0 y 1.");
    }

    private static void ValidarAduana(
        string? fraccionArancelaria, string? unidadAduana, decimal? pesoUnitarioKg)
    {
        if (fraccionArancelaria is not null
            && !System.Text.RegularExpressions.Regex.IsMatch(fraccionArancelaria, @"^\d{8,10}$"))
            throw new BusinessRuleException("PRODUCTO_AW_FRACCION_INVALIDA",
                "La fracción arancelaria debe tener de 8 a 10 dígitos numéricos (c_FraccionArancelaria).");
        if (unidadAduana is { Length: 0 or > 3 })
            throw new BusinessRuleException("PRODUCTO_AW_UNIDAD_ADUANA_INVALIDA",
                "La unidad aduanera no puede estar vacía ni exceder 3 caracteres (c_UnidadAduana).");
        if (pesoUnitarioKg is < 0)
            throw new BusinessRuleException("PRODUCTO_AW_PESO_INVALIDO",
                "El peso unitario (kg) no puede ser negativo.");
    }
}
