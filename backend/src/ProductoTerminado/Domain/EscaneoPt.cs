using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>Lectura de una etiqueta en estación y su resultado de procesamiento (bandeja de excepciones).</summary>
public sealed class EscaneoPt : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public string Origen { get; private set; } = "";
    public string Etiqueta { get; private set; } = "";
    public string NumeroPedido { get; private set; } = "";
    public int? Posicion { get; private set; }
    public int? UPos { get; private set; }
    public string? NumeroPiezaTexto { get; private set; }
    public int Estacion { get; private set; }
    public string? Area { get; private set; }
    /// <summary>Hora cruda de A+W (<c>bearb_zeit</c>), Kind Unspecified; forma parte de la llave.</summary>
    public DateTime RegistradoAt { get; private set; }
    public DateTimeOffset RegistradoUtc { get; private set; }
    /// <summary>Cuándo vio el ERP la fila por primera vez; base de la regla de 24 h de Pospuesto.</summary>
    public DateTimeOffset RecibidoAt { get; private set; }
    public string? ClaveSucursalAw { get; private set; }
    public Guid? SucursalId { get; private set; }
    public ResultadoEscaneo Resultado { get; private set; } = ResultadoEscaneo.Excepcion;
    public string? Motivo { get; private set; }
    public int Intentos { get; private set; }
    public Guid? PiezaId { get; private set; }
    public Guid? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoAt { get; private set; }
    public string? MotivoResolucion { get; private set; }

    public bool EstaAbierto =>
        Resultado is ResultadoEscaneo.Pospuesto or ResultadoEscaneo.Excepcion or ResultadoEscaneo.Reposicion
        && ResueltoAt is null;

    private EscaneoPt() { }

    private EscaneoPt(Guid empresa, string origen, string etiqueta, string numeroPedido, int? posicion, int? uPos, string? numeroPiezaTexto, int estacion, string? area, DateTime registradoAt, DateTimeOffset registradoUtc, string? claveSucursalAw, Guid? sucursalId, DateTimeOffset recibidoAt) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        Origen = origen;
        Etiqueta = etiqueta.Trim();
        NumeroPedido = numeroPedido.Trim();
        Posicion = posicion;
        UPos = uPos;
        NumeroPiezaTexto = numeroPiezaTexto?.Trim();
        Estacion = estacion;
        Area = area;
        RegistradoAt = registradoAt;
        RegistradoUtc = registradoUtc;
        ClaveSucursalAw = claveSucursalAw;
        SucursalId = sucursalId;
        RecibidoAt = recibidoAt;
    }

    public static EscaneoPt Registrar(Guid empresa, string origen, string etiqueta, string numeroPedido, int? posicion, int? uPos, string? numeroPiezaTexto, int estacion, string? area, DateTime registradoAt, DateTimeOffset registradoUtc, string? claveSucursalAw, Guid? sucursalId, DateTimeOffset recibidoAt)
        => new(empresa, origen, etiqueta, numeroPedido, posicion, uPos, numeroPiezaTexto, estacion, area, registradoAt, registradoUtc, claveSucursalAw, sucursalId, recibidoAt);

    public void AsignarSucursal(Guid sucursalId) => SucursalId = sucursalId;

    public void MarcarAlta(Guid piezaId)
    {
        Resultado = ResultadoEscaneo.Alta;
        Motivo = null;
        PiezaId = piezaId;
    }

    public void MarcarPospuesto(string motivo) { Resultado = ResultadoEscaneo.Pospuesto; Motivo = motivo; }

    public void MarcarExcepcion(string motivo) { Resultado = ResultadoEscaneo.Excepcion; Motivo = motivo; }

    public void MarcarReposicion() { Resultado = ResultadoEscaneo.Reposicion; Motivo = MotivosPt.Reposicion; }

    public void Intentar(int max) => Intentos = Math.Min(Intentos + 1, max);

    public void Resolver(Guid usuario, string? motivoResolucion, DateTimeOffset ahora)
    {
        if (ResueltoAt is not null)
            throw new ConflictException("PT_ESCANEO_YA_RESUELTO", "El escaneo ya fue resuelto.");
        ResueltoPor = usuario;
        ResueltoAt = ahora;
        MotivoResolucion = motivoResolucion;
    }
}
