using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Cajas.Terminal;

public enum EstadoTransaccionTerminal : short { Emparejada = 1, SinEmparejar = 2, Rechazada = 3 }

/// <summary>
/// Carga de un archivo diario de la terminal de tarjetas (86e3vr1tf, Parte A;
/// Laura Cerón 26-oct: se registra cada cargo tal como viene del banco). Guarda
/// la huella del archivo y los conteos del resultado; los renglones viven en
/// <see cref="TransaccionTerminal"/> con su número de operación único.
/// </summary>
public sealed class CargaTerminal : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public string NombreArchivo { get; private set; } = default!;
    public string Huella { get; private set; } = default!;
    /// <summary>Día de las transacciones (el archivo trae día y mes sin año).</summary>
    public DateOnly FechaArchivo { get; private set; }
    public Guid CargadoPor { get; private set; }
    public DateTimeOffset CargadoEn { get; private set; }
    public int Renglones { get; private set; }
    public int Nuevos { get; private set; }
    public int Duplicados { get; private set; }
    public int Emparejados { get; private set; }
    public int SinEmparejar { get; private set; }
    public int Rechazados { get; private set; }
    public int Liquidaciones { get; private set; }

    private CargaTerminal() { }

    public static CargaTerminal Iniciar(Guid empresaId, string nombreArchivo, string huella, DateOnly fechaArchivo,
        Guid cargadoPor, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        EmpresaId = empresaId,
        NombreArchivo = nombreArchivo.Trim(),
        Huella = huella,
        FechaArchivo = fechaArchivo,
        CargadoPor = cargadoPor,
        CargadoEn = ahora,
    };

    public void Resumir(int renglones, int nuevos, int duplicados, int emparejados, int sinEmparejar, int rechazados, int liquidaciones)
    {
        Renglones = renglones; Nuevos = nuevos; Duplicados = duplicados; Emparejados = emparejados;
        SinEmparejar = sinEmparejar; Rechazados = rechazados; Liquidaciones = liquidaciones;
    }
}

/// <summary>
/// Renglón del archivo de la terminal: una venta con tarjeta tal como la reporta
/// el banco. Se empareja con la forma de pago 04/28 del cobro de caja por número
/// de autorización, importe y día. Comisión, IVA, neto y folio de liquidación
/// vienen del reporte de comisiones del banco (columnas a validar con Getnet).
/// </summary>
public sealed class TransaccionTerminal : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid CargaId { get; private set; }
    /// <summary>«No. Operación» del banco: llave para no duplicar al recargar.</summary>
    public string NoOperacion { get; private set; } = default!;
    public DateTime FechaHora { get; private set; }
    public decimal Importe { get; private set; }
    public string Moneda { get; private set; } = default!;
    public string? Ultimos4 { get; private set; }
    public string? Autorizacion { get; private set; }
    public string Estatus { get; private set; } = default!;
    public decimal? Comision { get; private set; }
    public decimal? IvaComision { get; private set; }
    public decimal? Neto { get; private set; }
    public string? FolioLiquidacion { get; private set; }
    /// <summary>Día en que el banco liquidó (GMR: <c>DAT_SETTLED</c>). Null = el de la venta.</summary>
    public DateOnly? FechaLiquidacion { get; private set; }
    public EstadoTransaccionTerminal Estado { get; private set; }
    public Guid? CobroMostradorId { get; private set; }
    public Guid? CobroFormaPagoId { get; private set; }
    public Guid? SucursalId { get; private set; }
    /// <summary>Afiliación del comercio y código de terminal que reporta el banco (statement); base del modelo terminal-afiliación-sucursal.</summary>
    public string? Afiliacion { get; private set; }
    public string? CodTerminal { get; private set; }
    public Guid? LiquidacionId { get; private set; }

    /// <summary>Trae lo que necesita la liquidación (comisión, IVA, neto y folio).</summary>
    public bool TraeLiquidacion => Comision is not null && IvaComision is not null && Neto is not null
        && !string.IsNullOrWhiteSpace(FolioLiquidacion);

    private TransaccionTerminal() { }

    public static TransaccionTerminal Registrar(Guid empresaId, Guid cargaId, RenglonTerminal r)
    {
        if (string.IsNullOrWhiteSpace(r.NoOperacion))
            throw new BusinessRuleException("TERMINAL_SIN_OPERACION", "Cada renglón de la terminal lleva su número de operación.");
        return new TransaccionTerminal
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            CargaId = cargaId,
            NoOperacion = r.NoOperacion.Trim(),
            FechaHora = r.FechaHora,
            Importe = r.Importe,
            Moneda = string.IsNullOrWhiteSpace(r.Moneda) ? "MXN" : r.Moneda.Trim().ToUpperInvariant(),
            Ultimos4 = Limpio(r.Ultimos4),
            Autorizacion = Limpio(r.Autorizacion),
            Estatus = (Limpio(r.Estatus) ?? "").Trim(),
            Comision = r.Comision,
            IvaComision = r.IvaComision,
            Neto = r.Neto,
            FolioLiquidacion = Limpio(r.FolioLiquidacion),
            FechaLiquidacion = r.FechaLiquidacion,
            Afiliacion = Limpio(r.Afiliacion),
            CodTerminal = Limpio(r.CodTerminal),
            Estado = r.Aprobada ? EstadoTransaccionTerminal.SinEmparejar : EstadoTransaccionTerminal.Rechazada,
        };
    }

    public void Emparejar(Guid cobroMostradorId, Guid cobroFormaPagoId, Guid sucursalId)
    {
        if (Estado == EstadoTransaccionTerminal.Rechazada)
            throw new BusinessRuleException("TERMINAL_RECHAZADA", "Una transacción rechazada por el banco no se empareja.");
        Estado = EstadoTransaccionTerminal.Emparejada;
        CobroMostradorId = cobroMostradorId;
        CobroFormaPagoId = cobroFormaPagoId;
        SucursalId = sucursalId;
    }

    public void LigarLiquidacion(Guid liquidacionId) => LiquidacionId = liquidacionId;

    private static string? Limpio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>
