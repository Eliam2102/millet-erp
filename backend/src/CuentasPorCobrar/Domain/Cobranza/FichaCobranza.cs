using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Cobranza;

public enum TipoEventoFicha : short
{
    /// <summary>Se abrió la ficha o se asignó/reasignó el responsable.</summary>
    Asignacion = 1,
    /// <summary>Gestión de cobro (llamada, correo, whatsapp) con su resultado.</summary>
    Gestion = 2,
    /// <summary>El cliente prometió pago: fija la siguiente revisión.</summary>
    Promesa = 3,
    /// <summary>Se movió la siguiente revisión.</summary>
    Reprogramacion = 4,
    /// <summary>Pasó a un supervisor: cambia el responsable.</summary>
    Escalamiento = 5,
    /// <summary>Corrige un evento anterior sin editarlo (rastro).</summary>
    Correccion = 6,
    /// <summary>M-345: se cerró la ficha con motivo y, si quedaba saldo, su explicación.</summary>
    Cierre = 7,
}

/// <summary>M-345 (CXC-GESTION/s06): por qué se cierra la ficha de cobranza.</summary>
public enum MotivoCierreFicha : short
{
    Pagada = 1,
    /// <summary>Quedó un convenio o acuerdo de pago distinto de la promesa.</summary>
    AcuerdoDePago = 2,
    /// <summary>El cliente disputa la factura o el saldo.</summary>
    Disputa = 3,
    Otro = 4,
}

