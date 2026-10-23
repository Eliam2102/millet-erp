using Millet.SharedKernel.Domain;
namespace Millet.Almacen.Domain.Movimientos;

/// <summary>Documentos de cada recepción: no se sobrescriben al recibir otra parcialidad.</summary>
public sealed class RecepcionImportacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid OrdenCompraId { get; private set; }
    public Guid RecepcionId { get; private set; }
    public string NumeroPedimento { get; private set; } = null!;
    public string NumeroBl { get; private set; } = null!;
    public string NumeroContenedor { get; private set; } = null!;
    public string Aduana { get; private set; } = null!;
    public string ClavePedimento { get; private set; } = null!;
    public List<RecepcionImportacionPartida> Partidas { get; private set; } = [];
    private RecepcionImportacion() { }
    public RecepcionImportacion(Guid empresa, Guid oc, string pedimento, string bl, string contenedor, string aduana, string clave,
        List<RecepcionImportacionPartida> partidas) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa; OrdenCompraId = oc; NumeroPedimento = pedimento; NumeroBl = bl;
        NumeroContenedor = contenedor; Aduana = aduana; ClavePedimento = clave; Partidas = partidas;
    }
    public void VincularRecepcion(Guid id) => RecepcionId = id;
}

public sealed class RecepcionImportacionPartida : BaseEntity, IAuditable
{
    public Guid LineaOcId { get; private set; }
    public string FraccionArancelaria { get; private set; } = null!;
    private RecepcionImportacionPartida() { }
    public RecepcionImportacionPartida(Guid linea, string fraccion) : base(Guid.CreateVersion7())
    { LineaOcId = linea; FraccionArancelaria = fraccion; }
}
