using System.Text.Json.Serialization;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>
/// Dimensiones contables (F1-CON-02, D1): son los niveles del catálogo de Centros de Costo de ADM-08
/// (Dim1 → Dim2/CeCo → Dim3/máquina). No hay otro catálogo de centros. El valor es fijo por ABI: agregar al final.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DimensionContable : short { Dim1 = 1, Dim2 = 2, Dim3 = 3 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequerimientoDimension : short { Obligatorio = 1, Opcional = 2, NoAplica = 3 }

/// <summary>
/// Tipo de documento contable (D2): catálogo propio de Contabilidad. No reutiliza <c>TipoDocumentoSerie</c> (folios).
/// Mientras Millet no entregue su lista, solo hay tipos de prueba (<see cref="EsPrueba"/>, clave <c>FIX-*</c>).
/// </summary>
public sealed class TipoDocumentoContable : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;
    public bool EsPrueba { get; private set; }

    public bool Activo => Estatus == EstatusCatalogo.Activo;

    private TipoDocumentoContable() { }

    public TipoDocumentoContable(Guid id, string clave, string nombre, bool esPrueba) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("CONTAB_TIPO_DOC_CLAVE_INVALIDA", "La clave del tipo de documento es requerida y admite hasta 20 caracteres.");
        Clave = clave;
        EsPrueba = esPrueba;
        Editar(nombre);
    }

    public void Editar(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 120)
            throw new BusinessRuleException("CONTAB_TIPO_DOC_NOMBRE_INVALIDO", "El nombre del tipo de documento es requerido y admite hasta 120 caracteres.");
        Nombre = nombre;
    }

    public void CambiarEstatus(bool activo) => Estatus = activo ? EstatusCatalogo.Activo : EstatusCatalogo.Inactivo;
}

/// <summary>
/// Regla cuenta × tipo de documento × dimensión con vigencia (D3/D4). La cuenta puede ser una rama (título): aplica a sus
/// descendientes salvo que haya una regla más específica. <see cref="TipoDocumentoId"/> null = todos los tipos.
/// Cambiar la política = cerrar la vigencia y crear otra regla; una regla ya iniciada no se edita (conserva la historia).
/// </summary>
public sealed class ReglaDimension : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid CuentaId { get; private set; }
    public Guid? TipoDocumentoId { get; private set; }
    public DimensionContable Dimension { get; private set; }
    public RequerimientoDimension Requerimiento { get; private set; }
    public DateOnly VigenteDesde { get; private set; }
    public DateOnly? VigenteHasta { get; private set; }
    public bool EsPrueba { get; private set; }
    public string? Nota { get; private set; }

    private ReglaDimension() { }

    public ReglaDimension(
        Guid id, Guid cuentaId, Guid? tipoDocumentoId, DimensionContable dimension, RequerimientoDimension requerimiento,
        DateOnly vigenteDesde, DateOnly? vigenteHasta, bool esPrueba, string? nota) : base(id)
    {
        if (cuentaId == Guid.Empty)
            throw new BusinessRuleException("CONTAB_REGLA_CUENTA_INVALIDA", "La regla debe indicar una cuenta contable.");
        CuentaId = cuentaId;
        TipoDocumentoId = tipoDocumentoId;
        Dimension = Enum.IsDefined(dimension) ? dimension
            : throw new BusinessRuleException("CONTAB_REGLA_DIMENSION_INVALIDA", "La dimensión indicada no existe.");
        EsPrueba = esPrueba;
        Reprogramar(requerimiento, vigenteDesde, vigenteHasta, nota);
    }

    /// <summary>Solo para reglas que aún no inician (lo verifica Application con la fecha de hoy).</summary>
    public void Reprogramar(RequerimientoDimension requerimiento, DateOnly vigenteDesde, DateOnly? vigenteHasta, string? nota)
    {
        if (!Enum.IsDefined(requerimiento))
            throw new BusinessRuleException("CONTAB_REGLA_REQUERIMIENTO_INVALIDO", "El requerimiento debe ser obligatorio, opcional o no aplica.");
        ValidarVigencia(vigenteDesde, vigenteHasta);
        if (nota?.Length > 500)
            throw new BusinessRuleException("CONTAB_REGLA_NOTA_INVALIDA", "La nota admite hasta 500 caracteres.");
        Requerimiento = requerimiento;
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
        Nota = nota;
    }

    public void Cerrar(DateOnly vigenteHasta)
    {
        ValidarVigencia(VigenteDesde, vigenteHasta);
        VigenteHasta = vigenteHasta;
    }

    public bool VigenteEn(DateOnly fecha) => VigenteDesde <= fecha && (VigenteHasta is null || fecha <= VigenteHasta);

    public bool SeTraslapaCon(DateOnly desde, DateOnly? hasta) =>
        VigenteDesde <= (hasta ?? DateOnly.MaxValue) && desde <= (VigenteHasta ?? DateOnly.MaxValue);

    private static void ValidarVigencia(DateOnly desde, DateOnly? hasta)
    {
        if (hasta is { } h && h < desde)
            throw new BusinessRuleException("CONTAB_REGLA_VIGENCIA_INVALIDA", "La fecha final de la vigencia no puede ser anterior a la inicial.");
    }
}

