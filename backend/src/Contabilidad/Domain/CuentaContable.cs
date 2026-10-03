using System.Text.Json.Serialization;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NaturalezaCuenta : short { Deudora = 0, Acreedora = 1 }

/// <summary>Título = acumula (no recibe movimientos); afectable = recibe movimientos (P19: se deriva de la jerarquía).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoCuenta : short { Titulo = 0, Afectable = 1 }

/// <summary>Cuentas colectivas (P23): se afectan solo desde su módulo, que lleva el detalle por persona.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CuentaControl : short { Ninguna = 0, Clientes = 1, Proveedores = 2, Deudores = 3, Acreedores = 4 }

/// <summary>
/// P24 (propuesta del TL para pruebas): un rubro es una agrupación de REPORTE separada del árbol de niveles: no tiene padre
/// ni hijas, no recibe movimientos y agrupa cuentas de nivel 1 por relación explícita (<see cref="CuentaContable.RubroId"/>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClaseCuenta : short { Cuenta = 0, Rubro = 1 }

/// <summary>Origen declarado por quien quiere afectar una cuenta (regla R10).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrigenMovimiento : short { Manual = 0, AuxiliarCxC = 1, AuxiliarCxP = 2 }

/// <summary>
/// Cuenta del catálogo contable de Millet (<c>contabilidad.cuentas_contables</c>).
/// Una sola razón social: <see cref="EmpresaId"/> técnico (ADR-0011); sin sucursal (ADR-0051 no aplica).
/// El código es inmutable y único por empresa incluyendo inactivas (P9). Las reglas que
/// dependen del árbol o de la configuración de formato viven en la capa Application.
/// </summary>
public sealed class CuentaContable : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public string Codigo { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public Guid? PadreId { get; private set; }
    public short Nivel { get; private set; }
    public NaturalezaCuenta? Naturaleza { get; private set; }
    public TipoCuenta? Tipo { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;
    public CuentaControl CuentaControl { get; private set; }
    public string? CodigoAgrupador { get; private set; }
    public string? GrupoReporte { get; private set; }
    public ClaseCuenta Clase { get; private set; }
    /// <summary>Rubro de reporte al que pertenece una cuenta de nivel 1 (P24). Null en rubros y en cuentas con padre.</summary>
    public Guid? RubroId { get; private set; }

    public bool EsRubro => Clase == ClaseCuenta.Rubro;
    public bool Activa => Estatus == EstatusCatalogo.Activo;

    /// <summary>P14/P19: naturaleza nula = pendiente de validación por Contabilidad (el tipo ya no queda pendiente: se deriva).</summary>
    public bool PendienteValidacion => !EsRubro && Naturaleza is null;

    /// <summary>
    /// P19 (reglas de Contabilidad): nivel 1 acumula; una cuenta con hijas (activas o no) acumula; nivel ≥ 2 sin hijas recibe movimientos.
    /// </summary>
    public static TipoCuenta DerivarTipo(int nivel, bool tieneHijas) =>
        nivel <= 1 || tieneHijas ? TipoCuenta.Titulo : TipoCuenta.Afectable;

    private CuentaContable() { }

    public CuentaContable(
        Guid id, string codigo, string nombre, Guid? padreId, int nivel,
        NaturalezaCuenta? naturaleza, TipoCuenta? tipo, CuentaControl control,
        string? codigoAgrupador, string? grupoReporte, ClaseCuenta clase = ClaseCuenta.Cuenta) : base(id)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 30)
            throw new BusinessRuleException("CONTAB_CUENTA_CODIGO_INVALIDO",
                "El código es requerido y no puede exceder 30 caracteres.");
        Codigo = codigo;
        Clase = Enum.IsDefined(clase) ? clase : throw new BusinessRuleException("CONTAB_CUENTA_RUBRO_INVALIDO", "La clase de cuenta no es válida.");
        Editar(nombre, padreId, nivel, naturaleza, tipo, control, codigoAgrupador, grupoReporte);
    }

    public void Editar(
        string nombre, Guid? padreId, int nivel, NaturalezaCuenta? naturaleza, TipoCuenta? tipo,
        CuentaControl control, string? codigoAgrupador, string? grupoReporte)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("CONTAB_CUENTA_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");
        if (EsRubro && (padreId is not null || control != CuentaControl.Ninguna))
            throw new BusinessRuleException("CONTAB_CUENTA_RUBRO_INVALIDO",
                "Un rubro es una agrupación de reporte: no tiene cuenta padre ni puede ser cuenta colectiva.");
        if (EsRubro) tipo = TipoCuenta.Titulo; // un rubro nunca recibe movimientos
        if (naturaleza is { } n && !Enum.IsDefined(n))
            throw new BusinessRuleException("CONTAB_CUENTA_NATURALEZA_INVALIDA", "La naturaleza no es válida.");
        if (control != CuentaControl.Ninguna && tipo != TipoCuenta.Afectable)
            throw new BusinessRuleException("CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE",
                "Solo una cuenta afectable puede ser cuenta de control.");
        if (codigoAgrupador?.Length > 30 || grupoReporte?.Length > 60)
            throw new BusinessRuleException("CONTAB_CUENTA_NOMBRE_INVALIDO",
                "El código agrupador admite 30 caracteres y el grupo de reporte 60.");

        Nombre = nombre;
        PadreId = padreId;
        if (padreId is not null) RubroId = null; // solo las cuentas de nivel 1 pertenecen a un rubro
        Nivel = (short)nivel;
        Naturaleza = naturaleza;
        Tipo = tipo;
        CuentaControl = control;
        CodigoAgrupador = codigoAgrupador;
        GrupoReporte = grupoReporte;
    }

    public void FijarNivel(int nivel) => Nivel = (short)nivel;

    /// <summary>P20: una afectable que recibe su primera hija pasa a acumular (el llamador ya verificó que no tiene movimientos ni es colectiva).</summary>
    public void ConvertirEnAcumulativa() => Tipo = TipoCuenta.Titulo;
    /// <summary>P24: asocia (o desasocia con null) una cuenta de nivel 1 a un rubro. Que el destino sea un rubro lo valida Application.</summary>
    public void AsignarRubro(Guid? rubroId)
    {
        if (rubroId is not null && (EsRubro || PadreId is not null))
            throw new BusinessRuleException("CONTAB_CUENTA_RUBRO_INVALIDO",
                "Solo una cuenta de nivel 1 (sin cuenta padre) puede pertenecer a un rubro.");
        RubroId = rubroId;
    }

    public void Desactivar() => Estatus = EstatusCatalogo.Inactivo;
    public void Reactivar() => Estatus = EstatusCatalogo.Activo;
}

