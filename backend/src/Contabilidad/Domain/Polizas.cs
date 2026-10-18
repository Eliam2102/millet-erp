using System.Text.Json.Serialization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>
/// Tipo de póliza del SAT (Anexo 24, contabilidad electrónica): cada tipo de operación (K10.9) declara el suyo.
/// Los valores son los del XML del SAT; fijo por ABI.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoPolizaSat : short { Ingreso = 1, Egreso = 2, Diario = 3 }

/// <summary>Origen de la póliza (C1.3): la manual la captura un usuario; la automática la genera el motor (C1.4) y nace autorizada.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrigenPoliza : short { Manual = 0, Automatica = 1 }

/// <summary>
/// Estado de la póliza (C1.3): <c>Borrador → Autorizada → Anulada</c>. La autorizada es inmutable; un error se corrige anulándola
/// (K10.3). Fijo por ABI: agregar al final.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoPoliza : short { Borrador = 0, Autorizada = 1, Anulada = 2 }

/// <summary>
/// Relación de una póliza con otra (K10.4): la reversa automática de una recurrente y la reclasificación ligada al origen. La
/// anulación (K10.3) conserva su propia liga (<see cref="Poliza.PolizaAnuladaId"/>). Fijo por ABI: agregar al final.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RelacionPoliza : short { Reversa = 1, Reclasificacion = 2 }

/// <summary>Documento del módulo que originó la póliza (C1.3): módulo, tipo, id, folio y UUID del CFDI cuando aplica.</summary>
public sealed record DocumentoOrigenPoliza(string Modulo, string Tipo, Guid Id, string? Folio, string? Uuid);

/// <summary>
/// Partida capturada, antes de ser parte de una póliza. Cargo y abono en MXN; si la moneda no es MXN, <c>ImporteOriginal</c> y
/// <c>TipoCambio</c> explican el importe en pesos. Las dimensiones son las de K10.2 (Dim1–Dim3, proyecto y auxiliares).
/// </summary>
public sealed record DatosPartida(
    Guid CuentaId, string CuentaCodigo, string? Concepto, decimal Cargo, decimal Abono, string Moneda, decimal TipoCambio,
    decimal? ImporteOriginal, Guid SucursalId, Guid? Dim1Id, Guid? Dim2Id, Guid? Dim3Id, string? Proyecto,
    Guid? ClienteId, Guid? ProveedorId, Guid? CuentaBancariaId);