/// <summary>
/// Sucursales en las que se puede usar un centro de costo de nivel Dim2 (CeCo) en movimientos contables (D6).
/// Una Dim3 hereda las sucursales de su Dim2. Vive en Contabilidad porque ADM-08 decidió no relacionar el catálogo de
/// centros con sucursales; aquí es el alcance contable del centro, no un atributo del catálogo.
/// </summary>
public sealed class CentroCostoSucursal : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid Dim2Id { get; private set; }
    public Guid SucursalId { get; private set; }

    private CentroCostoSucursal() { }

    public CentroCostoSucursal(Guid id, Guid dim2Id, Guid sucursalId) : base(id)
    {
        if (dim2Id == Guid.Empty || sucursalId == Guid.Empty)
            throw new BusinessRuleException("CONTAB_CENTRO_SUCURSAL_INVALIDO", "Indique el centro de costo y la sucursal.");
        Dim2Id = dim2Id;
        SucursalId = sucursalId;
    }
}

/// <summary>
/// Movimiento de PRUEBA confirmado (D7): snapshot de lo capturado y de las reglas que lo validaron. Solo inserción.
/// Demuestra la conservación histórica mientras no existan las pólizas. No marca la cuenta como usada.
/// </summary>
// PLATFORM-TODO(<Polizas>): retirar cuando la póliza confirme sus partidas con IDimensionContableValidacionPort y guarde los regla_id.
public sealed class MovimientoDimensionPrueba : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid SucursalId { get; private set; }
    public Guid CuentaId { get; private set; }
    public string CuentaCodigo { get; private set; } = string.Empty;
    public Guid TipoDocumentoId { get; private set; }
    public string TipoDocumentoClave { get; private set; } = string.Empty;
    public DateOnly FechaContable { get; private set; }
    public Guid? Dim1Id { get; private set; }
    public Guid? Dim2Id { get; private set; }
    public Guid? Dim3Id { get; private set; }
    public string? Referencia { get; private set; }
    /// <summary>JSON con las reglas aplicadas (id, dimensión, requerimiento, vigencia, cuenta de origen) al confirmar.</summary>
    public string ReglasAplicadas { get; private set; } = "[]";
    public DateTimeOffset ConfirmadoEn { get; private set; }

    private MovimientoDimensionPrueba() { }

    public MovimientoDimensionPrueba(
        Guid id, Guid sucursalId, Guid cuentaId, string cuentaCodigo, Guid tipoDocumentoId, string tipoDocumentoClave,
        DateOnly fechaContable, Guid? dim1Id, Guid? dim2Id, Guid? dim3Id, string? referencia, string reglasAplicadas,
        DateTimeOffset confirmadoEn) : base(id)
    {
        if (referencia?.Length > 100)
            throw new BusinessRuleException("CONTAB_MOVIMIENTO_REFERENCIA_INVALIDA", "La referencia admite hasta 100 caracteres.");
        SucursalId = sucursalId;
        CuentaId = cuentaId;
        CuentaCodigo = cuentaCodigo;
        TipoDocumentoId = tipoDocumentoId;
        TipoDocumentoClave = tipoDocumentoClave;
        FechaContable = fechaContable;
        Dim1Id = dim1Id;
        Dim2Id = dim2Id;
        Dim3Id = dim3Id;
        Referencia = referencia;
        ReglasAplicadas = reglasAplicadas;
        ConfirmadoEn = confirmadoEn;
    }
}