/// Liquidación del banco por folio: suma de las ventas emparejadas que el banco
/// liquidó juntas. Origina el evento contable (bancos neto, comisión 601.75 e IVA
/// pendiente contra «COBROS T/C, T/D» por el bruto). Una sola vez por folio.
/// </summary>
public sealed class LiquidacionTerminal : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public string Folio { get; private set; } = default!;
    public DateOnly Fecha { get; private set; }
    public decimal Bruto { get; private set; }
    public decimal Comision { get; private set; }
    public decimal IvaComision { get; private set; }
    public decimal Neto { get; private set; }
    public int Transacciones { get; private set; }
    public Guid CargaId { get; private set; }
    /// <summary>Factura mensual del banco que ampara este cargo (86e3xt2q8, Parte B). Null = IVA pendiente de pago.</summary>
    public Guid? FacturaBancoId { get; private set; }
    /// <summary>Folio fiscal (UUID) de la factura del banco ligada.</summary>
    public string? UuidFacturaBanco { get; private set; }

    private LiquidacionTerminal() { }

    public void LigarFacturaBanco(Guid facturaBancoId, string uuid)
    {
        if (FacturaBancoId is not null)
            throw new BusinessRuleException("TERMINAL_CARGO_YA_FACTURADO",
                $"El cargo de la liquidación {Folio} ya está ligado a una factura del banco.");
        FacturaBancoId = facturaBancoId;
        UuidFacturaBanco = uuid;
    }

    public static LiquidacionTerminal Crear(Guid empresaId, Guid cargaId, string folio, DateOnly fecha,
        IReadOnlyCollection<TransaccionTerminal> transacciones)
    {
        if (transacciones.Count == 0 || transacciones.Any(t => t.Estado != EstadoTransaccionTerminal.Emparejada || !t.TraeLiquidacion))
            throw new BusinessRuleException("TERMINAL_LIQUIDACION_INCOMPLETA",
                "La liquidación solo se forma con ventas emparejadas que traen comisión, IVA, neto y folio.");
        var l = new LiquidacionTerminal
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            CargaId = cargaId,
            Folio = folio.Trim(),
            Fecha = fecha,
            Bruto = transacciones.Sum(t => t.Importe),
            Comision = transacciones.Sum(t => t.Comision!.Value),
            IvaComision = transacciones.Sum(t => t.IvaComision!.Value),
            Neto = transacciones.Sum(t => t.Neto!.Value),
            Transacciones = transacciones.Count,
        };
        foreach (var t in transacciones) t.LigarLiquidacion(l.Id);
        return l;
    }

    /// <summary>
    /// Suma a esta liquidación las ventas del mismo folio emparejadas después de crearla (B13: una venta que se
    /// empareja con «Reintentar» cuando el folio ya se había liquidado). Idempotente: las ventas ya ligadas a esta
    /// liquidación no se vuelven a sumar. Devuelve las que sí se agregaron. No se toca un cargo ya ligado a la
    /// factura mensual del banco, porque esa factura se cuadró con el importe anterior.
    /// </summary>
    public IReadOnlyList<TransaccionTerminal> Agregar(IReadOnlyCollection<TransaccionTerminal> transacciones)
    {
        var nuevas = transacciones.Where(t => t.LiquidacionId != Id).ToList();
        if (nuevas.Count == 0) return nuevas;
        if (FacturaBancoId is not null)
            throw new BusinessRuleException("TERMINAL_CARGO_YA_FACTURADO",
                $"El cargo de la liquidación {Folio} ya está ligado a una factura del banco.");
        if (nuevas.Any(t => t.Estado != EstadoTransaccionTerminal.Emparejada || !t.TraeLiquidacion
                || t.LiquidacionId is not null || !string.Equals(t.FolioLiquidacion, Folio, StringComparison.Ordinal)))
            throw new BusinessRuleException("TERMINAL_LIQUIDACION_INCOMPLETA",
                "Solo se agregan ventas emparejadas del mismo folio que traen comisión, IVA y neto y no tienen otra liquidación.");
        Bruto += nuevas.Sum(t => t.Importe);
        Comision += nuevas.Sum(t => t.Comision!.Value);
        IvaComision += nuevas.Sum(t => t.IvaComision!.Value);
        Neto += nuevas.Sum(t => t.Neto!.Value);
        Transacciones += nuevas.Count;
        // Fecha del cargo = día en que el banco liquidó el folio; una venta sin DAT_SETTLED no la mueve.
        var fecha = nuevas.Where(t => t.FechaLiquidacion is not null).Select(t => t.FechaLiquidacion!.Value).DefaultIfEmpty(Fecha).Max();
        if (fecha > Fecha) Fecha = fecha;
        foreach (var t in nuevas) t.LigarLiquidacion(Id);
        return nuevas;
    }
}

