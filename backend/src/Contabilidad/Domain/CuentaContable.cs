using System.Text.Json.Serialization;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NaturalezaCuenta : short { Deudora = 0, Acreedora = 1 }

/// <summary>Título agrupa y no recibe movimientos; afectable es hoja y sí los recibe.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoCuenta : short { Titulo = 0, Afectable = 1 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CuentaControl : short { Ninguna = 0, Clientes = 1, Proveedores = 2 }

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

    public bool Activa => Estatus == EstatusCatalogo.Activo;

    /// <summary>P14: naturaleza o tipo nulos = pendiente de validación por Contabilidad (estado calculado; no se supone ni se deriva).</summary>
    public bool PendienteValidacion => Naturaleza is null || Tipo is null;

    private CuentaContable() { }

    public CuentaContable(
        Guid id, string codigo, string nombre, Guid? padreId, int nivel,
        NaturalezaCuenta? naturaleza, TipoCuenta? tipo, CuentaControl control,
        string? codigoAgrupador, string? grupoReporte) : base(id)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 30)
            throw new BusinessRuleException("CONTAB_CUENTA_CODIGO_INVALIDO",
                "El código es requerido y no puede exceder 30 caracteres.");
        Codigo = codigo;
        Editar(nombre, padreId, nivel, naturaleza, tipo, control, codigoAgrupador, grupoReporte);
    }

    public void Editar(
        string nombre, Guid? padreId, int nivel, NaturalezaCuenta? naturaleza, TipoCuenta? tipo,
        CuentaControl control, string? codigoAgrupador, string? grupoReporte)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("CONTAB_CUENTA_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");
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
        Nivel = (short)nivel;
        Naturaleza = naturaleza;
        Tipo = tipo;
        CuentaControl = control;
        CodigoAgrupador = codigoAgrupador;
        GrupoReporte = grupoReporte;
    }

    public void FijarNivel(int nivel) => Nivel = (short)nivel;
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
