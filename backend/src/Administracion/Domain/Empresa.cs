using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Razón social del grupo Millet. Vive en el esquema <c>compartido</c>
/// (catálogo global, multi-tenant lógico). Cada fila representa un
/// tenant lógico al que otras entidades hacen FK vía <c>empresa_id</c>.
/// Ver ADR-0011 y 01-diseno §4.2 del módulo Administración.
///
/// <para>
/// <see cref="Empresa"/> es la RAÍZ del tenant — no implementa
/// <see cref="IPerteneceAEmpresa"/> (no pertenece a sí misma). F1-ADM-01
/// agrega <see cref="EmpresaPadreId"/> para modelar jerarquías simples de
/// máximo 2 niveles (raíz + hijas): una empresa que ya tiene padre no
/// puede a su vez ser padre de otra. Ver <see cref="AsignarEmpresaPadre"/>.
/// </para>
/// </summary>
public sealed class Empresa : BaseEntity, IAuditable
{
    /// <summary>
    /// Business key corta, única global (F1-ADM-01). Formato libre
    /// razonable (sin espacios, máx 20 caracteres) — Millet la validará
    /// en Fase 2/UI; hoy solo se enforce el shape mínimo.
    /// </summary>
    public string Clave { get; private set; } = string.Empty;

    /// <summary>
    /// FK autorreferencial opcional. <c>null</c> = empresa raíz. Cuando
    /// tiene valor, la empresa referenciada por <see cref="EmpresaPadreId"/>
    /// debe ser ella misma una raíz (profundidad máxima 2 niveles) — ver
    /// <see cref="AsignarEmpresaPadre"/>.
    /// </summary>
    public Guid? EmpresaPadreId { get; private set; }

    public string Rfc { get; private set; } = string.Empty;

    public string RazonSocial { get; private set; } = string.Empty;

    public string? NombreComercial { get; private set; }

    public string RegimenFiscal { get; private set; } = string.Empty;

    /// <summary>
    /// Tasa de IVA default de la empresa (fracción, p.ej. 0.16) para captura
    /// manual en Facturación (FAC-DET-PR2). Precedencia efectiva en pedidos
    /// manuales: IVA del artículo (<c>ProductoAw.TasaIvaTraslado</c>) &gt;
    /// esta tasa. Los pedidos A+W traen la tasa del documento origen y no
    /// usan este default. <c>null</c> = sin default configurado.
    /// </summary>
    public decimal? TasaIvaDefault { get; private set; }

    // --- Domicilio fiscal estructurado (F1-ADM-01) ---
    // CodigoPostal ya existía (LugarExpedicion del CFDI); el resto es nuevo.
    public string Calle { get; private set; } = string.Empty;
    public string NumeroExterior { get; private set; } = string.Empty;
    public string? NumeroInterior { get; private set; }
    public string Colonia { get; private set; } = string.Empty;
    public string Ciudad { get; private set; } = string.Empty;
    public string Municipio { get; private set; } = string.Empty;
    public string Estado { get; private set; } = string.Empty;
    public string Pais { get; private set; } = string.Empty;

    /// <summary>
    /// Código postal fiscal de la empresa (domicilio fiscal SAT). Es el
    /// <c>LugarExpedicion</c> del CFDI 4.0 (obligatorio para timbrar,
    /// F12-PR1). <c>null</c> = sin capturar — la emisión de CFDI falla con
    /// <c>EMISOR_SIN_LUGAR_EXPEDICION</c> hasta capturarlo.
    /// </summary>
    public string? CodigoPostal { get; private set; }

    /// <summary>
    /// FK opcional a <see cref="Catalogos.Domain.Moneda"/> (moneda operativa
    /// default de la empresa). <c>null</c> = sin default configurado
    /// (F1-ADM-01; sin FK física de navegación, solo el Guid — mismo patrón
    /// del resto del dominio).
    /// </summary>
    public Guid? MonedaId { get; private set; }

    public bool Activa { get; private set; } = true;

    private Empresa() { } // EF Core