/// <summary>
/// M-241 (CXC-GESTION): ficha de gestión de cobranza de una factura vencida, con responsable, siguiente revisión y
/// escalamiento. Complementa al seguimiento por cliente; su historial (<see cref="FichaCobranzaEvento"/>) es append-only.
/// </summary>
public sealed class FichaCobranza : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid FacturaCarteraId { get; private set; }
    public Guid ClienteId { get; private set; }
    /// <summary>Null = «sin asignar»: la ficha se abre sola al vencer la factura y Cobranza la asigna.</summary>
    public Guid? ResponsableId { get; private set; }
    public string? ResponsableNombre { get; private set; }
    public DateOnly? SiguienteRevision { get; private set; }
    public bool Escalada { get; private set; }
    public Guid? EscaladaPorId { get; private set; }
    public DateTimeOffset? EscaladaEn { get; private set; }
    /// <summary>M-345: la ficha cerrada ya no admite gestiones nuevas; su historial queda.</summary>
    public bool Cerrada { get; private set; }
    public DateTimeOffset? CerradaEn { get; private set; }
    public MotivoCierreFicha? MotivoCierre { get; private set; }
    public decimal? SaldoAlCerrar { get; private set; }
    public string? ExplicacionSaldo { get; private set; }

    private FichaCobranza() { }

    public static (FichaCobranza Ficha, FichaCobranzaEvento Evento) Abrir(
        Guid empresaId, Guid facturaCarteraId, Guid clienteId, Guid responsableId, string? responsableNombre,
        DateOnly? siguienteRevision, Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        if (responsableId == Guid.Empty)
            throw new BusinessRuleException("FCO_RESPONSABLE_VACIO", "La ficha necesita un responsable.");
        return Crear(empresaId, facturaCarteraId, clienteId, responsableId, responsableNombre, siguienteRevision, usuarioId, usuarioNombre, ahora, "Ficha abierta");
    }

    /// <summary>
    /// Apertura automática al vencer la factura (evaluación de alertas): <paramref name="responsableId"/> es el último gestor del cliente
    /// si lo hay; si no, queda «sin asignar» (supuesto a validar con Millet). El evento queda a nombre del sistema.
    /// </summary>
    public static (FichaCobranza Ficha, FichaCobranzaEvento Evento) AbrirAlVencer(
        Guid empresaId, Guid facturaCarteraId, Guid clienteId, Guid? responsableId, string? responsableNombre, DateTimeOffset ahora)
        => Crear(empresaId, facturaCarteraId, clienteId, responsableId, responsableNombre, null, Guid.Empty, "Sistema", ahora,
            responsableId is null
                ? "Ficha abierta al vencer la factura: sin responsable asignado"
                : "Ficha abierta al vencer la factura: responsable por omisión (último gestor del cliente)");

    private static (FichaCobranza Ficha, FichaCobranzaEvento Evento) Crear(
        Guid empresaId, Guid facturaCarteraId, Guid clienteId, Guid? responsableId, string? responsableNombre,
        DateOnly? siguienteRevision, Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora, string nota)
    {
        var ficha = new FichaCobranza
        {
            Id = Guid.CreateVersion7(), EmpresaId = empresaId, FacturaCarteraId = facturaCarteraId, ClienteId = clienteId,
            ResponsableId = responsableId, ResponsableNombre = responsableNombre, SiguienteRevision = siguienteRevision,
        };
        var evento = FichaCobranzaEvento.Crear(ficha, TipoEventoFicha.Asignacion, usuarioId, usuarioNombre, ahora,
            nota: nota, responsableDespuesId: responsableId, responsableDespuesNombre: responsableNombre,
            revisionDespues: siguienteRevision);
        return (ficha, evento);
    }

    public FichaCobranzaEvento Reasignar(Guid responsableId, string? nombre, string motivo, Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        // Asignar a una ficha «sin asignar» no pide motivo; cambiar a un responsable ya existente, sí.
        if (ResponsableId is not null) Exigir(motivo);
        if (responsableId == Guid.Empty)
            throw new BusinessRuleException("FCO_RESPONSABLE_VACIO", "La ficha necesita un responsable.");
        if (responsableId == ResponsableId)
            throw new BusinessRuleException("FCO_SIN_CAMBIO", "Esa persona ya es la responsable.");
        var antes = (ResponsableId, ResponsableNombre);
        ResponsableId = responsableId; ResponsableNombre = nombre;
        return FichaCobranzaEvento.Crear(this, TipoEventoFicha.Asignacion, usuarioId, usuarioNombre, ahora,
            nota: string.IsNullOrWhiteSpace(motivo) ? "Responsable asignado" : motivo.Trim(),
            responsableAntesId: antes.ResponsableId, responsableAntesNombre: antes.ResponsableNombre,
            responsableDespuesId: responsableId, responsableDespuesNombre: nombre);
    }

    /// <summary>Gestión de cobro. Una promesa (monto + fecha) fija la siguiente revisión en la fecha comprometida.</summary>
    public FichaCobranzaEvento RegistrarGestion(CanalCobranza canal, ResultadoCobranza resultado, string nota,
        decimal? monto, DateOnly? fechaComprometida, DateOnly? siguienteRevision, Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        ExigirAbierta();
        if (string.IsNullOrWhiteSpace(nota))
            throw new BusinessRuleException("FCO_NOTA_VACIA", "La nota de la gestión es obligatoria.");
        var hoy = DateOnly.FromDateTime(ahora.UtcDateTime);
        var antes = SiguienteRevision;
        TipoEventoFicha tipo;
        if (resultado == ResultadoCobranza.PromesaPago)
        {
            if (monto is not > 0)
                throw new BusinessRuleException("FCO_PROMESA_SIN_MONTO", "Una promesa de pago exige el monto comprometido (> 0).");
            if (fechaComprometida is null)
                throw new BusinessRuleException("FCO_PROMESA_SIN_FECHA", "Una promesa de pago exige la fecha comprometida.");
            if (fechaComprometida.Value < hoy)
                throw new BusinessRuleException("FCO_PROMESA_FECHA_PASADA", "La fecha comprometida no puede ser anterior a la gestión.");
            SiguienteRevision = fechaComprometida;
            tipo = TipoEventoFicha.Promesa;
        }
        else
        {
            if (monto is not null || fechaComprometida is not null)
                throw new BusinessRuleException("FCO_COMPROMISO_SOBRANTE", "Monto y fecha comprometidos solo aplican a promesa de pago.");
            if (siguienteRevision is { } r)
            {
                if (r < hoy) throw new BusinessRuleException("FCO_REVISION_PASADA", "La siguiente revisión no puede ser anterior a hoy.");
                SiguienteRevision = r;
            }
            tipo = TipoEventoFicha.Gestion;
        }
        return FichaCobranzaEvento.Crear(this, tipo, usuarioId, usuarioNombre, ahora, nota: nota.Trim(), canal: canal, resultado: resultado,
            monto: monto, fechaComprometida: fechaComprometida, revisionAntes: antes, revisionDespues: SiguienteRevision);
    }

    public FichaCobranzaEvento Reprogramar(DateOnly nueva, string motivo, Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        ExigirAbierta();
        Exigir(motivo);
        if (nueva < DateOnly.FromDateTime(ahora.UtcDateTime))
            throw new BusinessRuleException("FCO_REVISION_PASADA", "La siguiente revisión no puede ser anterior a hoy.");
        if (nueva == SiguienteRevision)
            throw new BusinessRuleException("FCO_SIN_CAMBIO", "La siguiente revisión ya es esa fecha.");
        var antes = SiguienteRevision;
        SiguienteRevision = nueva;
        return FichaCobranzaEvento.Crear(this, TipoEventoFicha.Reprogramacion, usuarioId, usuarioNombre, ahora, nota: motivo.Trim(),
            revisionAntes: antes, revisionDespues: nueva);
    }

    /// <summary>Escala la gestión a un supervisor: él pasa a ser el responsable y queda el rastro de quién y por qué.</summary>
    public FichaCobranzaEvento Escalar(Guid supervisorId, string? supervisorNombre, string motivo, Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        ExigirAbierta();
        Exigir(motivo);
        if (supervisorId == Guid.Empty)
            throw new BusinessRuleException("FCO_SUPERVISOR_VACIO", "Indica a qué supervisor se escala.");
        if (supervisorId == ResponsableId)
            throw new BusinessRuleException("FCO_SUPERVISOR_ES_RESPONSABLE", "Escala a una persona distinta del responsable actual.");
        var antes = (ResponsableId, ResponsableNombre);
        ResponsableId = supervisorId; ResponsableNombre = supervisorNombre;
        Escalada = true; EscaladaPorId = usuarioId; EscaladaEn = ahora;
        return FichaCobranzaEvento.Crear(this, TipoEventoFicha.Escalamiento, usuarioId, usuarioNombre, ahora, nota: motivo.Trim(),
            responsableAntesId: antes.ResponsableId, responsableAntesNombre: antes.ResponsableNombre,
            responsableDespuesId: supervisorId, responsableDespuesNombre: supervisorNombre);
    }

    /// <summary>Corrige un evento anterior: el original no se edita; se agrega un evento que dice qué cambió y por qué.</summary>
    public FichaCobranzaEvento Corregir(FichaCobranzaEvento original, string notaCorregida, string motivo, Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        Exigir(motivo);
        if (original.FichaCobranzaId != Id)
            throw new BusinessRuleException("FCO_EVENTO_AJENO", "El evento no pertenece a esta ficha.");
        if (original.Tipo is TipoEventoFicha.Correccion or TipoEventoFicha.Asignacion)
            throw new BusinessRuleException("FCO_EVENTO_NO_CORREGIBLE", "Solo se corrigen gestiones, promesas, reprogramaciones y escalamientos.");
        if (string.IsNullOrWhiteSpace(notaCorregida))
            throw new BusinessRuleException("FCO_NOTA_VACIA", "La nota corregida es obligatoria.");
        if (notaCorregida.Trim() == original.Nota)
            throw new BusinessRuleException("FCO_SIN_CAMBIO", "La nota corregida es igual a la original.");
        return FichaCobranzaEvento.Crear(this, TipoEventoFicha.Correccion, usuarioId, usuarioNombre, ahora, nota: notaCorregida.Trim(),
            corrigeEventoId: original.Id, notaAnterior: original.Nota, motivoCorreccion: motivo.Trim());
    }

    /// <summary>
    /// M-345: cierra la ficha con motivo. Con saldo pendiente exige explicarlo (qué se acordó o por qué queda): la ficha
    /// no cierra con saldo sin explicar. «Pagada» solo vale sin saldo.
    /// </summary>
    public FichaCobranzaEvento Cerrar(MotivoCierreFicha motivo, decimal saldoPendiente, string? explicacionSaldo,
        Guid usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        ExigirAbierta();
        if (saldoPendiente < 0)
            throw new BusinessRuleException("FCO_SALDO_INVALIDO", "El saldo pendiente no puede ser negativo.");
        var explicacion = explicacionSaldo?.Trim();
        if (saldoPendiente > 0)
        {
            if (motivo == MotivoCierreFicha.Pagada)
                throw new BusinessRuleException("FCO_PAGADA_CON_SALDO", "La factura aún tiene saldo: elige otro motivo de cierre y explica el saldo.");
            if (string.IsNullOrWhiteSpace(explicacion))
                throw new BusinessRuleException("FCO_SALDO_SIN_EXPLICAR", "La ficha no se cierra con saldo sin explicar: indica qué pasó con el saldo pendiente.");
        }
        else if (motivo != MotivoCierreFicha.Pagada)
        {
            throw new BusinessRuleException("FCO_CIERRE_SIN_SALDO", "Sin saldo pendiente la ficha se cierra como pagada.");
        }
        Cerrada = true; CerradaEn = ahora; MotivoCierre = motivo; SaldoAlCerrar = saldoPendiente;
        ExplicacionSaldo = string.IsNullOrWhiteSpace(explicacion) ? null : explicacion;
        return FichaCobranzaEvento.Crear(this, TipoEventoFicha.Cierre, usuarioId, usuarioNombre, ahora,
            nota: ExplicacionSaldo ?? $"Ficha cerrada: {motivo}", monto: saldoPendiente);
    }

    private void ExigirAbierta()
    {
        if (Cerrada)
            throw new BusinessRuleException("FCO_FICHA_CERRADA", "La ficha está cerrada: ya no admite cambios.");
    }

    private static void Exigir(string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("FCO_MOTIVO_VACIO", "El motivo es obligatorio.");
    }
}

