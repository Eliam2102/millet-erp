using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Corridas;

/// <summary>
/// Registro del archivo de layout de pago generado para una corrida (§4.5, T9.3, 86e3ke5ck).
/// Mantiene la trazabilidad de qué archivo se generó, qué pagos salieron en él y
/// controla las reimpresiones para no duplicar registros de salida.
/// </summary>
public sealed class ArchivoLayoutPago : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid CorridaId { get; private set; }
    public string Banco { get; private set; } = default!;
    public TipoPagoLayout TipoPago { get; private set; } = TipoPagoLayout.NacionalSpei;
    public string NombreArchivo { get; private set; } = default!;
    public string Contenido { get; private set; } = default!;
    public decimal Total { get; private set; }
    public int LineasValidas { get; private set; }
    public int LineasExcluidas { get; private set; }
    public int VecesDescargado { get; private set; } = 1;
    public Guid GeneradoPor { get; private set; }
    public DateTimeOffset GeneradoEn { get; private set; }
    public DateTimeOffset? UltimaDescargaEn { get; private set; }

    private readonly List<ArchivoLayoutPagoLinea> _lineas = [];
    public IReadOnlyCollection<ArchivoLayoutPagoLinea> Lineas => _lineas.AsReadOnly();

    private ArchivoLayoutPago() { }

    public static ArchivoLayoutPago Crear(
        Guid empresaId,
        Guid corridaId,
        string banco,
        TipoPagoLayout tipoPago,
        string nombreArchivo,
        string contenido,
        decimal total,
        int lineasExcluidas,
        Guid generadoPor,
        DateTimeOffset ahora,
        IEnumerable<ArchivoLayoutPagoLinea> lineas)
    {
        var archivo = new ArchivoLayoutPago
        {
            EmpresaId = empresaId,
            CorridaId = corridaId,
            Banco = banco.Trim().ToUpperInvariant(),
            TipoPago = tipoPago,
            NombreArchivo = nombreArchivo.Trim(),
            Contenido = contenido,
            Total = total,
            LineasExcluidas = lineasExcluidas,
            GeneradoPor = generadoPor,
            GeneradoEn = ahora,
            VecesDescargado = 1,
            UltimaDescargaEn = ahora,
        };
        archivo.Id = Guid.CreateVersion7();
        foreach (var l in lineas)
        {
            l.AsignarArchivo(archivo.Id);
            archivo._lineas.Add(l);
        }
        archivo.LineasValidas = archivo._lineas.Count;
        return archivo;
    }

    /// <summary>
    /// Marca el archivo como reimpreso sin crear un nuevo registro de salida (CA3, T9.3-a).
    /// </summary>
    public void RegistrarReimpresion(DateTimeOffset ahora)
    {
        VecesDescargado++;
        UltimaDescargaEn = ahora;
    }
}
