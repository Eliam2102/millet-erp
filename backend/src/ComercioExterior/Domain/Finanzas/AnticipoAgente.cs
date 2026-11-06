using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public sealed record ImpuestosPedimento(decimal Iva, decimal Igi, decimal Prv)
{
    public decimal Total => Iva + Igi + Prv;
    public void Validar()
    {
        if (new[] { Iva, Igi, Prv }.Any(x => x < 0 || x > 999_999_999_999m || decimal.Round(x, 2) != x))
            throw new BusinessRuleException("CE_IMPUESTOS_INVALIDOS", "Los impuestos deben ser importes no negativos a centavos.");
    }
}
public sealed record ResolucionSaldo(string Tipo, decimal Importe, DateOnly Fecha, string Referencia,
    Guid EvidenciaId, Guid? AnticipoDestinoId, Guid UsuarioId);
public sealed record ComprobacionAnterior(ImpuestosPedimento Impuestos, Guid PedimentoId, Guid ComprobanteId,
    DateOnly Fecha, Guid? UsuarioId, decimal GastosMxn, string Motivo, Guid CorregidoPor, IReadOnlyList<Guid>? GastoIds = null);
public sealed class AnticipoAgente : BaseEntity, IPerteneceAEmpresa, IAuditable, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid AgenteId { get; private set; }
    public string Pedimento { get; private set; } = "";
    public decimal Iva { get; private set; }
    public decimal Igi { get; private set; }
    public decimal Prv { get; private set; }
    public DateOnly Fecha { get; private set; }
    public string? ReferenciaPago { get; private set; }
    public decimal CompensacionRecibida { get; private set; }
    public bool CuentaPeca { get; private set; }
    public decimal GastosAplicadosMxn { get; private set; }
    public Guid? SoporteCalculoId { get; private set; }
    public decimal? ImporteSolicitado { get; private set; }
    public string? MotivoCancelacion { get; private set; }
    public void Cancelar(string motivo)
    {
        if (PagadoMxn > 0 || Aplicado is not null || string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 400)
            throw new BusinessRuleException("CE_CANCELACION_INVALIDA", "Sólo se cancela una solicitud sin pago, con motivo de hasta 400 caracteres.");
        MotivoCancelacion = motivo.Trim();
    }
    public int RevisionComprobacion { get; private set; }
    public DateOnly? FechaPago { get; private set; }
    public Guid? PagoId { get; private set; }
    public Guid? ComprobadoPor { get; private set; }
    public List<ComprobacionAnterior> HistorialComprobaciones { get; private set; } = [];
    public List<Guid> PagosRecibidos { get; private set; } = [];
    public decimal PagadoMxn { get; private set; }
    public decimal Anticipado => ImporteSolicitado ?? Iva + Igi + Prv;
    public decimal? IvaAplicado { get; private set; }
    public decimal? IgiAplicado { get; private set; }
    public decimal? PrvAplicado { get; private set; }
    public Guid? PedimentoPagadoId { get; private set; }
    public Guid? ComprobantePagoId { get; private set; }
    public DateOnly? FechaComprobacion { get; private set; }
    public decimal? Aplicado => IvaAplicado + IgiAplicado + PrvAplicado + GastosAplicadosMxn;
    public decimal? Diferencia => Anticipado - Aplicado;
    public decimal? Saldo => Diferencia - Resoluciones.Sum(r => r.Importe);
    public string Estado => MotivoCancelacion is not null ? "Cancelado" : DevolucionPendienteId is not null ? "Devolución pendiente de depósito" : Aplicado is null ? SoporteCalculoId is null ? "Sin comprobar" : PagadoMxn >= Anticipado ? "Pagado" : PagadoMxn > 0 ? "Pago parcial" : PagosRevertidos.Count > 0 ? "Autorizado" : "Solicitado" : Saldo > 0 ? "Saldo a favor abierto" : Saldo < 0 ? "Por pagar al agente" : Resoluciones.Any(r => r.Tipo == "saldo-al-agente") ? "Saldo trasladado a la cuenta del agente" : DepositosRecibidos.Count > 0 ? "Saldado" : "Comprobado";
    public List<ResolucionSaldo> Resoluciones { get; private set; } = [];
    private AnticipoAgente() { }
    internal AnticipoAgente(Guid id, Guid empresa, Guid expediente, Guid agente, string pedimento, ImpuestosPedimento impuestos,
        DateOnly fecha, string? referencia, bool cuentaPeca = false) : base(id)
    {
        CuentaPeca = cuentaPeca;
        impuestos.Validar();
        if (agente == Guid.Empty || string.IsNullOrWhiteSpace(pedimento) || pedimento.Length > 100 || impuestos.Total <= 0 || fecha == default || referencia?.Length > 200)
            throw new BusinessRuleException("CE_ANTICIPO_INVALIDO", "Capture agente, pedimento, fecha e impuestos solicitados.");
        EmpresaId = empresa; ExpedienteId = expediente; AgenteId = agente; Pedimento = pedimento.Trim();
        Iva = impuestos.Iva; Igi = impuestos.Igi; Prv = impuestos.Prv; Fecha = fecha; ReferenciaPago = referencia;
    }
    public Guid? DevolucionPendienteId { get; private set; }
    public ResolucionSaldo? DevolucionAprobada { get; private set; }
    public List<Guid> DepositosRecibidos { get; private set; } = [];
    public Guid AprobarDevolucion(decimal importe, DateOnly fecha, string referencia, Guid evidencia, Guid usuario)
    {
        Resolver("devolucion", importe, fecha, referencia, evidencia, usuario, registroDevolucion: true);
        DevolucionAprobada = Resoluciones[^1]; Resoluciones = Resoluciones.Take(Resoluciones.Count - 1).ToList();
        DevolucionPendienteId = Guid.CreateVersion7(); return DevolucionPendienteId.Value;
    }
    public void ConfirmarDevolucion(Guid devolucion, decimal importe, string referencia)
    {
        if (DepositosRecibidos.Contains(devolucion)) return;
        if (DevolucionPendienteId != devolucion || DevolucionAprobada is not { } aprobada || aprobada.Importe != importe || string.IsNullOrWhiteSpace(referencia))
            throw new BusinessRuleException("CE_DEVOLUCION_INVALIDA", "El depósito no corresponde a la devolución aprobada.");
        Resoluciones = [.. Resoluciones, aprobada with { Referencia = referencia }];
        DepositosRecibidos = [.. DepositosRecibidos, devolucion]; DevolucionPendienteId = null; DevolucionAprobada = null;
    }
    public void Solicitar(Guid soporte, decimal importe)
    {
        if (soporte == Guid.Empty || importe <= 0 || importe > 999_999_999_999m || decimal.Round(importe, 2) != importe || SoporteCalculoId is not null)
            throw new BusinessRuleException("CE_SOPORTE_CALCULO_REQUERIDO", "Adjunte el cálculo de impuestos y capture un importe positivo a centavos.");
        SoporteCalculoId = soporte; ImporteSolicitado = importe; ReferenciaPago = null;
    }
    public List<Guid> PagosRevertidos { get; private set; } = [];
    public string? MotivoReversa { get; private set; }
    public void RevertirPago(Guid pago, decimal importe, string motivo)
    {
        if (PagosRevertidos.Contains(pago)) return;
        if (Aplicado is not null || importe <= 0 || !PagosRecibidos.Contains(pago) || string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CE_REVERSA_INVALIDA", "Sólo se puede revertir un pago recibido sin comprobación.");
        PagadoMxn -= importe; PagosRevertidos = [.. PagosRevertidos, pago];
        PagoId = null; ReferenciaPago = null; FechaPago = null; MotivoReversa = motivo.Trim();
    }
    public void RegistrarPago(Guid pago, decimal importe, string? referencia, DateOnly? fechaPago = null)
    {
        if (PagosRecibidos.Contains(pago)) return;
        if (MotivoCancelacion is not null || SoporteCalculoId is null || pago == Guid.Empty || importe <= 0 || PagadoMxn + importe > Anticipado ||
            string.IsNullOrWhiteSpace(referencia) || referencia.Length > 200)
            throw new BusinessRuleException("CE_PAGO_INVALIDO", "El pago debe corresponder al importe pendiente y traer la referencia bancaria.");
        PagosRecibidos = [.. PagosRecibidos, pago]; PagadoMxn += importe; PagoId = pago; ReferenciaPago = referencia; FechaPago = fechaPago;
    }
    public void Corregir(ImpuestosPedimento impuestos, Guid pedimento, Guid comprobante, DateOnly fecha,
        Guid usuario, string motivo, IReadOnlyCollection<GastoExpediente> gastos, IReadOnlyCollection<GastoExpediente>? nuevos = null)
    {
        impuestos.Validar();
        if (Aplicado is null || DevolucionPendienteId is not null || string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 500 ||
            pedimento == Guid.Empty || comprobante == Guid.Empty || fecha < Fecha || usuario == Guid.Empty)
            throw new BusinessRuleException("CE_CORRECCION_INVALIDA", "La corrección requiere comprobación abierta, motivo y evidencia válida.");
        if (gastos.Any(g => g.AnticipoAgenteId != Id))
            throw new BusinessRuleException("CE_GASTO_AGENTE_INVALIDO", "Seleccione los gastos de esta comprobación.");
        nuevos ??= gastos;
        if (nuevos.Any(g => g.FacturaProveedorId is null))
            throw new BusinessRuleException("CE_GASTO_SIN_FACTURA", "Para aplicar un gasto al anticipo, ligue antes la factura del agente.");
        if ((CuentaPeca && nuevos.Count > 0) || nuevos.Any(g => g.EmpresaId != EmpresaId || g.ExpedienteId != ExpedienteId ||
            g.ProveedorId != AgenteId || (g.AnticipoAgenteId is not null && g.AnticipoAgenteId != Id)) || nuevos.Select(g => g.Id).Distinct().Count() != nuevos.Count)
            throw new BusinessRuleException("CE_GASTO_AGENTE_INVALIDO", "Seleccione gastos únicos del mismo agente y expediente, sin aplicar a otro anticipo.");
        if (Resoluciones.Count > 0) throw new BusinessRuleException("CE_RESOLUCION_APROBADA", "No se puede corregir una comprobación con resolución aprobada.");
        HistorialComprobaciones = [.. HistorialComprobaciones, new(new(IvaAplicado!.Value, IgiAplicado!.Value, PrvAplicado!.Value),
            PedimentoPagadoId!.Value, ComprobantePagoId!.Value, FechaComprobacion!.Value, ComprobadoPor, GastosAplicadosMxn, motivo.Trim(), usuario, gastos.Select(g => g.Id).ToArray())];
        RevisionComprobacion++;
        IvaAplicado = impuestos.Iva; IgiAplicado = impuestos.Igi; PrvAplicado = impuestos.Prv;
        PedimentoPagadoId = pedimento; ComprobantePagoId = comprobante; FechaComprobacion = fecha; ComprobadoPor = usuario;
        foreach (var gasto in gastos) gasto.Desaplicar();
        foreach (var gasto in nuevos) gasto.AplicarA(Id);
        GastosAplicadosMxn = nuevos.Sum(g => g.ImporteMxn);
    }
    public void Comprobar(ImpuestosPedimento reales, Guid pedimento, Guid comprobante, DateOnly fecha, Guid? usuario = null)
    {
        if (SoporteCalculoId is not null && PagadoMxn != Anticipado)
            throw new BusinessRuleException("CE_ANTICIPO_SIN_PAGO", "Espere el pago completo de Tesorería antes de comprobar.");
        reales.Validar();
        if (Aplicado is not null) throw new BusinessRuleException("CE_ANTICIPO_COMPROBADO", "El anticipo ya fue comprobado.");
        if (pedimento == Guid.Empty || comprobante == Guid.Empty || fecha < Fecha)
            throw new BusinessRuleException("CE_EVIDENCIA_REQUERIDA", "Adjunte pedimento pagado y comprobante de pago con fecha válida.");
        RevisionComprobacion++;
        IvaAplicado = reales.Iva; IgiAplicado = reales.Igi; PrvAplicado = reales.Prv;
        ComprobadoPor = usuario;
        PedimentoPagadoId = pedimento; ComprobantePagoId = comprobante; FechaComprobacion = fecha;
    }
    public void AplicarGastos(IReadOnlyCollection<GastoExpediente> gastos)
    {
        if (gastos.Count == 0) return;
        if (CuentaPeca)
            throw new BusinessRuleException("CE_ANTICIPO_PECA_SOLO_IMPUESTOS", "La cuenta PECA sólo admite impuestos del pedimento.");
        if (Aplicado is null || Resoluciones.Count > 0)
            throw new BusinessRuleException("CE_COMPROBACION_INVALIDA", "Aplique los gastos al comprobar, antes de resolver el saldo.");
        if (gastos.Select(g => g.Id).Distinct().Count() != gastos.Count || gastos.Any(g => g.AnticipoAgenteId != null))
            throw new BusinessRuleException("CE_GASTO_YA_APLICADO", "Un gasto ya aplicado no puede aplicarse a otro anticipo.");
        if (gastos.Any(g => g.EmpresaId != EmpresaId || g.ExpedienteId != ExpedienteId || g.ProveedorId != AgenteId))
            throw new BusinessRuleException("CE_GASTO_AGENTE_INVALIDO", "Seleccione gastos del mismo expediente y agente.");
        if (gastos.Any(g => g.FacturaProveedorId is null))
            throw new BusinessRuleException("CE_GASTO_SIN_FACTURA", "Para aplicar un gasto al anticipo, ligue antes la factura del agente.");
        foreach (var gasto in gastos) gasto.AplicarA(Id);
        GastosAplicadosMxn += gastos.Sum(g => g.ImporteMxn);
    }
    public void Resolver(string tipo, decimal importe, DateOnly fecha, string referencia, Guid evidencia, Guid usuario, AnticipoAgente? destino = null, bool registroDevolucion = false)
    {
        if (DevolucionPendienteId is not null)
            throw new BusinessRuleException("CE_DEVOLUCION_PENDIENTE", "Espere el depósito de la devolución aprobada.");
        if (ComprobadoPor is null)
            throw new BusinessRuleException("CE_COMPROBADOR_NO_IDENTIFICADO", "La comprobación histórica no identifica a su responsable. Corríjala antes de aprobar el saldo.");
        if (!registroDevolucion && ComprobadoPor == usuario)
            throw new BusinessRuleException("CE_RESOLUCION_MISMA_PERSONA", "Quien comprueba no puede aprobar la resolución del saldo.");
        if (Saldo is null || Saldo == 0 || importe <= 0 || decimal.Round(importe, 2) != importe || importe > Math.Abs(Saldo.Value) ||
            evidencia == Guid.Empty || usuario == Guid.Empty || string.IsNullOrWhiteSpace(referencia) || referencia.Length > 200 || fecha < FechaComprobacion)
            throw new BusinessRuleException("CE_RESOLUCION_INVALIDA", "La resolución requiere saldo abierto, importe disponible, fecha, referencia y evidencia.");
        if (tipo == "compensar")
        {
            if (Saldo < 0 || destino is null || destino.Id == Id || destino.EmpresaId != EmpresaId || destino.AgenteId != AgenteId ||
                destino.Aplicado is not null || importe > destino.Anticipado - destino.CompensacionRecibida)
                throw new BusinessRuleException("CE_COMPENSACION_INVALIDA", "Seleccione otro anticipo sin comprobar del mismo agente, con importe disponible.");
            destino.CompensacionRecibida += importe;
        }
        else if (!(tipo is "devolucion" or "saldo-al-agente" && Saldo > 0) && !(tipo == "pago-complementario" && Saldo < 0))
            throw new BusinessRuleException("CE_RESOLUCION_INVALIDA", "Use devolución para saldo a favor o pago complementario para deuda con el agente.");
        Resoluciones = [.. Resoluciones, new(tipo, Saldo < 0 ? -importe : importe, fecha, referencia.Trim(), evidencia, destino?.Id, usuario)];
    }
}