/// <summary>Correspondencia (fuente, código de origen) → cuenta; permite reimportar sin duplicar (§4.2).</summary>
public sealed class CuentaContableOrigen : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid CuentaId { get; private set; }
    public string Fuente { get; private set; } = string.Empty;
    public string CodigoOrigen { get; private set; } = string.Empty;
    public Guid LoteId { get; private set; }

    private CuentaContableOrigen() { }

    public CuentaContableOrigen(Guid id, Guid cuentaId, string fuente, string codigoOrigen, Guid loteId) : base(id)
    {
        CuentaId = cuentaId;
        Fuente = fuente;
        CodigoOrigen = codigoOrigen;
        LoteId = loteId;
    }
}

/// <summary>Lote de importación aplicado. La huella (sha256 de filas canónicas) da idempotencia por archivo.</summary>
public sealed class ImportacionCatalogo : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public string Fuente { get; private set; } = string.Empty;
    public string? ArchivoNombre { get; private set; }
    public string HuellaSha256 { get; private set; } = string.Empty;
    public int TotalFilas { get; private set; }
    public int Creadas { get; private set; }
    public int Actualizadas { get; private set; }
    public int SinCambios { get; private set; }
    public DateTimeOffset AplicadoEn { get; private set; }
    public string? AplicadoPor { get; private set; }

    private ImportacionCatalogo() { }

    public ImportacionCatalogo(
        Guid id, string fuente, string? archivoNombre, string huella, int total, int creadas,
        int actualizadas, int sinCambios, DateTimeOffset aplicadoEn, string? aplicadoPor) : base(id)
    {
        Fuente = fuente;
        ArchivoNombre = archivoNombre;
        HuellaSha256 = huella;
        TotalFilas = total;
        Creadas = creadas;
        Actualizadas = actualizadas;
        SinCambios = sinCambios;
        AplicadoEn = aplicadoEn;
        AplicadoPor = aplicadoPor;
    }
}

/// <summary>
/// Una fila ⇒ la cuenta se considera usada (R8, P4). La escribe solo
/// <c>RegistrarUsoCuentaCommand</c>; los consumidores nunca escriben aquí.
/// </summary>
public sealed class CuentaContableUso : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid CuentaId { get; private set; }
    public string Consumidor { get; private set; } = string.Empty;
    public DateTimeOffset PrimerUsoEn { get; private set; }
    public string? Referencia { get; private set; }

    private CuentaContableUso() { }

    public CuentaContableUso(Guid id, Guid cuentaId, string consumidor, DateTimeOffset primerUsoEn, string? referencia)
        : base(id)
    {
        CuentaId = cuentaId;
        Consumidor = consumidor;
        PrimerUsoEn = primerUsoEn;
        Referencia = referencia;
    }
}
