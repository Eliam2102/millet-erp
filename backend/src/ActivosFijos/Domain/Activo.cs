using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Domain;

public enum EstadoActivo { PorCapitalizar, Activo, DadoDeBaja }
public enum TipoImportacionActivo { No, Temporal, Definitivo }
public sealed record CustodiaActivo(Guid ResponsableId, Guid SucursalId, string Ubicacion, Guid CentroCostoId, Guid? EquipoId);
public sealed record CustodiaDescripcion(string? Responsable, string? Sucursal, string? CentroCosto, string? Equipo);
public sealed record DatosActivo(string Descripcion, Guid ClaseId, string? Marca, string? Modelo, string? NumeroSerie,
    DateOnly FechaAdquisicion, DateOnly? InicioUso, decimal ValorMonedaOriginal, string Moneda, decimal TipoCambio,
    CustodiaActivo Custodia, TipoImportacionActivo Importacion, string? Pedimento, bool Arrendado, string? DocumentoSoporte, Guid? ActivoMejoradoId = null, int? VidaUtilMeses = null);

public sealed partial class Activo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public string Numero { get; private set; } = "";
    public string Descripcion { get; private set; } = "";
    public Guid? ActivoMejoradoId { get; private set; }
    public int? VidaUtilMeses { get; private set; }
    public Guid ClaseId { get; private set; }
    public string? Marca { get; private set; }
    public string? Modelo { get; private set; }
    public string? NumeroSerie { get; private set; }
    public DateOnly FechaAdquisicion { get; private set; }
    public DateOnly? InicioUso { get; private set; }
    public decimal ValorOriginal { get; private set; }
    public decimal ValorMonedaOriginal { get; private set; }
    public string Moneda { get; private set; } = "MXN";
    public decimal TipoCambio { get; private set; }
    public decimal DepreciacionAcumulada { get; private set; }
    public decimal ValorActual { get; private set; }
    public decimal ValorEnLibros => ValorActual - DepreciacionAcumulada;
    public Guid ResponsableId { get; private set; }
    public Guid SucursalId { get; private set; }
    public string Ubicacion { get; private set; } = "";
    public Guid CentroCostoId { get; private set; }
    public Guid? EquipoId { get; private set; }
    public TipoImportacionActivo Importacion { get; private set; }
    public string? Pedimento { get; private set; }
    public bool Arrendado { get; private set; }
    public string? DocumentoSoporte { get; private set; }
    public Guid? OrdenCompraId { get; private set; }
    public Guid? LineaOcId { get; private set; }
    public Guid? RecepcionId { get; private set; }
    public int UnidadRecepcion { get; private set; }
    public Guid? LineaFacturaId { get; private set; }
    public Guid? FacturaProveedorId { get; private set; }
    public Guid? PolizaAltaId { get; private set; }
    public EstadoActivo Estado { get; private set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public CustodiaDescripcion? CustodiaDescripcion { get; set; }
    public DateTimeOffset? UltimaImpresionEn { get; private set; }
    public void RegistrarImpresion(DateTimeOffset ahora) => UltimaImpresionEn = ahora;
    public bool EnTraslado => Traslados.Any(x => x.AceptadoEn == null);
    public List<MovimientoValorActivo> Historial { get; private set; } = [];
    public List<TrasladoActivo> Traslados { get; private set; } = [];
    private Activo() { }
    public Activo(Guid id, Guid empresa, string numero, DatosActivo datos, Guid? oc = null, Guid? linea = null, Guid? recepcion = null, int unidad = 0) : base(id)
    {
        if (string.IsNullOrWhiteSpace(numero) || numero.Length > 60 || (oc is null && string.IsNullOrWhiteSpace(datos.DocumentoSoporte)))
            throw new BusinessRuleException("AF_SOPORTE_REQUERIDO", "El alta manual requiere número y documento de soporte.");
        EmpresaId = empresa; Numero = numero; OrdenCompraId = oc; LineaOcId = linea; RecepcionId = recepcion; UnidadRecepcion = unidad;
        Completar(datos);
    }
    public void Completar(DatosActivo d)
    {
        if (Estado != EstadoActivo.PorCapitalizar || EnTraslado)
            throw new BusinessRuleException("AF_NO_EDITABLE", "Solo puede completar activos por capitalizar sin traslado pendiente.");
        if (string.IsNullOrWhiteSpace(d.Descripcion) || d.Descripcion.Length > 500 || d.ClaseId == Guid.Empty || d.Custodia.SucursalId == Guid.Empty
            || (OrdenCompraId == null && string.IsNullOrWhiteSpace(d.DocumentoSoporte))
            || d.ValorMonedaOriginal < 0 || d.TipoCambio <= 0 || d.Moneda.Length != 3 || (d.Moneda == "MXN" && d.TipoCambio != 1)
            || !Enum.IsDefined(d.Importacion) || (d.Importacion != TipoImportacionActivo.No && string.IsNullOrWhiteSpace(d.Pedimento))
            || d.VidaUtilMeses is < 1 or > 600 || d.ActivoMejoradoId == Guid.Empty || d.ActivoMejoradoId == Id
            || d.FechaAdquisicion == default || d.InicioUso < d.FechaAdquisicion)
            throw new BusinessRuleException("AF_DATOS_INVALIDOS", "Revise descripción, clase, sucursal, fechas, valor, moneda, tipo de cambio y pedimento.");
        if (FacturaProveedorId != null && (ValorMonedaOriginal != d.ValorMonedaOriginal || Moneda != d.Moneda || TipoCambio != d.TipoCambio))
            throw new BusinessRuleException("AF_VALOR_FACTURA", "El valor conciliado se modifica desde la factura de proveedor.");
        ActivoMejoradoId = d.ActivoMejoradoId; VidaUtilMeses = d.VidaUtilMeses;
        Descripcion = d.Descripcion.Trim(); ClaseId = d.ClaseId; Marca = d.Marca; Modelo = d.Modelo; NumeroSerie = d.NumeroSerie;
        FechaAdquisicion = d.FechaAdquisicion; InicioUso = d.InicioUso; ValorMonedaOriginal = d.ValorMonedaOriginal;
        Moneda = d.Moneda; TipoCambio = d.TipoCambio; ValorOriginal = decimal.Round(d.ValorMonedaOriginal * d.TipoCambio, 2); ValorActual = ValorOriginal;
        AplicarCustodia(d.Custodia); Importacion = d.Importacion; Pedimento = d.Pedimento; Arrendado = d.Arrendado; DocumentoSoporte = d.DocumentoSoporte;
    }
    public void ConciliarFactura(Guid factura, decimal valor, string moneda, decimal tc, Guid? lineaFactura = null)
    {
        if (FacturaProveedorId == factura && ValorMonedaOriginal == valor && Moneda == moneda && TipoCambio == tc) return;
        if (Estado != EstadoActivo.PorCapitalizar || FacturaProveedorId != null || factura == Guid.Empty || valor <= 0 || tc <= 0 || moneda.Length != 3 || (moneda == "MXN" && tc != 1))
            throw new BusinessRuleException("AF_FACTURA_INVALIDA", "El activo debe estar por capitalizar y tener una factura válida sin conciliación previa.");
        FacturaProveedorId = factura; LineaFacturaId = lineaFactura; ValorMonedaOriginal = valor; Moneda = moneda; TipoCambio = tc;
        ValorOriginal = decimal.Round(valor * tc, 2); ValorActual = ValorOriginal;
    }
    public void ValidarCapitalizacion()
    {
        if (Estado != EstadoActivo.PorCapitalizar || InicioUso == null || ResponsableId == Guid.Empty || CentroCostoId == Guid.Empty || ValorOriginal <= 0 || (OrdenCompraId != null && FacturaProveedorId == null))
            throw new BusinessRuleException("AF_CAPITALIZACION_INCOMPLETA", "Complete inicio de uso, responsable, centro de costo y valor conciliado antes de capitalizar.");
    }
    public void Capitalizar(Guid poliza)
    {
        if (Estado == EstadoActivo.Activo && PolizaAltaId == poliza) return;
        ValidarCapitalizacion();
        if (poliza == Guid.Empty) throw new BusinessRuleException("AF_POLIZA_REQUERIDA", "La capitalización requiere póliza.");
        PolizaAltaId = poliza; Estado = EstadoActivo.Activo;
    }
    public void ValidarAjuste(Guid documento, Guid factura, decimal importe, bool credito, string descripcion)
    {
        if (Estado != EstadoActivo.Activo || documento == Guid.Empty || factura == Guid.Empty || importe <= 0 || string.IsNullOrWhiteSpace(descripcion)
            || (credito && (factura != FacturaProveedorId || importe > ValorEnLibros)))
            throw new BusinessRuleException("AF_AJUSTE_INVALIDO", "Revise importe, descripción y factura origen; la nota no puede exceder el valor en libros.");
    }
    public void Ajustar(Guid documento, Guid factura, DateOnly fecha, decimal importe, bool credito, string descripcion, Guid poliza)
    {
        if (Historial.SingleOrDefault(x => x.DocumentoId == documento) is { } previo)
        {
            if (previo.FacturaId != factura || previo.Importe != (credito ? -importe : importe) || previo.Fecha != fecha || previo.Descripcion != descripcion)
                throw new BusinessRuleException("AF_DOCUMENTO_YA_APLICADO", "El documento ya tiene un ajuste distinto en este activo.");
            return;
        }
        ValidarAjuste(documento, factura, importe, credito, descripcion);
        Historial.Add(new MovimientoValorActivo(Guid.CreateVersion7(), EmpresaId, Id, documento, factura, fecha, credito ? -importe : importe, descripcion, poliza));
        ValorActual += credito ? -importe : importe;
    }
    public void Trasladar(Guid id, CustodiaActivo destino, string motivo, DateOnly fechaEfectiva, Guid actor, DateTimeOffset ahora)
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, TimeZoneInfo.FindSystemTimeZoneById("America/Merida")).DateTime);
        if (Estado != EstadoActivo.Activo || destino.ResponsableId == Guid.Empty || destino.SucursalId == Guid.Empty
            || destino.CentroCostoId == Guid.Empty || destino.EquipoId == Guid.Empty || string.IsNullOrWhiteSpace(destino.Ubicacion)
            || destino == Custodia() || string.IsNullOrWhiteSpace(motivo) || motivo.Length > 300 || actor == Guid.Empty
            || fechaEfectiva > hoy || fechaEfectiva < (InicioUso ?? FechaAdquisicion) || fechaEfectiva < FechaAdquisicion
            || Traslados.Any(x => x.FechaEfectiva > fechaEfectiva))
            throw new BusinessRuleException("AF_TRASLADO_INVALIDO", "Revise estado activo, destino distinto y completo, motivo de hasta 300 caracteres y fecha efectiva entre el alta/último traslado y hoy.");
        Traslados.Add(new TrasladoActivo(id, EmpresaId, Id, Custodia(), destino, motivo.Trim(), fechaEfectiva, actor, ahora));
        AplicarCustodia(destino);
    }
    public CustodiaActivo Custodia() => new(ResponsableId, SucursalId, Ubicacion, CentroCostoId, EquipoId);
    private void AplicarCustodia(CustodiaActivo c) { ResponsableId = c.ResponsableId; SucursalId = c.SucursalId; Ubicacion = c.Ubicacion; CentroCostoId = c.CentroCostoId; EquipoId = c.EquipoId; }
}