/// <summary>
/// Factura mensual del banco por las comisiones de la terminal (86e3xt2q8, Parte B;
/// definición del negocio del 26-oct: cada cargo diario queda ligado a la factura mensual). Se
/// liga solo si la comisión, el IVA y el total del mes cuadran con el CFDI recibido con
/// una diferencia de hasta $0.01 a favor o en contra (Tesorería, 4-nov; 86e3zcbjx); si no,
/// no se liga nada. <see cref="ComisionFactura"/> e <see cref="IvaFactura"/> son los del CFDI
/// (lo que se deduce y se acredita); <see cref="Redondeo"/> es lo que el banco cargó de más
/// (positivo) o de menos (negativo) contra el CFDI y va a redondeo en la póliza.
/// </summary>
public sealed class FacturaBancoTerminal : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    /// <summary>Id del CFDI en el buzón de CxP (referencia, sin FK entre esquemas).</summary>
    public Guid CfdiRecibidoId { get; private set; }
    public string UuidCfdi { get; private set; } = default!;
    public string RfcEmisor { get; private set; } = default!;
    public int Anio { get; private set; }
    public int Mes { get; private set; }
    public decimal Comision { get; private set; }
    public decimal IvaComision { get; private set; }
    public decimal Total { get; private set; }
    /// <summary>Subtotal del CFDI del banco (comisión facturada).</summary>
    public decimal ComisionFactura { get; private set; }
    /// <summary>IVA trasladado del CFDI del banco (IVA acreditable pagado).</summary>
    public decimal IvaFactura { get; private set; }
    /// <summary>Cargos − (comisión + IVA del CFDI); a lo más $0.01 en valor absoluto.</summary>
    public decimal Redondeo { get; private set; }
    public int Cargos { get; private set; }
    public Guid LigadaPor { get; private set; }
    public DateTimeOffset LigadaEn { get; private set; }

    private FacturaBancoTerminal() { }

    public static FacturaBancoTerminal Ligar(Guid empresaId, CuadreFacturaBanco cuadre, Guid cfdiRecibidoId, string uuid,
        string rfcEmisor, IReadOnlyCollection<LiquidacionTerminal> cargos, Guid usuarioId, DateTimeOffset ahora)
    {
        if (cargos.Count == 0)
            throw new BusinessRuleException("TERMINAL_MES_SIN_CARGOS", "No hay cargos de la terminal sin factura en ese mes.");
        if (!cuadre.Cuadra)
            throw new BusinessRuleException("TERMINAL_FACTURA_NO_CUADRA",
                $"La factura del banco no cuadra con los cargos del mes: diferencia de {cuadre.DiferenciaTotal:N2} " +
                $"(comisión {cuadre.DiferenciaComision:N2}, IVA {cuadre.DiferenciaIva:N2}); se acepta hasta " +
                $"{CuadreFacturaBanco.Tolerancia:N2}. No se ligó nada.");
        var f = new FacturaBancoTerminal
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            CfdiRecibidoId = cfdiRecibidoId,
            UuidCfdi = uuid.Trim().ToUpperInvariant(),
            RfcEmisor = rfcEmisor.Trim().ToUpperInvariant(),
            Anio = cuadre.Anio,
            Mes = cuadre.Mes,
            Comision = cuadre.ComisionCargos,
            IvaComision = cuadre.IvaCargos,
            Total = cuadre.TotalCargos,
            ComisionFactura = cuadre.SubtotalFactura,
            IvaFactura = cuadre.IvaFactura,
            Redondeo = cuadre.Redondeo,
            Cargos = cargos.Count,
            LigadaPor = usuarioId,
            LigadaEn = ahora,
        };
        foreach (var c in cargos) c.LigarFacturaBanco(f.Id, f.UuidCfdi);
        return f;
    }
}

