using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Estado de cuenta periódico de una <see cref="Tarjeta"/> (§3.1, §4.1
/// anexo TC, F7-PR5). Agrupa N movimientos + las líneas crudas del
/// archivo del banco. Su ciclo:
///
/// <list type="number">
///   <item><c>EnConciliacion</c> — archivo subido, líneas parseadas;
///   algoritmo de match auto-pre-llenó sugerencias/matches.</item>
///   <item><c>Conciliado</c> — todas las líneas atadas o explicadas
///   (diferencia 0). F7-PR6.</item>
///   <item><c>Cerrado</c> — titular aprobó; genera FacturaProveedor
///   contra el banco. F7-PR6.</item>
///   <item><c>PagadoBanco</c> — Tesorería pagó al banco. F9-PR1.</item>
/// </list>
///
/// <para>
/// <b>F7-PR5 alcance</b>: factory <see cref="Crear"/>, subir archivo
/// (<see cref="RegistrarArchivoBanco"/>), agregar líneas parseadas,
/// recibir resultados del algoritmo de conciliación. Cierre + factura
/// agregada quedan en F7-PR6.
/// </para>
/// </summary>
public sealed class EstadoCuentaTc : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public Guid TarjetaId { get; private set; }
    public DateOnly PeriodoDesde { get; private set; }
    public DateOnly PeriodoHasta { get; private set; }
    public DateOnly FechaCorte { get; private set; }
    public DateOnly FechaLimitePago { get; private set; }

    public string? ArchivoBancoBlobRef { get; private set; }
    public string? ArchivoBancoHash { get; private set; }    // SHA-256 hex
    public DateTimeOffset? ArchivoBancoCargadoAt { get; private set; }
    public Guid? ArchivoBancoCargadoBy { get; private set; }

    public string? PerfilParserUsado { get; private set; }

    public decimal? TotalBancoMxn { get; private set; }
    public decimal? TotalConciliadoMxn { get; private set; }

    /// <summary>Diferencia = TotalBancoMxn - TotalConciliadoMxn. 0 al cerrar.</summary>
    public decimal? DiferenciaMxn { get; private set; }

    public EstadoCuentaTcStatus Estado { get; private set; }

    /// <summary>FK a FacturaProveedor generada al cerrar (F7-PR6, null en F7-PR5).</summary>
    public Guid? FacturaProveedorId { get; private set; }

    /// <summary>Diferencia cambiaria entre captura y corte del banco (§8.1, D9).</summary>
    public decimal? DiferenciaCambiariaMxn { get; private set; }

    private readonly List<LineaBancoTc> _lineas = [];
    public IReadOnlyCollection<LineaBancoTc> Lineas => _lineas.AsReadOnly();

    private EstadoCuentaTc() { }

    public static EstadoCuentaTc Crear(
        Guid empresaId,
        Guid tarjetaId,
        DateOnly periodoDesde,
        DateOnly periodoHasta,
        DateOnly fechaCorte,
        DateOnly fechaLimitePago)
    {
        if (tarjetaId == Guid.Empty)
            throw new BusinessRuleException("EC_TARJETA_VACIA",
                "La tarjeta es obligatoria.");
        if (periodoHasta < periodoDesde)
            throw new BusinessRuleException("EC_PERIODO_INVALIDO",
                "El período fin no puede ser anterior al inicio.");
        if (fechaLimitePago < fechaCorte)
            throw new BusinessRuleException("EC_FECHA_LIMITE_INVALIDA",
                "La fecha límite de pago no puede ser anterior a la fecha de corte.");

        return new EstadoCuentaTc
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            TarjetaId = tarjetaId,
            PeriodoDesde = periodoDesde,
            PeriodoHasta = periodoHasta,
            FechaCorte = fechaCorte,
            FechaLimitePago = fechaLimitePago,
            Estado = EstadoCuentaTcStatus.EnConciliacion,
        };
    }

    /// <summary>
    /// Registra el archivo del banco recién subido + parseado. Solo
    /// permite mientras <see cref="EstadoCuentaTcStatus.EnConciliacion"/>.
    /// La idempotencia por SHA-256 la valida el handler antes de llamar.
    /// </summary>
    public void RegistrarArchivoBanco(
        string blobRef,
        string sha256Hex,
        Guid cargadoBy,
        string perfilParserUsado,
        decimal? totalDeclaradoMxn,
        DateTimeOffset ahora)
    {
        if (Estado != EstadoCuentaTcStatus.EnConciliacion)
            throw new BusinessRuleException("EC_NO_EDITABLE",
                $"Solo se sube archivo en estado EnConciliacion (actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(blobRef))
            throw new BusinessRuleException("EC_BLOB_VACIO",
                "La referencia al blob del archivo es obligatoria.");
        if (string.IsNullOrWhiteSpace(sha256Hex) || sha256Hex.Length != 64)
            throw new BusinessRuleException("EC_HASH_INVALIDO",
                "El SHA-256 del archivo debe ser 64 caracteres hex.");
        if (string.IsNullOrWhiteSpace(perfilParserUsado))
            throw new BusinessRuleException("EC_PERFIL_VACIO",
                "El código del perfil usado es obligatorio.");

        ArchivoBancoBlobRef = blobRef;
        ArchivoBancoHash = sha256Hex.ToLowerInvariant();
        ArchivoBancoCargadoAt = ahora;
        ArchivoBancoCargadoBy = cargadoBy;
        PerfilParserUsado = perfilParserUsado.Trim().ToUpperInvariant();
        TotalBancoMxn = totalDeclaradoMxn;
    }

    /// <summary>
    /// Agrega una línea parseada del archivo del banco. Solo en
    /// <see cref="EstadoCuentaTcStatus.EnConciliacion"/>.
    /// </summary>
    public LineaBancoTc AgregarLinea(
        int posicionArchivo,
        DateOnly fechaAplicacion,
        decimal monto,
        string moneda,
        decimal montoMxn,
        string merchantRaw,
        string? referenciaBanco,
        string? tipoSegunBanco)
    {
        if (Estado != EstadoCuentaTcStatus.EnConciliacion)
            throw new BusinessRuleException("EC_NO_EDITABLE",
                $"Solo se agregan líneas en EnConciliacion (actual: {Estado}).");

        var linea = new LineaBancoTc(
            id: Guid.CreateVersion7(),
            empresaId: EmpresaId,
            estadoCuentaTcId: Id,
            posicionArchivo: posicionArchivo,
            fechaAplicacion: fechaAplicacion,
            monto: monto,
            moneda: moneda,
            montoMxn: montoMxn,
            merchantRaw: merchantRaw,
            referenciaBanco: referenciaBanco,
            tipoSegunBanco: tipoSegunBanco);

        _lineas.Add(linea);
        return linea;
    }

    /// <summary>
    /// Limpia las líneas para recargar un archivo corregido (TC-B4 de la
    /// verificación e2e P7: la recarga tronaba con 23505 sobre
    /// <c>ux_linea_archivo_posicion</c>). Solo permitido en
    /// <c>EnConciliacion</c> y cuando ninguna línea tiene match — si ya
    /// hubo conciliación hay que deshacerla operativamente, no pisarla.
    /// </summary>
    public IReadOnlyList<LineaBancoTc> LimpiarLineasParaRecarga()
    {
        if (Estado != EstadoCuentaTcStatus.EnConciliacion)
            throw new BusinessRuleException(
                "EC_RECARGA_NO_PERMITIDA",
                $"Solo se recarga el archivo en EnConciliacion (actual: {Estado}).");
        if (_lineas.Any(l => l.EstadoMatch == EstadoMatchLineaBanco.Matched))
            throw new BusinessRuleException(
                "EC_RECARGA_CON_MATCHES",
                "Hay líneas ya conciliadas con movimientos — no se puede recargar el archivo sin deshacer la conciliación.");

        var removidas = _lineas.ToList();
        _lineas.Clear();
        TotalConciliadoMxn = null;
        return removidas;
    }

    /// <summary>
    /// Actualiza el total conciliado (suma de líneas con match o
    /// explicación) — llamado por el handler de conciliación al
    /// terminar el algoritmo.
    /// </summary>
    public void ActualizarTotalConciliado(decimal totalConciliadoMxn)
    {
        TotalConciliadoMxn = totalConciliadoMxn;
        if (TotalBancoMxn is decimal tb)
            DiferenciaMxn = tb - totalConciliadoMxn;
    }

    /// <summary>
    /// Transición <see cref="EstadoCuentaTcStatus.EnConciliacion"/> →
    /// <see cref="EstadoCuentaTcStatus.Conciliado"/> (F7-PR6 §5.3 paso 8).
    /// Requiere que todas las líneas estén Matched o NoConciliado
    /// (clasificadas explícitamente como gasto financiero / refund /
    /// anualidad / comisión / disputa) y que <see cref="DiferenciaMxn"/>
    /// sea cero ± $0.01.
    /// </summary>
    public void MarcarConciliado(decimal toleranciaDiferencia = 0.01m)
    {
        if (Estado != EstadoCuentaTcStatus.EnConciliacion)
            throw new BusinessRuleException(
                "EC_NO_CONCILIABLE",
                $"Solo se concilia desde EnConciliacion (actual: {Estado}).");

        // Sin total declarado no hay diferencia que evaluar: el archivo
        // del banco debe traer la fila TOTAL/SALDO (sin fecha) que el
        // parser usa para capturar TotalDeclaradoMxn — mensaje accionable
        // detectado en la verificación e2e P7 (TC-B3).
        if (DiferenciaMxn is null)
        {
            throw new BusinessRuleException(
                "EC_SIN_TOTAL_DECLARADO",
                "El archivo del banco no trajo el total declarado (fila TOTAL/SALDO sin fecha). " +
                "Recarga el archivo con esa fila para poder conciliar.");
        }

        if (DiferenciaMxn is not decimal diff || Math.Abs(diff) > toleranciaDiferencia)
        {
            throw new BusinessRuleException(
                "EC_DIFERENCIA_NO_CERO",
                $"La diferencia {DiferenciaMxn?.ToString("0.00") ?? "no calculada"} excede la tolerancia (${toleranciaDiferencia:0.00}).");
        }

        var pendientes = _lineas.Count(l => l.EstadoMatch == EstadoMatchLineaBanco.Pendiente);
        if (pendientes > 0)
        {
            throw new BusinessRuleException(
                "EC_LINEAS_PENDIENTES",
                $"Quedan {pendientes} líneas en estado Pendiente — confirma o clasifica antes de conciliar.");
        }

        Estado = EstadoCuentaTcStatus.Conciliado;
    }

    /// <summary>
    /// Transición <see cref="EstadoCuentaTcStatus.Conciliado"/> →
    /// <see cref="EstadoCuentaTcStatus.Cerrado"/> (F7-PR6 §5.4). El
    /// handler debe haber creado la <c>FacturaProveedor</c> agregada
    /// contra el banco y pasar su Id; se persiste la FK para trazar.
    /// </summary>
    public void Cerrar(Guid facturaProveedorId)
    {
        if (Estado != EstadoCuentaTcStatus.Conciliado)
            throw new BusinessRuleException(
                "EC_NO_CERRABLE",
                $"Solo se cierra desde Conciliado (actual: {Estado}).");
        if (facturaProveedorId == Guid.Empty)
            throw new BusinessRuleException(
                "EC_FACTURA_BANCO_VACIA",
                "Se requiere FacturaProveedorId al cerrar.");

        Estado = EstadoCuentaTcStatus.Cerrado;
        FacturaProveedorId = facturaProveedorId;
    }

    /// <summary>
    /// Transición <see cref="EstadoCuentaTcStatus.Cerrado"/> →
    /// <see cref="EstadoCuentaTcStatus.PagadoBanco"/> (F7-PR6 §5.4
    /// paso 5). Llamado al recibir <c>PagoFacturaProveedorEvent</c>
    /// de Tesorería cuando la factura asociada es del banco.
    /// </summary>
    public void MarcarPagadoBanco()
    {
        if (Estado != EstadoCuentaTcStatus.Cerrado)
            throw new BusinessRuleException(
                "EC_NO_PAGABLE_BANCO",
                $"Solo se marca PagadoBanco desde Cerrado (actual: {Estado}).");

        Estado = EstadoCuentaTcStatus.PagadoBanco;
    }

    /// <summary>
    /// Registra la diferencia cambiaria detectada al conciliar (§8.1
    /// del anexo, D9). Llamado por el handler de conciliación
    /// manual cuando confirma un match en moneda extranjera donde el
    /// banco aplicó TC distinto al de la captura.
    /// </summary>
    public void RegistrarDiferenciaCambiaria(decimal diferenciaMxn)
    {
        DiferenciaCambiariaMxn = (DiferenciaCambiariaMxn ?? 0m) + diferenciaMxn;
    }
}