/// <summary>
/// Póliza contable (C1.3). Se identifica por su tipo de operación de dos letras y un consecutivo de 6 dígitos por tipo y año
/// (K10.9): <c>TT-000123</c>. El número se asigna al autorizar (la manual) o al nacer (la automática, que nace autorizada), dentro
/// de la misma transacción que la guarda: así no quedan huecos ni duplicados. La autorizada no se edita; si tiene un error se
/// anula (K10.3): nace otra con las mismas partidas en negativo, mismo lado, con el concepto «Anulación de TT-000123».
/// </summary>
public sealed class Poliza : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int ConceptoMaximo = 500;
    public const int MotivoMinimo = 10, MotivoMaximo = 500;
    public const int NumeroMaximo = 999_999;
    public const string MonedaNacional = "MXN";

    private readonly List<PolizaPartida> _partidas = [];

    public Guid EmpresaId { get; set; }
    public Guid TipoOperacionId { get; private set; }
    /// <summary>Clave del tipo al momento de crear la póliza (snapshot: la clave del catálogo es inmutable).</summary>
    public string TipoClave { get; private set; } = string.Empty;
    public TipoPolizaSat TipoSat { get; private set; }
    /// <summary>Consecutivo del tipo en el año del periodo; null mientras es borrador.</summary>
    public int? Numero { get; private set; }
    public DateOnly Fecha { get; private set; }
    public int PeriodoAnio { get; private set; }
    public int PeriodoNumero { get; private set; }
    public string Concepto { get; private set; } = string.Empty;
    public OrigenPoliza Origen { get; private set; }
    public EstadoPoliza Estado { get; private set; }

    public string? OrigenModulo { get; private set; }
    public string? OrigenTipo { get; private set; }
    public Guid? OrigenDocumentoId { get; private set; }
    public string? OrigenFolio { get; private set; }
    public string? OrigenUuid { get; private set; }

    /// <summary>Llave de idempotencia de la automática (<c>eventType:eventId</c>, plano C1 §3): una sola póliza por evento.</summary>
    public string? LlaveIdempotencia { get; private set; }

    /// <summary>En la póliza de anulación: la póliza que anula (K10.3).</summary>
    public Guid? PolizaAnuladaId { get; private set; }
    /// <summary>En la póliza anulada: la póliza que la anuló.</summary>
    public Guid? AnuladaPorPolizaId { get; private set; }
    public string? MotivoAnulacion { get; private set; }
    public string? AnuladaPor { get; private set; }
    public DateTimeOffset? AnuladaEn { get; private set; }

    /// <summary>K10.4: póliza con la que se relaciona esta (la revertida o la reclasificada) y el tipo de relación.</summary>
    public Guid? PolizaRelacionadaId { get; private set; }
    public RelacionPoliza? Relacion { get; private set; }
    /// <summary>K10.4: plantilla recurrente que generó la póliza y el periodo de la recurrencia.</summary>
    public Guid? PlantillaRecurrenteId { get; private set; }

    public Guid? CapturadaPorId { get; private set; }
    public string CapturadaPor { get; private set; } = string.Empty;
    public Guid? AutorizadaPorId { get; private set; }
    public string? AutorizadaPor { get; private set; }
    public DateTimeOffset? AutorizadaEn { get; private set; }

    public decimal TotalCargos { get; private set; }
    public decimal TotalAbonos { get; private set; }

    public IReadOnlyList<PolizaPartida> Partidas => _partidas;

    public bool EsAnulacion => PolizaAnuladaId is not null;
    public bool Cuadra => TotalCargos == TotalAbonos && TotalCargos != 0m;
    public string? Folio => Numero is { } n ? FormatearFolio(TipoClave, n) : null;

    private Poliza() { }

    private Poliza(Guid id, TipoDocumentoContable tipo, DateOnly fecha, int periodoAnio, int periodoNumero, string concepto,
        OrigenPoliza origen, DocumentoOrigenPoliza? documento, Guid? usuarioId, string usuarioNombre) : base(id)
    {
        TipoOperacionId = tipo.Id;
        TipoClave = tipo.Clave;
        TipoSat = tipo.TipoSat;
        Origen = origen;
        Estado = EstadoPoliza.Borrador;
        CapturadaPorId = usuarioId;
        CapturadaPor = usuarioNombre;
        if (documento is not null)
        {
            OrigenModulo = Recortar(documento.Modulo, 40, "módulo");
            OrigenTipo = Recortar(documento.Tipo, 40, "tipo de documento");
            OrigenDocumentoId = documento.Id;
            OrigenFolio = documento.Folio is null ? null : Recortar(documento.Folio, 60, "folio");
            OrigenUuid = documento.Uuid is null ? null : Recortar(documento.Uuid, 40, "UUID");
        }
        Encabezado(fecha, periodoAnio, periodoNumero, concepto);
    }

    /// <summary>Un tipo inactivo no admite pólizas nuevas; la anulación de una existente sí procede.</summary>
    private static void ExigirTipoActivo(TipoDocumentoContable tipo)
    {
        if (!tipo.Activo)
            throw new BusinessRuleException("CONTAB_POLIZA_TIPO_INACTIVO", $"El tipo de operación {tipo.Clave} está inactivo.");
    }

    public static string FormatearFolio(string tipoClave, int numero) => $"{tipoClave}-{numero:D6}";

    /// <summary>Póliza manual en borrador (C1.3). Puede no cuadrar todavía; solo se autoriza cuadrada.</summary>
    public static Poliza CrearManual(Guid id, TipoDocumentoContable tipo, DateOnly fecha, int periodoAnio, int periodoNumero, string concepto,
        DocumentoOrigenPoliza? documento, IEnumerable<DatosPartida> partidas, Guid? usuarioId, string usuarioNombre)
    {
        ExigirTipoActivo(tipo);
        var p = new Poliza(id, tipo, fecha, periodoAnio, periodoNumero, concepto, OrigenPoliza.Manual, documento, usuarioId, usuarioNombre);
        p.ReemplazarPartidas(partidas, admiteNegativos: false);
        return p;
    }

    /// <summary>
    /// Póliza automática (motor, C1.4): nace autorizada con el número ya asignado y debe cuadrar. La llave de idempotencia evita
    /// una segunda póliza por el mismo evento (plano C1 §3 y §6).
    /// </summary>
    public static Poliza CrearAutomatica(Guid id, TipoDocumentoContable tipo, DateOnly fecha, int periodoAnio, int periodoNumero, string concepto,
        DocumentoOrigenPoliza documento, string llaveIdempotencia, IEnumerable<DatosPartida> partidas, int numero, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(llaveIdempotencia) || llaveIdempotencia.Length > 200)
            throw new BusinessRuleException("CONTAB_POLIZA_LLAVE_INVALIDA", "La póliza automática requiere una llave de idempotencia de hasta 200 caracteres.");
        ExigirTipoActivo(tipo);
        var p = new Poliza(id, tipo, fecha, periodoAnio, periodoNumero, concepto, OrigenPoliza.Automatica, documento, null, "motor-contable");
        p.LlaveIdempotencia = llaveIdempotencia;
        p.ReemplazarPartidas(partidas, admiteNegativos: false);
        p.ValidarCuadre();
        p.Estado = EstadoPoliza.Autorizada;
        p.AsignarNumero(numero);
        p.AutorizadaPor = "motor-contable";
        p.AutorizadaEn = ahora;
        return p;
    }

    /// <summary>
    /// Solo el borrador se edita (C1.3-b: la autorizada solo se anula), y solo lo edita quien lo capturó: si otro usuario
    /// pudiera reescribirlo, después podría autorizarlo él mismo y se rompería «prepara el Analista, aprueba el Supervisor».
    /// </summary>
    public void Editar(DateOnly fecha, int periodoAnio, int periodoNumero, string concepto, IEnumerable<DatosPartida> partidas, Guid? editorId)
    {
        ExigirBorrador("editar");
        if (editorId is null || editorId != CapturadaPorId)
            throw new BusinessRuleException("CONTAB_POLIZA_EDITA_SOLO_QUIEN_CAPTURA",
                "Solo quien capturó el borrador puede editarlo. Si tiene un error, regréselo a quien lo capturó o descártelo y capture otro.");
        Encabezado(fecha, periodoAnio, periodoNumero, concepto);
        ReemplazarPartidas(partidas, admiteNegativos: false);
    }

    public void ExigirBorrador(string accion)
    {
        if (Estado != EstadoPoliza.Borrador)
            throw new BusinessRuleException("CONTAB_POLIZA_NO_EDITABLE", Estado == EstadoPoliza.Autorizada
                ? $"La póliza {Folio} está autorizada: no se puede {accion}; si tiene un error, anúlela."
                : $"La póliza {Folio} está anulada: no se puede {accion}.");
    }

    /// <summary>
    /// Autoriza el borrador (C1.3): debe cuadrar (C1.3-a) y no la autoriza quien la capturó (C1.3-c: prepara el Analista, aprueba el
    /// Supervisor). El número lo reserva Application en la misma transacción.
    /// </summary>
    public void Autorizar(int numero, Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        ExigirBorrador("autorizar");
        if (usuarioId is null || usuarioId == CapturadaPorId)
            throw new BusinessRuleException("CONTAB_POLIZA_AUTORIZA_QUIEN_CAPTURA",
                "No puede autorizar una póliza que usted capturó: la autoriza otro usuario con permiso (prepara el Analista, aprueba el Supervisor).");
        ValidarCuadre();
        AsignarNumero(numero);
        Estado = EstadoPoliza.Autorizada;
        AutorizadaPorId = usuarioId;
        AutorizadaPor = usuarioNombre;
        AutorizadaEn = ahora;
    }

    public void ValidarCuadre()
    {
        if (_partidas.Count < 2)
            throw new BusinessRuleException("CONTAB_POLIZA_SIN_PARTIDAS", "La póliza necesita al menos dos partidas.");
        if (!Cuadra)
            throw new BusinessRuleException("CONTAB_POLIZA_NO_CUADRA",
                $"La póliza no cuadra: cargos {TotalCargos:N2} y abonos {TotalAbonos:N2} (diferencia {TotalCargos - TotalAbonos:N2}).");
    }

    /// <summary>Verifica que la póliza se pueda anular (K10.3-b) y el motivo (obligatorio, RNF-05).</summary>
    public void ExigirAnulable(string motivo)
    {
        if (Estado == EstadoPoliza.Anulada)
            throw new ConflictException("CONTAB_POLIZA_YA_ANULADA", $"La póliza {Folio} ya está anulada.");
        if (Estado != EstadoPoliza.Autorizada)
            throw new BusinessRuleException("CONTAB_POLIZA_NO_AUTORIZADA", "Solo se anula una póliza autorizada; un borrador se descarta.");
        if (EsAnulacion)
            throw new BusinessRuleException("CONTAB_POLIZA_ANULACION_NO_ANULABLE",
                "Una póliza de anulación no se anula: si la anulación fue un error, capture de nuevo la póliza correcta.");
        ValidarMotivo(motivo);
    }

    /// <summary>
    /// Anulación (K10.3, R10 de Millet): crea otra póliza del mismo tipo con las mismas partidas en el mismo lado e importe negativo,
    /// concepto «Anulación de TT-000123», ligada a esta; esta queda <see cref="EstadoPoliza.Anulada"/>. La anulación nace autorizada
    /// por quien anula (con permiso y motivo). La suma del periodo queda como si la original no existiera.
    /// </summary>
    public Poliza Anular(Guid anulacionId, TipoDocumentoContable tipo, DateOnly fecha, int periodoAnio, int periodoNumero, int numero,
        string motivo, Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        ExigirAnulable(motivo);
        if (tipo.Id != TipoOperacionId)
            throw new BusinessRuleException("CONTAB_POLIZA_ANULACION_TIPO", "La anulación lleva el mismo tipo de operación que la póliza original.");
        var documento = OrigenDocumentoId is { } docId ? new DocumentoOrigenPoliza(OrigenModulo!, OrigenTipo!, docId, OrigenFolio, OrigenUuid) : null;
        var anulacion = new Poliza(anulacionId, tipo, fecha, periodoAnio, periodoNumero, $"Anulación de {Folio}", Origen, documento, usuarioId, usuarioNombre)
        {
            PolizaAnuladaId = Id,
            MotivoAnulacion = motivo.Trim(),
        };
        anulacion.ReemplazarPartidas(_partidas.OrderBy(x => x.Linea).Select(x => x.Negada()), admiteNegativos: true);
        anulacion.AsignarNumero(numero);
        anulacion.Estado = EstadoPoliza.Autorizada;
        anulacion.AutorizadaPorId = usuarioId;
        anulacion.AutorizadaPor = usuarioNombre;
        anulacion.AutorizadaEn = ahora;

        Estado = EstadoPoliza.Anulada;
        AnuladaPorPolizaId = anulacionId;
        MotivoAnulacion = motivo.Trim();
        AnuladaPor = usuarioNombre;
        AnuladaEn = ahora;
        return anulacion;
    }

    /// <summary>K10.4: la póliza la generó una plantilla recurrente; la llave evita una segunda póliza del mismo periodo.</summary>
    public void MarcarRecurrente(Guid plantillaId, string llave)
    {
        ExigirBorrador("marcar como recurrente");
        PlantillaRecurrenteId = plantillaId;
        LlaveIdempotencia = llave;
    }

    /// <summary>K10.4: la póliza reclasifica a otra (autorizada); queda ligada para verse en ambos sentidos.</summary>
    public void LigarReclasificacion(Poliza original)
    {
        ExigirBorrador("ligar");
        if (original.Estado != EstadoPoliza.Autorizada)
            throw new BusinessRuleException("CONTAB_POLIZA_RECLASIFICA_NO_AUTORIZADA", "Solo se reclasifica una póliza autorizada.");
        PolizaRelacionadaId = original.Id;
        Relacion = RelacionPoliza.Reclasificacion;
        if (OrigenDocumentoId is null && original.OrigenDocumentoId is not null)
        {
            OrigenModulo = original.OrigenModulo;
            OrigenTipo = original.OrigenTipo;
            OrigenDocumentoId = original.OrigenDocumentoId;
            OrigenFolio = original.OrigenFolio;
            OrigenUuid = original.OrigenUuid;
        }
    }

    /// <summary>
    /// Reversa automática de una recurrente (K10.4-b): con la misma regla que la anulación (R10: mismas partidas en negativo, mismo
    /// lado), concepto «Reversa de TT-000123», ligada a esta. A diferencia de la anulación, la original NO cambia de estado: fue válida
    /// en su periodo. Nace autorizada con el número ya reservado.
    /// </summary>
    public Poliza Revertir(Guid reversaId, TipoDocumentoContable tipo, DateOnly fecha, int periodoAnio, int periodoNumero, int numero,
        Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        if (Estado != EstadoPoliza.Autorizada)
            throw new BusinessRuleException("CONTAB_POLIZA_REVERSA_NO_AUTORIZADA", "Solo se revierte una póliza autorizada.");
        if (EsAnulacion || Relacion == RelacionPoliza.Reversa)
            throw new BusinessRuleException("CONTAB_POLIZA_REVERSA_INVALIDA", "Una anulación o una reversa no se revierten.");
        if (tipo.Id != TipoOperacionId)
            throw new BusinessRuleException("CONTAB_POLIZA_REVERSA_TIPO", "La reversa lleva el mismo tipo de operación que la póliza original.");
        var documento = OrigenDocumentoId is { } docId ? new DocumentoOrigenPoliza(OrigenModulo!, OrigenTipo!, docId, OrigenFolio, OrigenUuid) : null;
        var reversa = new Poliza(reversaId, tipo, fecha, periodoAnio, periodoNumero, $"Reversa de {Folio}", Origen, documento, usuarioId, usuarioNombre)
        {
            EmpresaId = EmpresaId,
            PolizaRelacionadaId = Id,
            Relacion = RelacionPoliza.Reversa,
            PlantillaRecurrenteId = PlantillaRecurrenteId,
            LlaveIdempotencia = $"reversa:{Id}",
        };
        reversa.ReemplazarPartidas(_partidas.OrderBy(x => x.Linea).Select(x => x.Negada()), admiteNegativos: true);
        reversa.AsignarNumero(numero);
        reversa.Estado = EstadoPoliza.Autorizada;
        reversa.AutorizadaPorId = usuarioId;
        reversa.AutorizadaPor = usuarioNombre;
        reversa.AutorizadaEn = ahora;
        return reversa;
    }

    public static void ValidarMotivo(string? motivo)
    {
        if (motivo is null || motivo.Trim().Length is < MotivoMinimo or > MotivoMaximo)
            throw new BusinessRuleException("CONTAB_POLIZA_MOTIVO_INVALIDO",
                $"Indique el motivo de la anulación (entre {MotivoMinimo} y {MotivoMaximo} caracteres).");
    }

    private void AsignarNumero(int numero)
    {
        if (numero is < 1 or > NumeroMaximo)
            throw new BusinessRuleException("CONTAB_POLIZA_CONSECUTIVO_AGOTADO",
                $"El consecutivo del tipo {TipoClave} en {PeriodoAnio} llegó al máximo de 6 dígitos.");
        Numero = numero;
    }

    private void Encabezado(DateOnly fecha, int periodoAnio, int periodoNumero, string concepto)
    {
        if (periodoNumero is < 1 or > PeriodoContable.NumeroAjuste)
            throw new BusinessRuleException("CONTAB_PERIODO_NUMERO_INVALIDO", "El número de periodo debe estar entre 1 y 13.");
        var esperado = periodoNumero == PeriodoContable.NumeroAjuste
            ? (Anio: fecha.Year, Numero: periodoNumero)
            : PeriodoContable.PorFecha(fecha);
        if (esperado != (periodoAnio, periodoNumero) || (periodoNumero == PeriodoContable.NumeroAjuste && fecha != new DateOnly(periodoAnio, 12, 31)))
            throw new BusinessRuleException("CONTAB_POLIZA_FECHA_FUERA_DE_PERIODO",
                $"La fecha {fecha:yyyy-MM-dd} no corresponde al periodo {PeriodoContable.Etiqueta(periodoAnio, periodoNumero)}.");
        if (string.IsNullOrWhiteSpace(concepto) || concepto.Trim().Length > ConceptoMaximo)
            throw new BusinessRuleException("CONTAB_POLIZA_CONCEPTO_INVALIDO", $"El concepto es requerido y admite hasta {ConceptoMaximo} caracteres.");
        Fecha = fecha;
        PeriodoAnio = periodoAnio;
        PeriodoNumero = periodoNumero;
        Concepto = concepto.Trim();
    }

    private void ReemplazarPartidas(IEnumerable<DatosPartida> partidas, bool admiteNegativos)
    {
        var lista = partidas.ToList();
        _partidas.Clear();
        for (var i = 0; i < lista.Count; i++)
            _partidas.Add(new PolizaPartida(Guid.CreateVersion7(), Id, EmpresaId, i + 1, lista[i], admiteNegativos));
        TotalCargos = _partidas.Sum(x => x.Cargo);
        TotalAbonos = _partidas.Sum(x => x.Abono);
    }

    private static string Recortar(string valor, int maximo, string campo)
    {
        var v = valor.Trim();
        if (v.Length == 0 || v.Length > maximo)
            throw new BusinessRuleException("CONTAB_POLIZA_ORIGEN_INVALIDO", $"El {campo} del documento de origen es requerido y admite hasta {maximo} caracteres.");
        return v;
    }
}