/// <summary>
/// Comparación de los cargos del mes (comisión + IVA) contra la factura del banco
/// (subtotal + IVA trasladado). Diferencia = factura − cargos. Cuadra si ninguna de las
/// tres diferencias pasa de <see cref="Tolerancia"/> en valor absoluto (86e3zcbjx: $0.01 a
/// favor o en contra; $0.02 ya no cuadra).
/// </summary>
public sealed record CuadreFacturaBanco(
    int Anio, int Mes, int Cargos, decimal ComisionCargos, decimal IvaCargos,
    decimal SubtotalFactura, decimal IvaFactura, decimal TotalFactura)
{
    /// <summary>Diferencia aceptada por Tesorería (4-nov): un centavo a favor o en contra.</summary>
    public const decimal Tolerancia = 0.01m;

    public decimal TotalCargos => ComisionCargos + IvaCargos;
    public decimal DiferenciaComision => SubtotalFactura - ComisionCargos;
    public decimal DiferenciaIva => IvaFactura - IvaCargos;
    public decimal DiferenciaTotal => TotalFactura - TotalCargos;
    /// <summary>
    /// Lo que el banco cargó de más (positivo) o de menos (negativo) contra la comisión y el IVA del CFDI; va a
    /// redondeo para que la póliza cuadre contra lo que salió del banco.
    /// </summary>
    public decimal Redondeo => TotalCargos - SubtotalFactura - IvaFactura;
    public bool Cuadra => Cargos > 0 && Math.Abs(DiferenciaComision) <= Tolerancia && Math.Abs(DiferenciaIva) <= Tolerancia
        && Math.Abs(DiferenciaTotal) <= Tolerancia && Math.Abs(Redondeo) <= Tolerancia;

    public static CuadreFacturaBanco Calcular(int anio, int mes, IReadOnlyCollection<LiquidacionTerminal> cargos,
        decimal subtotalFactura, decimal ivaFactura, decimal totalFactura) =>
        new(anio, mes, cargos.Count, cargos.Sum(c => c.Comision), cargos.Sum(c => c.IvaComision),
            subtotalFactura, ivaFactura, totalFactura);
}

/// <summary>Renglón leído del archivo, ya con tipos (antes de persistirlo).</summary>
public sealed record RenglonTerminal(
    string NoOperacion, DateTime FechaHora, decimal Importe, string? Moneda, string? Ultimos4, string? Autorizacion,
    string? Estatus, bool Aprobada, decimal? Comision, decimal? IvaComision, decimal? Neto, string? FolioLiquidacion,
    DateOnly? FechaLiquidacion = null, string? Afiliacion = null, string? CodTerminal = null);