    public Empresa(
        Guid id,
        string clave,
        string rfc,
        string razonSocial,
        string regimenFiscal,
        string calle,
        string numeroExterior,
        string colonia,
        string ciudad,
        string municipio,
        string estado,
        string pais,
        string? numeroInterior = null,
        string? nombreComercial = null,
        Guid? empresaPadreId = null,
        bool empresaPadreEsRaiz = true,
        Guid? monedaId = null,
        decimal? tasaIvaDefault = null,
        string? codigoPostal = null)
        : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("EMPRESA_ID_INVALIDO", "El id es obligatorio.");
        ValidarClave(clave);
        ValidarRfc(rfc);
        ValidarRazonSocial(razonSocial);
        ValidarRegimenFiscal(regimenFiscal);
        ValidarNombreComercial(nombreComercial);
        ValidarJerarquia(id, empresaPadreId, empresaPadreEsRaiz);
        ValidarCalle(calle);
        ValidarNumeroExterior(numeroExterior);
        ValidarNumeroInterior(numeroInterior);
        ValidarColonia(colonia);
        ValidarCiudad(ciudad);
        ValidarMunicipio(municipio);
        ValidarEstado(estado);
        ValidarPais(pais);
        if (codigoPostal is not null) ValidarCodigoPostal(codigoPostal);
        if (tasaIvaDefault is not null) ValidarTasaIvaDefault(tasaIvaDefault.Value);

