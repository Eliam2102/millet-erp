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
    public decimal Anticipado => Iva + Igi + Prv;
    public decimal? IvaAplicado { get; private set; }
    public decimal? IgiAplicado { get; private set; }
    public decimal? PrvAplicado { get; private set; }
    public Guid? PedimentoPagadoId { get; private set; }
    public Guid? ComprobantePagoId { get; private set; }
    public DateOnly? FechaComprobacion { get; private set; }
    public decimal? Aplicado => IvaAplicado + IgiAplicado + PrvAplicado + GastosAplicadosMxn;
    public decimal? Diferencia => Anticipado - Aplicado;
    public decimal? Saldo => Diferencia - Resoluciones.Sum(r => r.Importe);
    public string Estado => Aplicado is null ? "Sin comprobar" : Saldo > 0 ? "Saldo a favor abierto" : Saldo < 0 ? "Por pagar al agente" : Resoluciones.Any(r => r.Tipo == "saldo-al-agente") ? "Saldo trasladado a la cuenta del agente" : "Comprobado";
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
    public void Comprobar(ImpuestosPedimento reales, Guid pedimento, Guid comprobante, DateOnly fecha)
    {
        reales.Validar();
        if (Aplicado is not null) throw new BusinessRuleException("CE_ANTICIPO_COMPROBADO", "El anticipo ya fue comprobado.");
        if (pedimento == Guid.Empty || comprobante == Guid.Empty || fecha < Fecha)
            throw new BusinessRuleException("CE_EVIDENCIA_REQUERIDA", "Adjunte pedimento pagado y comprobante de pago con fecha válida.");
        IvaAplicado = reales.Iva; IgiAplicado = reales.Igi; PrvAplicado = reales.Prv;
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
        foreach (var gasto in gastos) gasto.AplicarA(Id);
        GastosAplicadosMxn += gastos.Sum(g => g.ImporteMxn);
    }
    public void Resolver(string tipo, decimal importe, DateOnly fecha, string referencia, Guid evidencia, Guid usuario, AnticipoAgente? destino = null)
    {
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
