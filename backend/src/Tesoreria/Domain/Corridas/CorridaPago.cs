using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.Tesoreria.Domain.Pasivos;

namespace Millet.Tesoreria.Domain.Corridas;

/// <summary>
/// Corrida de pagos (§4.5, TES-8): lote de pasivos seleccionados para
/// desembolso, autorizable como unidad. El oficio de cartera es artefacto
/// de la corrida autorizada (ADR-0036).
///
/// <para>
/// T9.1-A (armar y autorizar): <c>Borrador → EnAutorizacion → Autorizada</c>;
/// <c>Rechazada</c>/<c>Cancelada</c> solo desde los dos primeros. Quien arma
/// no autoriza. Las firmas requeridas salen del parámetro
/// <see cref="PoliticaFirmaCorrida"/> y se congelan al enviar a autorización.
/// <c>Ejecutada</c>/<c>Cerrada</c> y el oficio llegan en la Parte B (86e3jvvhf).
/// </para>
/// </summary>
public sealed class CorridaPago : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public string Nombre { get; private set; } = string.Empty;
    public Guid CuentaBancariaId { get; private set; }
    public EstadoCorridaPago Estado { get; private set; } = EstadoCorridaPago.Borrador;
    public decimal Total { get; private set; }
    public string Moneda { get; private set; } = default!;
    public Guid SolicitadaPor { get; private set; }
    public Guid? AutorizadaPor { get; private set; }
    public DateTimeOffset? AutorizadaEn { get; private set; }
    public DateTimeOffset? OficioGeneradoEn { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }

    public int FirmasRequeridas { get; private set; } = 1;
    public string? RolRequerido { get; private set; }
    /// <summary>Contador en la raíz: cada firma sube la versión y dos firmas simultáneas chocan (ADR-0012).</summary>
    public int FirmasRecibidas { get; private set; }
    public string? MotivoCierre { get; private set; }
    /// <summary>Avisos acumulados (p. ej. pasivo retirado por CxP), uno por línea.</summary>
    public string? Avisos { get; private set; }

    private readonly List<CorridaPagoLinea> _lineas = [];
    public IReadOnlyCollection<CorridaPagoLinea> Lineas => _lineas.AsReadOnly();

    private readonly List<CorridaPagoFirma> _firmas = [];
    public IReadOnlyCollection<CorridaPagoFirma> Firmas => _firmas.AsReadOnly();

    private CorridaPago() { }

    public static CorridaPago Crear(Guid empresaId, string? nombre, Guid cuentaBancariaId, string moneda, Guid usuarioId, DateTimeOffset ahora)
    {
        if (cuentaBancariaId == Guid.Empty || string.IsNullOrWhiteSpace(moneda) || usuarioId == Guid.Empty)
            throw new BusinessRuleException("CORRIDA_DATOS_INCOMPLETOS", "La corrida requiere cuenta bancaria, moneda y usuario.");
        var c = new CorridaPago
        {
            EmpresaId = empresaId, CuentaBancariaId = cuentaBancariaId, Moneda = moneda, SolicitadaPor = usuarioId, CreadaEn = ahora,
        };
        c.Id = Guid.CreateVersion7();
        c.Nombre = string.IsNullOrWhiteSpace(nombre) ? $"Corrida {moneda} {ahora:yyyy-MM-dd} {c.Id.ToString("N")[^6..]}" : nombre.Trim();
        return c;
    }

    /// <summary>
    /// Agrega el pasivo y devuelve la línea nueva (null si ya estaba). El llamador la registra con
    /// <c>db.Add</c>: EF trata como existente una hija con Id asignado descubierta por navegación.
    /// </summary>
    public CorridaPagoLinea? AgregarLinea(PasivoPendientePago pasivo)
    {
        Exigir(EstadoCorridaPago.Borrador, "agregar pasivos a");
        if (pasivo.Moneda != Moneda)
            throw new BusinessRuleException("CORRIDA_MONEDA_DISTINTA", $"La corrida es en {Moneda} y el pasivo está en {pasivo.Moneda}.");
        if (pasivo.SaldoPendiente <= 0)
            throw new BusinessRuleException("CORRIDA_PASIVO_SIN_SALDO", "El pasivo no tiene saldo por pagar.");
        if (_lineas.Any(l => l.FacturaProveedorId == pasivo.FacturaProveedorId)) return null;
        var linea = new CorridaPagoLinea(Id, pasivo);
        _lineas.Add(linea);
        Recalcular();
        return linea;
    }

    public void QuitarLinea(Guid facturaProveedorId)
    {
        Exigir(EstadoCorridaPago.Borrador, "quitar pasivos de");
        var l = _lineas.FirstOrDefault(x => x.FacturaProveedorId == facturaProveedorId)
            ?? throw new BusinessRuleException("CORRIDA_LINEA_NO_ENCONTRADA", "El pasivo no está en la corrida.");
        _lineas.Remove(l);
        Recalcular();
    }

    /// <summary>Congela el requisito de firmas según el total y pasa a autorización.</summary>
    public void EnviarAAutorizacion(int firmasRequeridas, string? rolRequerido)
    {
        Exigir(EstadoCorridaPago.Borrador, "enviar a autorización");
        if (_lineas.Count == 0)
            throw new BusinessRuleException("CORRIDA_SIN_LINEAS", "La corrida no tiene pasivos.");
        FirmasRequeridas = firmasRequeridas; RolRequerido = rolRequerido;
        Estado = EstadoCorridaPago.EnAutorizacion;
    }

    /// <summary>Agrega la firma de <paramref name="usuarioId"/>; autoriza al cumplir las firmas y el rol requeridos.</summary>
    public CorridaPagoFirma Firmar(Guid usuarioId, IReadOnlyCollection<string> roles, DateTimeOffset ahora)
    {
        Exigir(EstadoCorridaPago.EnAutorizacion, "firmar");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("CORRIDA_USUARIO_VACIO", "El usuario que firma es obligatorio.");
        if (usuarioId == SolicitadaPor)
            throw new BusinessRuleException("CORRIDA_MISMO_USUARIO", "Quien armó la corrida no puede autorizarla. Pide la firma al Gerente o al Jefe de Tesorería.");
        if (_firmas.Any(f => f.UsuarioId == usuarioId))
            throw new BusinessRuleException("CORRIDA_YA_FIRMADA", "Ya firmaste esta corrida; falta la firma de otra persona.");
        var firma = new CorridaPagoFirma(Id, usuarioId, roles, ahora);
        _firmas.Add(firma);
        FirmasRecibidas++;
        if (FirmasRecibidas >= FirmasRequeridas && (RolRequerido is null || _firmas.Any(f => f.TieneRol(RolRequerido))))
        {
            Estado = EstadoCorridaPago.Autorizada; AutorizadaPor = usuarioId; AutorizadaEn = ahora;
        }
        return firma;
    }

    public void Rechazar(string motivo, Guid usuarioId)
    {
        if (Estado is not (EstadoCorridaPago.Borrador or EstadoCorridaPago.EnAutorizacion))
            throw new BusinessRuleException("CORRIDA_ESTADO_INVALIDO", "Solo se rechaza una corrida en borrador o en autorización.");
        if (usuarioId == SolicitadaPor)
            throw new BusinessRuleException("CORRIDA_MISMO_USUARIO", "Quien armó la corrida no puede rechazarla; si ya no procede, cancélala.");
        Cerrar(EstadoCorridaPago.Rechazada, motivo);
    }

    public decimal Ejecutado => _lineas.Where(l => l.Ejecutada).Sum(l => l.ImporteProgramado);
    public decimal Pendiente => Total - Ejecutado;

    /// <summary>El oficio solo existe para la versión autorizada (y la que ya se ejecuta o cerró).</summary>
    public void RegistrarOficio(DateTimeOffset ahora)
    {
        if (Estado is not (EstadoCorridaPago.Autorizada or EstadoCorridaPago.Ejecutada or EstadoCorridaPago.Cerrada))
            throw new BusinessRuleException("CORRIDA_OFICIO_NO_AUTORIZADA",
                $"El oficio solo se genera de una corrida autorizada; esta está en {Estado}.");
        OficioGeneradoEn ??= ahora;
    }

    /// <summary>
    /// Marca ejecutadas las líneas pagadas con <paramref name="referencia"/> (un pago de un solo proveedor). Devuelve las
    /// líneas por pagar; vacío si es un reintento de la misma referencia (no se suma dos veces). Ejecución parcial permitida:
    /// la corrida pasa a <c>Ejecutada</c> con el resto pendiente.
    /// </summary>
    public IReadOnlyList<CorridaPagoLinea> RegistrarEjecucion(IReadOnlyCollection<Guid> facturas, string referencia, DateOnly fecha)
    {
        if (Estado is not (EstadoCorridaPago.Autorizada or EstadoCorridaPago.Ejecutada))
            throw new BusinessRuleException("CORRIDA_ESTADO_INVALIDO", $"No se puede registrar la ejecución de una corrida en estado {Estado}.");
        if (string.IsNullOrWhiteSpace(referencia))
            throw new BusinessRuleException("CORRIDA_REFERENCIA_VACIA", "La referencia bancaria es obligatoria.");
        referencia = referencia.Trim();
        var lineas = facturas.Select(f => _lineas.FirstOrDefault(l => l.FacturaProveedorId == f)
            ?? throw new BusinessRuleException("CORRIDA_LINEA_NO_ENCONTRADA", $"El pasivo {f} no está en la corrida.")).ToList();
        if (lineas.Count == 0)
            throw new BusinessRuleException("CORRIDA_LINEA_NO_ENCONTRADA", "Indica al menos un pasivo de la corrida.");
        if (lineas.Select(l => l.ProveedorId).Distinct().Count() > 1)
            throw new BusinessRuleException("CORRIDA_PAGO_MULTIPROVEEDOR", "Un pago cubre pasivos de un solo proveedor; registra un pago por proveedor.");

        var mismaRef = (string? r) => string.Equals(r, referencia, StringComparison.OrdinalIgnoreCase);
        if (lineas.All(l => l.Ejecutada && mismaRef(l.ReferenciaBancaria))) return [];
        if (lineas.Any(l => l.Ejecutada))
            throw new BusinessRuleException("CORRIDA_LINEA_YA_EJECUTADA", "Alguno de los pasivos ya se ejecutó con otra referencia.");
        if (_lineas.Any(l => l.Ejecutada && mismaRef(l.ReferenciaBancaria)))
            throw new BusinessRuleException("CORRIDA_REFERENCIA_REPETIDA", $"La referencia '{referencia}' ya se registró en esta corrida para otros pasivos.");

        foreach (var l in lineas) l.MarcarEjecutada(referencia, fecha);
        Estado = EstadoCorridaPago.Ejecutada;
        return lineas;
    }

    /// <summary>La reversa de un pago de la corrida deja su línea pendiente otra vez (el contramovimiento lo crea el pago).</summary>
    public void RevertirEjecucion(Guid facturaProveedorId)
    {
        _lineas.FirstOrDefault(l => l.FacturaProveedorId == facturaProveedorId)?.RevertirEjecucion();
        if (Estado == EstadoCorridaPago.Ejecutada && _lineas.All(l => !l.Ejecutada)) Estado = EstadoCorridaPago.Autorizada;
    }

    /// <summary>Cierra la corrida ejecutada; lo que quedó sin pagar se libera para otra corrida.</summary>
    public void CerrarEjecucion()
    {
        if (Estado != EstadoCorridaPago.Ejecutada)
            throw new BusinessRuleException("CORRIDA_ESTADO_INVALIDO", "Solo se cierra una corrida con pagos ejecutados.");
        Estado = EstadoCorridaPago.Cerrada;
        foreach (var l in _lineas) l.Liberar();
    }

    public void Cancelar(string motivo)
    {
        if (Estado is not (EstadoCorridaPago.Borrador or EstadoCorridaPago.EnAutorizacion))
            throw new BusinessRuleException("CORRIDA_ESTADO_INVALIDO", "Solo se cancela una corrida en borrador o en autorización.");
        Cerrar(EstadoCorridaPago.Cancelada, motivo);
    }

    /// <summary>
    /// CxP retiró la autorización del pasivo. En borrador o autorización la línea sale de la corrida y, si ya había
    /// firmas, la corrida vuelve a borrador (el total cambió). Autorizada: solo se avisa; la Parte B revalida al ejecutar.
    /// </summary>
    public bool RetirarPasivo(Guid facturaProveedorId, string motivo)
    {
        var linea = _lineas.FirstOrDefault(l => l.FacturaProveedorId == facturaProveedorId);
        if (linea is null || Estado is not (EstadoCorridaPago.Borrador or EstadoCorridaPago.EnAutorizacion or EstadoCorridaPago.Autorizada)) return false;
        Avisos = (Avisos is null ? "" : Avisos + "\n") + $"Cuentas por Pagar retiró el pasivo {facturaProveedorId} ({motivo}).";
        if (Estado == EstadoCorridaPago.Autorizada) return true;
        _lineas.Remove(linea);
        _firmas.Clear();
        FirmasRecibidas = 0;
        Estado = EstadoCorridaPago.Borrador;
        Recalcular();
        return true;
    }

    private void Cerrar(EstadoCorridaPago estado, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CORRIDA_MOTIVO_VACIO", "Indica el motivo.");
        Estado = estado; MotivoCierre = motivo.Trim();
        foreach (var l in _lineas) l.Liberar();
    }

    private void Recalcular() => Total = _lineas.Sum(l => l.ImporteProgramado);

    private void Exigir(EstadoCorridaPago esperado, string accion)
    {
        if (Estado != esperado)
            throw new BusinessRuleException("CORRIDA_ESTADO_INVALIDO", $"No se puede {accion} una corrida en estado {Estado}.");
    }
}

/// <summary>Estados de la corrida (§4.2 del 01-diseño; RN-5).</summary>
public enum EstadoCorridaPago : short
{
    Borrador = 1,
    EnAutorizacion = 2,
    Autorizada = 3,
    Ejecutada = 4,
    Cerrada = 5,
    Rechazada = 6,
    Cancelada = 7,
}
