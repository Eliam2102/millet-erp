using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.DatosMaestros.Domain;

/// <summary>Resultado de la última aplicación de A+W sobre el cliente (ADR-0048, ADM-06).</summary>
public enum ResultadoSincronizacionAw : short
{
    Aplicado = 0,
    SinCambios = 1,
    Pendiente = 2,
    Conflicto = 3,
    Error = 4,
}

/// <summary>
/// Registro de origen A+W 1:1 con <see cref="Cliente"/> (docs/integration/05
/// §4). Guarda los valores crudos recibidos (comercial + candidatos fiscales)
/// y el control de la lectura, <b>sin tocar</b> los datos fiscales locales del
/// cliente. Nulo, 0 y desconocido se conservan distintos.
/// </summary>
public sealed class ClienteSincronizacionAw : BaseEntity, INotAudited
{
    public Guid ClienteId { get; private set; }
    public string ReferenciaExterna { get; private set; } = string.Empty;

    public int? MandantOrigen { get; private set; }
    public string? NombreComercialOrigen { get; private set; }
    public string? DomicilioOrigenCalle { get; private set; }
    public string? DomicilioOrigenCiudad { get; private set; }
    public string? DomicilioOrigenCp { get; private set; }
    public string? DomicilioOrigenProvincia { get; private set; }
    public string? DomicilioOrigenPais { get; private set; }
    public string? CandidatoFiscalUstId { get; private set; }
    public string? CandidatoFiscalSteuernummer { get; private set; }
    public string? Telefono2Origen { get; private set; }
    public string? CondicionCodigoOrigen { get; private set; }
    public int? CondicionNumeroOrigen { get; private set; }
    public int? DiasNominalesOrigen { get; private set; }
    public string? MonedaCodigoOrigen { get; private set; }
    public string? MonedaNormalizada { get; private set; }
    public decimal? CreditoReferenciaLimite { get; private set; }
    public decimal? CreditoReferenciaLimite1 { get; private set; }
    public double? CreditoReferenciaNet { get; private set; }
    public int? EstadoOrigenCrudo { get; private set; }
    public int? BloqueoOrigenCrudo { get; private set; }
    public DateOnly? FechaOrigen { get; private set; }
    public DateTime? TransaccionOrigenUtc { get; private set; }

    // Control
    public Guid? EjecucionId { get; private set; }
    public DateTime LeidoEnUtc { get; private set; }
    public DateTime AplicadoEnUtc { get; private set; }
    public string HashOrigen { get; private set; } = string.Empty;
    public string VersionContrato { get; private set; } = string.Empty;
    public string VersionMapeo { get; private set; } = string.Empty;
    public ResultadoSincronizacionAw Resultado { get; private set; }
    public string? Error { get; private set; }

    private ClienteSincronizacionAw() { }

    public ClienteSincronizacionAw(Guid id, Guid clienteId, string referenciaExterna) : base(id)
    {
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("CLIENTE_SYNC_CLIENTE_INVALIDO", "El cliente es requerido.");
        if (string.IsNullOrWhiteSpace(referenciaExterna) || referenciaExterna.Length > 50)
            throw new BusinessRuleException("CLIENTE_SYNC_REFERENCIA_INVALIDA",
                "La referencia externa es requerida y no puede exceder 50 caracteres.");
        ClienteId = clienteId;
        ReferenciaExterna = referenciaExterna;
    }

