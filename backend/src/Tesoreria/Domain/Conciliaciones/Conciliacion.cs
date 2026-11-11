using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Conciliaciones;

/// <summary>
/// Agregado raíz de conciliación bancaria mensual por cuenta (§4.1 / §5 DDL).
/// Invariante central (RN-7): solo cierra con diferencia 0 (saldo cuadrado) y acta firmada
/// por quien tiene la facultad de cerrar conciliación.
/// </summary>
public sealed class Conciliacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid CuentaBancariaId { get; private set; }
    public int PeriodoAnio { get; private set; }
    public int PeriodoMes { get; private set; }
    public decimal SaldoExtracto { get; private set; }
    public decimal SaldoLibros { get; private set; }
    public EstadoConciliacion Estado { get; private set; } = EstadoConciliacion.Abierta;

    public Guid? CerradaPor { get; private set; }
    public DateTimeOffset? CerradaEn { get; private set; }

    public string? MotivoReapertura { get; private set; }
    public Guid? ReabiertaPor { get; private set; }
    public DateTimeOffset? ReabiertaEn { get; private set; }

    /// <summary>M-202: visto bueno de Contabilidad; distinto de quien firmó.</summary>
    public Guid? VistoBuenoPor { get; private set; }
    public DateTimeOffset? VistoBuenoEn { get; private set; }

    public string? FirmanteNombre { get; private set; }
    public string? FirmantePuesto { get; private set; }
    public DateTimeOffset? ActaGeneradaEn { get; private set; }

    private readonly List<ExtractoLinea> _lineas = new();
    public IReadOnlyCollection<ExtractoLinea> Lineas => _lineas.AsReadOnly();

    private readonly List<BitacoraConciliacion> _bitacora = new();
    public IReadOnlyCollection<BitacoraConciliacion> Bitacora => _bitacora.AsReadOnly();

    public decimal Diferencia => SaldoExtracto - SaldoLibros;

    private Conciliacion() { }

    public static Conciliacion Crear(
        Guid empresaId,
        Guid cuentaBancariaId,
        int periodoAnio,
        int periodoMes,
        decimal saldoExtracto = 0m)
    {
        if (cuentaBancariaId == Guid.Empty)
            throw new BusinessRuleException("CONCILIACION_CUENTA_VACIA", "La cuenta bancaria es obligatoria.");
        if (periodoMes < 1 || periodoMes > 12)
            throw new BusinessRuleException("CONCILIACION_MES_INVALIDO", "El mes de la conciliación debe estar entre 1 y 12.");
        if (periodoAnio < 2000 || periodoAnio > 2100)
            throw new BusinessRuleException("CONCILIACION_ANIO_INVALIDO", "El año de la conciliación no es válido.");

        return new Conciliacion
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            CuentaBancariaId = cuentaBancariaId,
            PeriodoAnio = periodoAnio,
            PeriodoMes = periodoMes,
            SaldoExtracto = saldoExtracto,
            SaldoLibros = 0m,
            Estado = EstadoConciliacion.Abierta,
        };
    }

    /// <summary>
    /// Importa líneas a la conciliación. Deduplica automáticamente por LineaHash si ya existían (CA9.5).
    /// Retorna el número de líneas efectivamente agregadas.
    /// </summary>
    public int CargarLineas(IEnumerable<ExtractoLinea> nuevasLineas, decimal? nuevoSaldoExtracto = null)
    {
        if (Estado != EstadoConciliacion.Abierta)
            throw new BusinessRuleException("CONCILIACION_CERRADA", "No se pueden importar líneas en una conciliación cerrada o pendiente de visto bueno.");

        if (nuevoSaldoExtracto.HasValue)
            SaldoExtracto = nuevoSaldoExtracto.Value;

        var hashesExistentes = new HashSet<string>(_lineas.Where(l => l.LineaHash != null).Select(l => l.LineaHash!));
        int agregadas = 0;

        foreach (var linea in nuevasLineas)
        {
            if (linea.LineaHash != null && hashesExistentes.Contains(linea.LineaHash))
            {
                // CA9.5: La línea ya fue importada previamente. Se omite para no duplicar.
                continue;
            }

            _lineas.Add(linea);
            if (linea.LineaHash != null)
                hashesExistentes.Add(linea.LineaHash);

            agregadas++;
        }

        return agregadas;
    }

    public void ActualizarSaldoExtracto(decimal saldoExtracto)
    {
        if (Estado != EstadoConciliacion.Abierta)
            throw new BusinessRuleException("CONCILIACION_CERRADA", "No se puede editar el saldo de una conciliación cerrada o pendiente de visto bueno.");
        SaldoExtracto = saldoExtracto;
    }

    public void ActualizarSaldoLibros(decimal saldoLibros)
    {
        SaldoLibros = saldoLibros;
    }

    /// <summary>
    /// Cierre de la conciliación (RN-7). Exige saldo cuadrado (Diferencia == 0),
    /// sin líneas pendientes ni en revisión, y firma de quien tiene la facultad de cerrar conciliación.
    /// </summary>
    public void Cerrar(
        Guid usuarioId,
        string firmanteNombre,
        string firmantePuesto,
        decimal saldoLibrosCalculado,
        DateTimeOffset ahora)
    {
        if (Estado != EstadoConciliacion.Abierta)
            throw new BusinessRuleException("CONCILIACION_YA_CERRADA", "La conciliación ya está firmada o cerrada.");

        if (string.IsNullOrWhiteSpace(firmanteNombre))
            throw new BusinessRuleException("CONCILIACION_FIRMANTE_OBLIGATORIO", "El nombre del firmante es obligatorio para emitir el acta.");

        // La facultad «Cerrar conciliación» (tesoreria.conciliacion.cerrar) la exige el endpoint;
        // el puesto es solo el texto del acta, ya no autoriza por contener «Gerente» o «Jefe».
        if (string.IsNullOrWhiteSpace(firmantePuesto))
            throw new BusinessRuleException("CONCILIACION_FIRMANTE_OBLIGATORIO", "El puesto del firmante es obligatorio para emitir el acta.");

        var pendientes = _lineas.Count(l => l.Estado == EstadoExtractoLinea.Pendiente || l.Estado == EstadoExtractoLinea.EnRevision);
        if (pendientes > 0)
        {
            throw new BusinessRuleException("CONCILIACION_LINEAS_PENDIENTES",
                $"Existen {pendientes} líneas pendientes o en revisión. Todas las líneas deben estar emparejadas o marcadas en tránsito para cerrar.");
        }

        SaldoLibros = saldoLibrosCalculado;
        decimal diff = SaldoExtracto - SaldoLibros;
        if (Math.Abs(diff) > 0.001m)
        {
            throw new BusinessRuleException("CONCILIACION_DESCUADRADA",
                $"No se puede cerrar la conciliación con una diferencia de ${diff:N2}. El saldo debe ser exactamente $0.00.");
        }

        // M-202: la firma de Tesorería no cierra; queda en espera del visto bueno de Contabilidad.
        Estado = EstadoConciliacion.PendienteVistoBueno;
        CerradaPor = usuarioId;
        CerradaEn = ahora;
        FirmanteNombre = firmanteNombre.Trim();
        FirmantePuesto = firmantePuesto.Trim();
        ActaGeneradaEn = ahora;
    }

    /// <summary>M-202: Contabilidad da el visto bueno; quien firmó no puede darlo.</summary>
    public void DarVistoBueno(Guid usuarioId, DateTimeOffset ahora)
    {
        if (Estado != EstadoConciliacion.PendienteVistoBueno)
            throw new BusinessRuleException("CONCILIACION_NO_PENDIENTE", "Solo una conciliación firmada por Tesorería espera visto bueno.");
        if (usuarioId == Guid.Empty || usuarioId == CerradaPor)
            throw new BusinessRuleException("CONCILIACION_VISTO_BUENO_MISMO_USUARIO", "Quien firmó no puede dar el visto bueno.");
        Estado = EstadoConciliacion.Cerrada;
        VistoBuenoPor = usuarioId;
        VistoBuenoEn = ahora;
        _bitacora.Add(new BitacoraConciliacion(Id, null, null, "Visto bueno de Contabilidad", "Conciliación cerrada", usuarioId, ahora));
    }

    /// <summary>M-202: Contabilidad devuelve a Tesorería con motivo; regresa a Abierta.</summary>
    public void DevolverATesoreria(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        if (Estado != EstadoConciliacion.PendienteVistoBueno)
            throw new BusinessRuleException("CONCILIACION_NO_PENDIENTE", "Solo se devuelve una conciliación pendiente de visto bueno.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CONCILIACION_MOTIVO_DEVOLUCION_OBLIGATORIO", "El motivo de la devolución es obligatorio.");
        Estado = EstadoConciliacion.Abierta;
        CerradaPor = null; CerradaEn = null; ActaGeneradaEn = null;
        _bitacora.Add(new BitacoraConciliacion(Id, null, null, "Devuelta a Tesorería", motivo.Trim(), usuarioId, ahora));
    }

    /// <summary>
    /// Reapertura de conciliación cerrada (T9.2). Exige motivo obligatorio y bitácora.
    /// </summary>
    public void Reabrir(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        if (Estado != EstadoConciliacion.Cerrada)
            throw new BusinessRuleException("CONCILIACION_NO_CERRADA", "Solo una conciliación cerrada puede reabrirse.");

        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CONCILIACION_MOTIVO_REAPERTURA_OBLIGATORIO", "El motivo de reapertura es obligatorio.");

        Estado = EstadoConciliacion.Abierta;
        VistoBuenoPor = null; VistoBuenoEn = null;
        MotivoReapertura = motivo.Trim();
        ReabiertaPor = usuarioId;
        ReabiertaEn = ahora;

        _bitacora.Add(new BitacoraConciliacion(
            Id,
            null,
            null,
            "Reapertura de conciliación",
            motivo.Trim(),
            usuarioId,
            ahora));
    }

    public void RegistrarEnBitacora(Guid? lineaId, Guid? movId, string accion, string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        _bitacora.Add(new BitacoraConciliacion(Id, lineaId, movId, accion, motivo, usuarioId, ahora));
    }
}