public sealed class MovimientoValorActivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ActivoId { get; private set; }
    public Guid DocumentoId { get; private set; }
    public Guid FacturaId { get; private set; }
    public DateOnly Fecha { get; private set; }
    public decimal Importe { get; private set; }
    public string Descripcion { get; private set; } = "";
    public Guid PolizaId { get; private set; }
    private MovimientoValorActivo() { }
    internal MovimientoValorActivo(Guid id, Guid empresa, Guid activo, Guid documento, Guid factura, DateOnly fecha, decimal importe, string descripcion, Guid poliza) : base(id)
    { EmpresaId = empresa; ActivoId = activo; DocumentoId = documento; FacturaId = factura; Fecha = fecha; Importe = importe; Descripcion = descripcion; PolizaId = poliza; }
}
public sealed class TrasladoActivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ActivoId { get; private set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public CustodiaDescripcion? OrigenDescripcion { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public CustodiaDescripcion? DestinoDescripcion { get; set; }
    public CustodiaActivo Origen { get; private set; } = null!;
    public CustodiaActivo Destino { get; private set; } = null!;
    public Guid RegistradoPor { get; private set; }
    public DateTimeOffset RegistradoEn { get; private set; }
    public DateTimeOffset? AceptadoEn { get; private set; }
    public string Motivo { get; private set; } = "";
    public DateOnly FechaEfectiva { get; private set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? RegistradoPorNombre { get; set; }
    private TrasladoActivo() { }
    internal TrasladoActivo(Guid id, Guid empresa, Guid activo, CustodiaActivo origen, CustodiaActivo destino, string motivo, DateOnly fechaEfectiva, Guid actor, DateTimeOffset fecha) : base(id)
    { EmpresaId = empresa; ActivoId = activo; Origen = origen; Destino = destino; RegistradoPor = actor; RegistradoEn = fecha; AceptadoEn = fecha; Motivo = motivo; FechaEfectiva = fechaEfectiva; }
}