    /// <summary>
    /// Sobrescribe el registro con la lectura actual (valores crudos + control).
    /// No decide la política sobre <see cref="Cliente"/>; eso lo hace el servicio.
    /// </summary>
    public void Aplicar(
        int? mandantOrigen, string? nombreComercialOrigen,
        string? domicilioCalle, string? domicilioCiudad, string? domicilioCp,
        string? domicilioProvincia, string? domicilioPais,
        string? candidatoFiscalUstId, string? candidatoFiscalSteuernummer,
        string? telefono2Origen,
        string? condicionCodigoOrigen, int? condicionNumeroOrigen, int? diasNominalesOrigen,
        string? monedaCodigoOrigen, string? monedaNormalizada,
        decimal? creditoLimite, decimal? creditoLimite1, double? creditoNet,
        int? estadoOrigenCrudo, int? bloqueoOrigenCrudo,
        DateOnly? fechaOrigen, DateTime? transaccionOrigenUtc,
        Guid? ejecucionId, DateTime leidoEnUtc, DateTime aplicadoEnUtc,
        string hashOrigen, string versionContrato, string versionMapeo,
        ResultadoSincronizacionAw resultado, string? error = null)
    {
        Max(nombreComercialOrigen, 400, nameof(NombreComercialOrigen));
        Max(domicilioCalle, 200, nameof(DomicilioOrigenCalle));
        Max(domicilioCiudad, 100, nameof(DomicilioOrigenCiudad));
        Max(domicilioCp, 20, nameof(DomicilioOrigenCp));
        Max(domicilioProvincia, 100, nameof(DomicilioOrigenProvincia));
        Max(domicilioPais, 100, nameof(DomicilioOrigenPais));
        Max(candidatoFiscalUstId, 40, nameof(CandidatoFiscalUstId));
        Max(candidatoFiscalSteuernummer, 40, nameof(CandidatoFiscalSteuernummer));
        Max(telefono2Origen, 50, nameof(Telefono2Origen));
        Max(condicionCodigoOrigen, 50, nameof(CondicionCodigoOrigen));
        Max(monedaCodigoOrigen, 20, nameof(MonedaCodigoOrigen));
        Max(monedaNormalizada, 3, nameof(MonedaNormalizada));
        Max(error, 1000, nameof(Error));
        if (string.IsNullOrWhiteSpace(hashOrigen) || hashOrigen.Length > 64)
            throw new BusinessRuleException("CLIENTE_SYNC_HASH_INVALIDO",
                "El hash de origen es requerido y no puede exceder 64 caracteres.");
        if (string.IsNullOrWhiteSpace(versionContrato) || versionContrato.Length > 20
            || string.IsNullOrWhiteSpace(versionMapeo) || versionMapeo.Length > 20)
            throw new BusinessRuleException("CLIENTE_SYNC_VERSION_INVALIDA",
                "Las versiones de contrato y mapeo son requeridas (máx. 20 caracteres).");

        MandantOrigen = mandantOrigen;
        NombreComercialOrigen = nombreComercialOrigen;
        DomicilioOrigenCalle = domicilioCalle;
        DomicilioOrigenCiudad = domicilioCiudad;
        DomicilioOrigenCp = domicilioCp;
        DomicilioOrigenProvincia = domicilioProvincia;
        DomicilioOrigenPais = domicilioPais;
        CandidatoFiscalUstId = candidatoFiscalUstId;
        CandidatoFiscalSteuernummer = candidatoFiscalSteuernummer;
        Telefono2Origen = telefono2Origen;
        CondicionCodigoOrigen = condicionCodigoOrigen;
        CondicionNumeroOrigen = condicionNumeroOrigen;
        DiasNominalesOrigen = diasNominalesOrigen;
        MonedaCodigoOrigen = monedaCodigoOrigen;
        MonedaNormalizada = monedaNormalizada;
        CreditoReferenciaLimite = creditoLimite;
        CreditoReferenciaLimite1 = creditoLimite1;
        CreditoReferenciaNet = creditoNet;
        EstadoOrigenCrudo = estadoOrigenCrudo;
        BloqueoOrigenCrudo = bloqueoOrigenCrudo;
        FechaOrigen = fechaOrigen;
        TransaccionOrigenUtc = transaccionOrigenUtc;
        EjecucionId = ejecucionId;
        LeidoEnUtc = leidoEnUtc;
        AplicadoEnUtc = aplicadoEnUtc;
        HashOrigen = hashOrigen;
        VersionContrato = versionContrato;
        VersionMapeo = versionMapeo;
        Resultado = resultado;
        Error = error;
    }

    /// <summary>Lectura sin cambios: solo refresca la marca de lectura; conserva el resultado previo.</summary>
    public void RegistrarComprobacion(DateTime leidoEnUtc, Guid? ejecucionId)
    {
        LeidoEnUtc = leidoEnUtc;
        EjecucionId = ejecucionId;
    }

    private static void Max(string? valor, int max, string campo)
    {
        if (valor is not null && valor.Length > max)
            throw new BusinessRuleException("CLIENTE_SYNC_CAMPO_INVALIDO",
                $"{campo} no puede exceder {max} caracteres.");
    }
}
