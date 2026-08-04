using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Unidad de medida del catálogo cross-empresa
/// <c>compartido.unidades_medida</c> (ADR-0046, Etapa 1a). Reemplaza el
/// string libre <c>varchar(20)</c> que hoy vive en <c>Articulo</c> y en los
/// snapshots de las líneas.
///
/// <para>Cada unidad pertenece a una <see cref="DimensionUnidad"/> y declara
/// su <see cref="FactorABase"/> (cuántas unidades base equivale: G→KG = 0.001)
/// y sus <see cref="Decimales"/> permitidos (PZA = 0 → discreta; KG = 3). La
/// validación de decimales por unidad es Etapa 2; aquí solo se almacena el
/// dato.</para>
///
/// <para><b>Guardrail (ADR-0046):</b> <see cref="Nombre"/>, <see cref="Decimales"/>
/// y <see cref="Estatus"/> son editables siempre; <see cref="Dimension"/>,
/// <see cref="FactorABase"/> y <see cref="EsBase"/> solo se pueden reconfigurar
/// si la unidad NO está en uso (ver <see cref="ReconfigurarConversion"/>),
/// porque cambiarlos reinterpretaría cantidades ya capturadas. <see cref="Codigo"/>
/// es inmutable (business key).</para>
/// </summary>
public sealed class UnidadMedida : BaseEntity, IAuditable
{
    public string Codigo { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public DimensionUnidad Dimension { get; private set; }

    /// <summary>Cuántas unidades base equivale 1 de esta unidad. La base de la
    /// dimensión tiene <c>1</c>. Debe ser &gt; 0.</summary>
    public decimal FactorABase { get; private set; }

    /// <summary>Decimales permitidos al capturar cantidad con esta unidad
    /// (0 = discreta). Lo consumirá la validación de Etapa 2.</summary>
    public int Decimales { get; private set; }

    /// <summary>Unidad base de su dimensión (<c>FactorABase = 1</c>).</summary>
    public bool EsBase { get; private set; }

    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private UnidadMedida() { }

    public UnidadMedida(
        Guid id,
        string codigo,
        string nombre,
        DimensionUnidad dimension,
        decimal factorABase,
        int decimales,
        bool esBase,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        ValidarCodigo(codigo);
        ValidarNombre(nombre);
        ValidarDimension(dimension);
        ValidarFactor(factorABase);
        ValidarDecimales(decimales);

        Codigo = codigo;
        Nombre = nombre;
        Dimension = dimension;
        FactorABase = factorABase;
        Decimales = decimales;
        EsBase = esBase;
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial de los campos SIEMPRE editables (no afectan la
    /// reinterpretación de cantidades capturadas). Inmutable: <see cref="Codigo"/>.
    /// </summary>
    public void ActualizarDatos(string? nombre = null, int? decimales = null)
    {
        if (nombre is not null)
        {
            ValidarNombre(nombre);
            Nombre = nombre;
        }
        if (decimales is int d)
        {
            ValidarDecimales(d);
            Decimales = d;
        }
    }

    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;

    /// <summary>
    /// Reconfigura la conversión de la unidad (<see cref="Dimension"/>,
    /// <see cref="FactorABase"/>, <see cref="EsBase"/>). GUARDRAIL (ADR-0046):
    /// se bloquea con <c>UNIDAD_MEDIDA_EN_USO</c> si la unidad ya está en uso,
    /// porque cambiar dimensión o factor reinterpretaría cantidades históricas.
    /// El llamador (handler) determina <paramref name="estaEnUso"/>.
    /// </summary>
    public void ReconfigurarConversion(
        DimensionUnidad dimension, decimal factorABase, bool esBase, bool estaEnUso)
    {
        if (estaEnUso)
            throw new BusinessRuleException("UNIDAD_MEDIDA_EN_USO",
                "No se puede cambiar la dimensión ni el factor de conversión de " +
                "una unidad de medida que ya está en uso.");

        ValidarDimension(dimension);
        ValidarFactor(factorABase);

        Dimension = dimension;
        FactorABase = factorABase;
        EsBase = esBase;
    }

    private static void ValidarCodigo(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 20)
            throw new BusinessRuleException("UNIDAD_MEDIDA_CODIGO_INVALIDO",
                "El código es requerido y no puede exceder 20 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
            throw new BusinessRuleException("UNIDAD_MEDIDA_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 100 caracteres.");
    }

    private static void ValidarDimension(DimensionUnidad dimension)
    {
        if (!Enum.IsDefined(dimension))
            throw new BusinessRuleException("UNIDAD_MEDIDA_DIMENSION_INVALIDA",
                "La dimensión no es válida.");
    }

    private static void ValidarFactor(decimal factorABase)
    {
        if (factorABase <= 0m)
            throw new BusinessRuleException("UNIDAD_MEDIDA_FACTOR_INVALIDO",
                "El factor a la unidad base debe ser mayor a 0.");
    }

    private static void ValidarDecimales(int decimales)
    {
        if (decimales is < 0 or > 6)
            throw new BusinessRuleException("UNIDAD_MEDIDA_DECIMALES_INVALIDOS",
                "Los decimales permitidos deben estar entre 0 y 6.");
    }
}