/// <summary>Evento append-only de la ficha: quién, cuándo, qué y el antes/después.</summary>
public sealed class FichaCobranzaEvento : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid FichaCobranzaId { get; private set; }
    public TipoEventoFicha Tipo { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string? UsuarioNombre { get; private set; }
    public string Nota { get; private set; } = default!;
    public CanalCobranza? Canal { get; private set; }
    public ResultadoCobranza? Resultado { get; private set; }
    public decimal? MontoComprometido { get; private set; }
    public DateOnly? FechaComprometida { get; private set; }
    public DateOnly? RevisionAntes { get; private set; }
    public DateOnly? RevisionDespues { get; private set; }
    public Guid? ResponsableAntesId { get; private set; }
    public string? ResponsableAntesNombre { get; private set; }
    public Guid? ResponsableDespuesId { get; private set; }
    public string? ResponsableDespuesNombre { get; private set; }
    public Guid? CorrigeEventoId { get; private set; }
    public string? NotaAnterior { get; private set; }
    public string? MotivoCorreccion { get; private set; }

    private FichaCobranzaEvento() { }

    internal static FichaCobranzaEvento Crear(FichaCobranza ficha, TipoEventoFicha tipo, Guid usuarioId, string? usuarioNombre,
        DateTimeOffset ahora, string nota, CanalCobranza? canal = null, ResultadoCobranza? resultado = null, decimal? monto = null,
        DateOnly? fechaComprometida = null, DateOnly? revisionAntes = null, DateOnly? revisionDespues = null,
        Guid? responsableAntesId = null, string? responsableAntesNombre = null, Guid? responsableDespuesId = null,
        string? responsableDespuesNombre = null, Guid? corrigeEventoId = null, string? notaAnterior = null, string? motivoCorreccion = null)
        => new()
        {
            Id = Guid.CreateVersion7(), EmpresaId = ficha.EmpresaId, FichaCobranzaId = ficha.Id, Tipo = tipo, Fecha = ahora.ToUniversalTime(),
            UsuarioId = usuarioId, UsuarioNombre = usuarioNombre, Nota = nota, Canal = canal, Resultado = resultado, MontoComprometido = monto,
            FechaComprometida = fechaComprometida, RevisionAntes = revisionAntes, RevisionDespues = revisionDespues,
            ResponsableAntesId = responsableAntesId, ResponsableAntesNombre = responsableAntesNombre,
            ResponsableDespuesId = responsableDespuesId, ResponsableDespuesNombre = responsableDespuesNombre,
            CorrigeEventoId = corrigeEventoId, NotaAnterior = notaAnterior, MotivoCorreccion = motivoCorreccion,
        };
}
