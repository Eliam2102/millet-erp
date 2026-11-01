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
    public EstadoTransaccionTerminal Estado { get; private set; }
    public Guid? CobroMostradorId { get; private set; }
    public Guid? CobroFormaPagoId { get; private set; }
    public Guid? SucursalId { get; private set; }
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

    private LiquidacionTerminal() { }

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
}

/// <summary>Renglón leído del archivo, ya con tipos (antes de persistirlo).</summary>
public sealed record RenglonTerminal(
    string NoOperacion, DateTime FechaHora, decimal Importe, string? Moneda, string? Ultimos4, string? Autorizacion,
    string? Estatus, bool Aprobada, decimal? Comision, decimal? IvaComision, decimal? Neto, string? FolioLiquidacion);
