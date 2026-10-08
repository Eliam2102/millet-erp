using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Repp;

public enum EstadoReppPendiente { Pendiente, Emitido, Descartado }

public sealed class ReppPendiente : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; private set; }
    Guid IPerteneceAEmpresa.EmpresaId { get => EmpresaId; set => EmpresaId = value; }
    public Guid ClienteId { get; private set; }
    public Guid MovimientoBancarioId { get; private set; }
    public Guid CuentaBancariaId { get; private set; }
    public Guid? PropuestaId { get; private set; }
    public decimal Monto { get; private set; }
    public string Moneda { get; private set; } = "MXN";
    public DateOnly FechaValor { get; private set; }
    public DateOnly FechaLimite { get; private set; }
    public string? Referencia { get; private set; }
    public string FormaPago { get; private set; } = "03";
    public bool Revisado { get; private set; }
    public EstadoReppPendiente Estado { get; private set; }
    public Guid? ReciboPagoId { get; private set; }
    // Conserva el folio incluso si el PAC falla o deja un resultado ambiguo.
    public Guid? IntentoReciboPagoId { get; private set; }
    public string? UltimoErrorCodigo { get; private set; }
    public string? UltimoErrorMensaje { get; private set; }
    public string? MotivoDescarte { get; private set; }
    private readonly List<ReppPendienteFactura> _facturas = [];
    public IReadOnlyCollection<ReppPendienteFactura> Facturas => _facturas.AsReadOnly();

    private ReppPendiente() { }

    public ReppPendiente(Guid empresaId, Guid clienteId, Guid movimientoBancarioId,
        Guid cuentaBancariaId, Guid? propuestaId, decimal monto, string moneda,
        DateOnly fechaValor, string? referencia, IEnumerable<RelacionRepp> facturas)
        : base(Guid.CreateVersion7())
    {
        EmpresaId = empresaId;
        ClienteId = clienteId;
        MovimientoBancarioId = movimientoBancarioId;
        CuentaBancariaId = cuentaBancariaId;
        PropuestaId = propuestaId;
        Monto = monto;
        Moneda = moneda;
        FechaValor = fechaValor;
        FechaLimite = PlazoRepp.FechaLimite(fechaValor);
        Referencia = referencia;
        ReemplazarFacturas(facturas);
    }

    public void VerificarEditable()
    {
        if (Estado != EstadoReppPendiente.Pendiente)
            throw new BusinessRuleException("REPP_PENDIENTE_CERRADO", "El pendiente ya fue emitido o descartado.");
        if (UltimoErrorCodigo == "REPP_RESULTADO_INCIERTO")
            throw new BusinessRuleException("REPP_RESULTADO_INCIERTO",
                "Verifica el resultado con el PAC y concilia el intento antes de volver a emitir este pago.");
        if (IntentoReciboPagoId.HasValue)
            throw new BusinessRuleException("REPP_INTENTO_EXISTENTE",
                "Existe un intento de timbrado. Resuélvelo desde el comprobante antes de modificar la relación.");
    }

    public void ValidarRelacion(IReadOnlyCollection<RelacionRepp> facturas)
    {
        if (facturas.Count == 0 || facturas.Any(f => f.FacturaVentaId == Guid.Empty || f.Importe <= 0)
            || facturas.Select(f => f.FacturaVentaId).Distinct().Count() != facturas.Count)
            throw new BusinessRuleException("REPP_RELACION_INVALIDA", "Relaciona facturas distintas con importes mayores a cero.");
        if (facturas.Sum(f => f.Importe) != Monto)
            throw new BusinessRuleException("REPP_MONTO_DISTINTO", "La suma de la relación debe ser exactamente el monto confirmado.");
    }

    public void Revisar(string formaPago, IReadOnlyCollection<RelacionRepp> facturas)
    {
        VerificarEditable();
        ValidarRelacion(facturas);
        if (string.IsNullOrWhiteSpace(formaPago) || formaPago == "99")
            throw new BusinessRuleException("REPP_FORMA_PAGO_INVALIDA", "Selecciona la forma de pago real; no se admite 99 Por definir.");
        FormaPago = formaPago;
        Revisado = true;
        ReemplazarFacturas(facturas);
        UltimoErrorCodigo = null;
        UltimoErrorMensaje = null;
    }

    public void RegistrarIntento(ReciboPago recibo)
    {
        if (Estado != EstadoReppPendiente.Pendiente)
            throw new BusinessRuleException("REPP_PENDIENTE_CERRADO", "El pendiente ya fue resuelto.");
        IntentoReciboPagoId = recibo.Id;
        Revisado = true;
        if (recibo.Estado == Comprobantes.EstadoTimbrado.Timbrado)
        {
            Estado = EstadoReppPendiente.Emitido;
            ReciboPagoId = recibo.Id;
            UltimoErrorCodigo = null;
            UltimoErrorMensaje = null;
        }
        else
            RegistrarError(recibo.TimbradoErrorCodigo ?? "REPP_TIMBRADO_EN_PROCESO",
                recibo.TimbradoErrorMensaje ?? "El PAC aún no confirma el timbrado. Revisa el comprobante antes de reintentar.");
    }

    public void RegistrarError(string codigo, string mensaje)
    {
        UltimoErrorCodigo = codigo;
        UltimoErrorMensaje = mensaje;
    }

    public void LiberarIntentoDescartado()
    {
        IntentoReciboPagoId = null;
        RegistrarError("REPP_INTENTO_DESCARTADO", "El intento fue descartado. Revisa la relación antes de volver a emitir.");
    }

    public void Descartar(string motivo)
    {
        VerificarEditable();
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("REPP_MOTIVO_REQUERIDO", "Indica por qué este pago no requiere REP.");
        Estado = EstadoReppPendiente.Descartado;
        MotivoDescarte = motivo.Trim();
    }

    private void ReemplazarFacturas(IEnumerable<RelacionRepp> facturas)
    {
        _facturas.Clear();
        _facturas.AddRange(facturas.Select(f => new ReppPendienteFactura(Id, f.FacturaVentaId, f.Importe)));
    }
}

public sealed record RelacionRepp(Guid FacturaVentaId, decimal Importe);

public sealed class ReppPendienteFactura
{
    public Guid Id { get; private set; }
    public Guid ReppPendienteId { get; private set; }
    public Guid FacturaVentaId { get; private set; }
    public decimal Importe { get; private set; }
    private ReppPendienteFactura() { }
    internal ReppPendienteFactura(Guid pendienteId, Guid facturaId, decimal importe)
    {
        Id = Guid.CreateVersion7();
        ReppPendienteId = pendienteId;
        FacturaVentaId = facturaId;
        Importe = importe;
    }
}

public static class PlazoRepp
{
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
    public static DateOnly FechaLimite(DateOnly fechaValor) => new DateOnly(fechaValor.Year, fechaValor.Month, 5).AddMonths(1);
    public static DateOnly Hoy(DateTimeOffset ahora) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, Zona).DateTime);
    public static string Alerta(DateOnly fechaLimite, DateTimeOffset ahora)
    {
        var dias = fechaLimite.DayNumber - Hoy(ahora).DayNumber;
        return dias < 0 ? "Vencido" : dias <= 3 ? "Cerca del plazo" : "En plazo";
    }
}