        Clave = clave;
        EmpresaPadreId = empresaPadreId;
        Rfc = rfc;
        RazonSocial = razonSocial;
        RegimenFiscal = regimenFiscal;
        NombreComercial = nombreComercial;
        Calle = calle;
        NumeroExterior = numeroExterior;
        NumeroInterior = numeroInterior;
        Colonia = colonia;
        Ciudad = ciudad;
        Municipio = municipio;
        Estado = estado;
        Pais = pais;
        CodigoPostal = codigoPostal;
        MonedaId = monedaId;
        TasaIvaDefault = tasaIvaDefault;
    }

    /// <summary>
    /// PATCH parcial sobre los campos editables de la empresa
    /// (F-Admin-PR2.1). Convención: parámetro <c>null</c> = no tocar;
    /// flag <c>limpiarX = true</c> = setear nullable a null. Inmutables:
    /// <see cref="BaseEntity.Id"/>, <see cref="Rfc"/> (RFC es business
    /// key fiscal — cambios reales requieren alta nueva por SAT) y
    /// <see cref="Clave"/> (business key interna).
    /// </summary>
    public void ActualizarDatos(
        string? razonSocial = null,
        string? nombreComercial = null,
        string? regimenFiscal = null,
        bool limpiarNombreComercial = false,
        decimal? tasaIvaDefault = null,
        bool limpiarTasaIvaDefault = false,
        string? codigoPostal = null,
        string? calle = null,
        string? numeroExterior = null,
        string? numeroInterior = null,
        bool limpiarNumeroInterior = false,
        string? colonia = null,
        string? ciudad = null,
        string? municipio = null,
        string? estado = null,
        string? pais = null,
        Guid? monedaId = null,
        bool limpiarMoneda = false)
    {
        if (razonSocial is not null)
        {
            ValidarRazonSocial(razonSocial);
            RazonSocial = razonSocial;
        }
        if (regimenFiscal is not null)
        {
            ValidarRegimenFiscal(regimenFiscal);
            RegimenFiscal = regimenFiscal;
        }
        if (nombreComercial is not null)
        {
            ValidarNombreComercial(nombreComercial);
            NombreComercial = nombreComercial;
        }
        else if (limpiarNombreComercial)
        {
            NombreComercial = null;
        }
        if (tasaIvaDefault is not null)
        {
            ValidarTasaIvaDefault(tasaIvaDefault.Value);
            TasaIvaDefault = tasaIvaDefault;
        }
        else if (limpiarTasaIvaDefault)
        {
            TasaIvaDefault = null;
        }
        if (codigoPostal is not null)
        {
            ValidarCodigoPostal(codigoPostal);
            CodigoPostal = codigoPostal;
        }
        if (calle is not null)
        {
            ValidarCalle(calle);
            Calle = calle;
        }
        if (numeroExterior is not null)
        {
            ValidarNumeroExterior(numeroExterior);
            NumeroExterior = numeroExterior;
        }
        if (limpiarNumeroInterior)
        {
            NumeroInterior = null;
        }
        else if (numeroInterior is not null)
        {
            ValidarNumeroInterior(numeroInterior);
            NumeroInterior = numeroInterior;
        }
        if (colonia is not null)
        {
            ValidarColonia(colonia);
            Colonia = colonia;
        }
        if (ciudad is not null)
        {
            ValidarCiudad(ciudad);
            Ciudad = ciudad;
        }
        if (municipio is not null)
        {
            ValidarMunicipio(municipio);
            Municipio = municipio;
        }
        if (estado is not null)
        {
            ValidarEstado(estado);
            Estado = estado;
        }
        if (pais is not null)
        {
            ValidarPais(pais);
            Pais = pais;
        }
        if (limpiarMoneda)
        {
            MonedaId = null;
        }
        else if (monedaId is not null)
        {
            MonedaId = monedaId;
        }
    }

    /// <summary>
    /// Asigna (o limpia) la empresa padre. Invariantes:
    /// <list type="bullet">
    ///   <item>Auto-referencia trivial: <paramref name="empresaPadreId"/>
    ///         no puede ser el propio <see cref="BaseEntity.Id"/>. Se
    ///         verifica aquí sin necesidad de I/O.</item>
    ///   <item>Profundidad máxima 2 niveles (raíz + hijas): la empresa
    ///         padre candidata NO puede a su vez tener padre. Esto es un
    ///         dato cross-entity que este método no puede resolver por sí
    ///         mismo — el caller (handler de Fase 2, p.ej.
    ///         <c>CrearEmpresaCommand</c> / <c>ActualizarEmpresaCommand</c>)
    ///         debe consultar la empresa padre candidata y pasar
    ///         <paramref name="padreEsRaiz"/> = <c>true</c> solo si esa
    ///         empresa tiene <c>EmpresaPadreId == null</c>.</item>
    /// </list>
    /// </summary>
    public void AsignarEmpresaPadre(Guid? empresaPadreId, bool padreEsRaiz = true)
    {
        ValidarJerarquia(Id, empresaPadreId, padreEsRaiz);
        EmpresaPadreId = empresaPadreId;
    }

    /// <summary>
    /// Reactiva una empresa previamente desactivada. Idempotente —
    /// si ya está activa, no-op.
    /// </summary>
    public void Activar() => Activa = true;

    /// <summary>
    /// Desactiva la empresa. La validación cross-entity (no desactivar
    /// si tiene sucursales activas, ADR-0011) vive en el handler
    /// (<c>DesactivarEmpresaCommand</c> de F-Admin-PR2.3) porque
    /// requiere consultar otra tabla. Esta capa se limita a la
    /// transición de estado idempotente.
    /// </summary>
    public void Desactivar() => Activa = false;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("EMPRESA_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
        if (clave.Any(char.IsWhiteSpace))
            throw new BusinessRuleException("EMPRESA_CLAVE_INVALIDA",
                "La clave no puede contener espacios.");
    }

    private static void ValidarJerarquia(Guid id, Guid? empresaPadreId, bool padreEsRaiz)
    {
        if (empresaPadreId is null) return;

        if (empresaPadreId == id)
            throw new BusinessRuleException("EMPRESA_PADRE_AUTOREFERENCIA",
                "Una empresa no puede ser su propia empresa padre.");

        if (!padreEsRaiz)
            throw new BusinessRuleException("EMPRESA_JERARQUIA_PROFUNDIDAD_EXCEDIDA",
                "La empresa padre ya tiene, a su vez, una empresa padre. " +
                "La jerarquía admite máximo 2 niveles (raíz + hijas).");
    }

    private static void ValidarRfc(string rfc)
    {
        if (string.IsNullOrWhiteSpace(rfc) || rfc.Length is < 12 or > 13)
            throw new BusinessRuleException("EMPRESA_RFC_INVALIDO",
                "El RFC debe tener 12 (moral) o 13 (física) caracteres.");
    }

    private static void ValidarRazonSocial(string razonSocial)
    {
        if (string.IsNullOrWhiteSpace(razonSocial) || razonSocial.Length > 254)
            throw new BusinessRuleException("EMPRESA_RAZON_SOCIAL_INVALIDA",
                "La razón social es requerida y no puede exceder 254 caracteres.");
    }

    private static void ValidarRegimenFiscal(string regimenFiscal)
    {
        if (string.IsNullOrWhiteSpace(regimenFiscal) || regimenFiscal.Length > 10)
            throw new BusinessRuleException("EMPRESA_REGIMEN_FISCAL_INVALIDO",
                "El régimen fiscal es requerido y no puede exceder 10 caracteres.");
    }

    private static void ValidarTasaIvaDefault(decimal tasa)
    {
        if (tasa is < 0 or > 1)
            throw new BusinessRuleException("EMPRESA_TASA_IVA_INVALIDA",
                "La tasa de IVA default debe ser fracción entre 0 y 1 (p.ej. 0.16).");
    }

    private static void ValidarCodigoPostal(string codigoPostal)
    {
        if (codigoPostal.Length != 5 || !codigoPostal.All(char.IsAsciiDigit))
            throw new BusinessRuleException("EMPRESA_CODIGO_POSTAL_INVALIDO",
                "El código postal fiscal debe ser de 5 dígitos (catálogo c_CodigoPostal del SAT).");
    }

    private static void ValidarNombreComercial(string? nombreComercial)
    {
        if (nombreComercial is { Length: > 254 })
            throw new BusinessRuleException("EMPRESA_NOMBRE_COMERCIAL_INVALIDO",
                "El nombre comercial no puede exceder 254 caracteres.");
    }

    private static void ValidarCalle(string calle)
    {
        if (string.IsNullOrWhiteSpace(calle) || calle.Length > 254)
            throw new BusinessRuleException("EMPRESA_CALLE_INVALIDA",
                "La calle es requerida y no puede exceder 254 caracteres.");
    }

    private static void ValidarNumeroExterior(string numeroExterior)
    {
        if (string.IsNullOrWhiteSpace(numeroExterior) || numeroExterior.Length > 20)
            throw new BusinessRuleException("EMPRESA_NUMERO_EXTERIOR_INVALIDO",
                "El número exterior es requerido y no puede exceder 20 caracteres.");
    }

    private static void ValidarNumeroInterior(string? numeroInterior)
    {
        if (numeroInterior is { Length: > 20 })
            throw new BusinessRuleException("EMPRESA_NUMERO_INTERIOR_INVALIDO",
                "El número interior no puede exceder 20 caracteres.");
    }

    private static void ValidarColonia(string colonia)
    {
        if (string.IsNullOrWhiteSpace(colonia) || colonia.Length > 254)
            throw new BusinessRuleException("EMPRESA_COLONIA_INVALIDA",
                "La colonia es requerida y no puede exceder 254 caracteres.");
    }

    private static void ValidarCiudad(string ciudad)
    {
        if (string.IsNullOrWhiteSpace(ciudad) || ciudad.Length > 100)
            throw new BusinessRuleException("EMPRESA_CIUDAD_INVALIDA",
                "La ciudad es requerida y no puede exceder 100 caracteres.");
    }

    private static void ValidarMunicipio(string municipio)
    {
        if (string.IsNullOrWhiteSpace(municipio) || municipio.Length > 100)
            throw new BusinessRuleException("EMPRESA_MUNICIPIO_INVALIDO",
                "El municipio es requerido y no puede exceder 100 caracteres.");
    }

    private static void ValidarEstado(string estado)
    {
        if (string.IsNullOrWhiteSpace(estado) || estado.Length > 100)
            throw new BusinessRuleException("EMPRESA_ESTADO_INVALIDO",
                "El estado es requerido y no puede exceder 100 caracteres.");
    }

    private static void ValidarPais(string pais)
    {
        if (string.IsNullOrWhiteSpace(pais) || pais.Length > 100)
            throw new BusinessRuleException("EMPRESA_PAIS_INVALIDO",
                "El país es requerido y no puede exceder 100 caracteres.");
    }
}