/// <summary>
/// Partida de una póliza (C1.3). En una partida normal solo hay cargo o abono, positivo. En una partida de anulación (K10.3) el
/// importe es negativo y conserva el lado de la original. Se audita (bitácora R23): solo se escribe al capturar o editar el borrador.
/// </summary>
public sealed class PolizaPartida : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int ConceptoMaximo = 300;

    public Guid EmpresaId { get; set; }
    public Guid PolizaId { get; private set; }
    public int Linea { get; private set; }
    public Guid CuentaId { get; private set; }
    public string CuentaCodigo { get; private set; } = string.Empty;
    public string? Concepto { get; private set; }
    public decimal Cargo { get; private set; }
    public decimal Abono { get; private set; }
    public string Moneda { get; private set; } = Poliza.MonedaNacional;
    public decimal TipoCambio { get; private set; } = 1m;
    public decimal? ImporteOriginal { get; private set; }
    public Guid SucursalId { get; private set; }
    public Guid? Dim1Id { get; private set; }
    public Guid? Dim2Id { get; private set; }
    public Guid? Dim3Id { get; private set; }
    public string? Proyecto { get; private set; }
    public Guid? ClienteId { get; private set; }
    public Guid? ProveedorId { get; private set; }
    public Guid? CuentaBancariaId { get; private set; }

    private PolizaPartida() { }

    internal PolizaPartida(Guid id, Guid polizaId, Guid empresaId, int linea, DatosPartida d, bool admiteNegativos) : base(id)
    {
        var etiqueta = $"Partida {linea}";
        if (d.CuentaId == Guid.Empty || string.IsNullOrWhiteSpace(d.CuentaCodigo))
            throw new BusinessRuleException("CONTAB_PARTIDA_CUENTA_REQUERIDA", $"{etiqueta}: elija la cuenta contable.");
        if (d.SucursalId == Guid.Empty)
            throw new BusinessRuleException("CONTAB_PARTIDA_SUCURSAL_REQUERIDA", $"{etiqueta}: elija la sucursal.");
        if (decimal.Round(d.Cargo, 2) != d.Cargo || decimal.Round(d.Abono, 2) != d.Abono)
            throw new BusinessRuleException("CONTAB_PARTIDA_IMPORTE_INVALIDO", $"{etiqueta}: los importes admiten hasta dos decimales.");
        var conCargo = d.Cargo != 0m;
        var conAbono = d.Abono != 0m;
        if (conCargo == conAbono)
            throw new BusinessRuleException("CONTAB_PARTIDA_UN_SOLO_LADO", $"{etiqueta}: capture un cargo o un abono (solo uno, distinto de cero).");
        var importe = conCargo ? d.Cargo : d.Abono;
        if (importe < 0m && !admiteNegativos)
            throw new BusinessRuleException("CONTAB_PARTIDA_IMPORTE_NEGATIVO",
                $"{etiqueta}: el importe debe ser positivo. Los importes negativos solo los genera la anulación de una póliza.");
        var moneda = (d.Moneda ?? Poliza.MonedaNacional).Trim().ToUpperInvariant();
        if (moneda.Length != 3 || !moneda.All(char.IsAsciiLetterUpper))
            throw new BusinessRuleException("CONTAB_PARTIDA_MONEDA_INVALIDA", $"{etiqueta}: la moneda es un código ISO de tres letras.");
        if (moneda == Poliza.MonedaNacional)
        {
            if (d.TipoCambio != 1m || d.ImporteOriginal is not null && d.ImporteOriginal != importe)
                throw new BusinessRuleException("CONTAB_PARTIDA_TIPO_CAMBIO_INVALIDO", $"{etiqueta}: en pesos el tipo de cambio es 1.");
        }
        else
        {
            if (d.TipoCambio <= 0m || d.ImporteOriginal is not { } original || original == 0m || Math.Sign(original) != Math.Sign(importe))
                throw new BusinessRuleException("CONTAB_PARTIDA_MONEDA_EXTRANJERA",
                    $"{etiqueta}: en {moneda} indique el importe original y el tipo de cambio.");
            if (Math.Abs(decimal.Round(original * d.TipoCambio, 2, MidpointRounding.AwayFromZero) - importe) > 0.01m)
                throw new BusinessRuleException("CONTAB_PARTIDA_CONVERSION_INVALIDA",
                    $"{etiqueta}: el importe en pesos no corresponde al importe original por el tipo de cambio.");
        }
        if (d.Concepto?.Trim().Length > ConceptoMaximo)
            throw new BusinessRuleException("CONTAB_PARTIDA_CONCEPTO_INVALIDO", $"{etiqueta}: el concepto admite hasta {ConceptoMaximo} caracteres.");
        if (d.Proyecto?.Trim().Length > 40)
            throw new BusinessRuleException("CONTAB_PARTIDA_PROYECTO_INVALIDO", $"{etiqueta}: la clave de proyecto admite hasta 40 caracteres.");

        PolizaId = polizaId;
        EmpresaId = empresaId;
        Linea = linea;
        CuentaId = d.CuentaId;
        CuentaCodigo = d.CuentaCodigo;
        Concepto = string.IsNullOrWhiteSpace(d.Concepto) ? null : d.Concepto.Trim();
        Cargo = d.Cargo;
        Abono = d.Abono;
        Moneda = moneda;
        TipoCambio = moneda == Poliza.MonedaNacional ? 1m : d.TipoCambio;
        ImporteOriginal = moneda == Poliza.MonedaNacional ? null : d.ImporteOriginal;
        SucursalId = d.SucursalId;
        Dim1Id = d.Dim1Id;
        Dim2Id = d.Dim2Id;
        Dim3Id = d.Dim3Id;
        Proyecto = string.IsNullOrWhiteSpace(d.Proyecto) ? null : d.Proyecto.Trim();
        ClienteId = d.ClienteId;
        ProveedorId = d.ProveedorId;
        CuentaBancariaId = d.CuentaBancariaId;
    }

    /// <summary>La misma partida en negativo y del mismo lado (K10.3).</summary>
    internal DatosPartida Negada() => new(CuentaId, CuentaCodigo, Concepto, -Cargo, -Abono, Moneda, TipoCambio, -ImporteOriginal,
        SucursalId, Dim1Id, Dim2Id, Dim3Id, Proyecto, ClienteId, ProveedorId, CuentaBancariaId);

    public DatosPartida Datos() => new(CuentaId, CuentaCodigo, Concepto, Cargo, Abono, Moneda, TipoCambio, ImporteOriginal,
        SucursalId, Dim1Id, Dim2Id, Dim3Id, Proyecto, ClienteId, ProveedorId, CuentaBancariaId);
}

/// <summary>
/// Último consecutivo usado por tipo de operación y año (K10.9). Se avanza con un UPSERT dentro de la transacción que guarda la
/// póliza: la fila queda bloqueada hasta el commit, así dos pólizas del mismo tipo no toman el mismo número y un rollback no deja
/// hueco. Un tipo nuevo crea su fila con el primer uso (K10.9-c). Supuesto V39: el consecutivo se reinicia cada año.
/// </summary>
public sealed class ConsecutivoPoliza
{
    public Guid EmpresaId { get; private set; }
    public Guid TipoOperacionId { get; private set; }
    public int Anio { get; private set; }
    public int UltimoNumero { get; private set; }

    private ConsecutivoPoliza() { }
}
