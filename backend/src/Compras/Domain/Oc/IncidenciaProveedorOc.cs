using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Incidencia del proveedor sobre una línea de la OC (F1-COM-07): faltante, dañado o atraso, con
/// responsable de seguimiento y fecha compromiso. Los compromisos son historial inmutable: reprogramar
/// agrega uno nuevo. Se cierra sola cuando una recepción deja la línea sin pendiente, o a mano con motivo.
/// </summary>
public sealed class IncidenciaProveedorOc : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid OrdenCompraId { get; private set; }
    public Guid AggregateRootId => OrdenCompraId;
    public Guid LineaOrdenCompraId { get; private set; }
    public int Numero { get; private set; }
    public TipoIncidenciaProveedorOc Tipo { get; private set; }
    public decimal Cantidad { get; private set; }
    public string? Descripcion { get; private set; }
    public Guid ResponsableId { get; private set; }
    public Guid RegistradaPorId { get; private set; }
    public DateTimeOffset RegistradaEn { get; private set; }
    public DateTimeOffset? CerradaEn { get; private set; }
    /// <summary>Usuario que la cerró; <c>null</c> si la cerró la recepción del pendiente.</summary>
    public Guid? CerradaPorId { get; private set; }
    public string? MotivoCierre { get; private set; }

    private readonly List<CompromisoIncidenciaOc> _compromisos = [];
    public IReadOnlyCollection<CompromisoIncidenciaOc> Compromisos => _compromisos.AsReadOnly();

    public bool Abierta => CerradaEn is null;
    public DateOnly FechaCompromisoVigente => _compromisos.MaxBy(c => c.Numero)!.FechaCompromiso;

    private IncidenciaProveedorOc() { }

    internal IncidenciaProveedorOc(Guid id, Guid ordenCompraId, Guid lineaId, int numero,
        TipoIncidenciaProveedorOc tipo, decimal cantidad, string? descripcion, Guid responsableId,
        DateOnly fechaCompromiso, Guid usuarioId, DateTimeOffset ahora) : base(id)
    {
        OrdenCompraId = ordenCompraId;
        LineaOrdenCompraId = lineaId;
        Numero = numero;
        Tipo = tipo;
        Cantidad = cantidad;
        Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        ResponsableId = responsableId;
        RegistradaPorId = usuarioId;
        RegistradaEn = ahora;
        AgregarCompromiso(fechaCompromiso, null, usuarioId, ahora);
    }

    internal void AgregarCompromiso(DateOnly fecha, string? motivo, Guid usuarioId, DateTimeOffset ahora) =>
        _compromisos.Add(new CompromisoIncidenciaOc(Guid.CreateVersion7(), Id, OrdenCompraId,
            _compromisos.Count + 1, fecha, motivo, usuarioId, ahora));

    internal void Cerrar(Guid? usuarioId, string motivo, DateTimeOffset ahora)
    {
        CerradaEn = ahora;
        CerradaPorId = usuarioId;
        MotivoCierre = motivo.Trim();
    }
}

/// <summary>Fecha compromiso de una incidencia (inmutable). La de mayor número es la vigente.</summary>
public sealed class CompromisoIncidenciaOc : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid IncidenciaProveedorOcId { get; private set; }
    public Guid OrdenCompraId { get; private set; }
    public Guid AggregateRootId => OrdenCompraId;
    public int Numero { get; private set; }
    public DateOnly FechaCompromiso { get; private set; }
    public string? Motivo { get; private set; }
    public Guid RegistradoPorId { get; private set; }
    public DateTimeOffset RegistradoEn { get; private set; }

    private CompromisoIncidenciaOc() { }

    internal CompromisoIncidenciaOc(Guid id, Guid incidenciaId, Guid ordenCompraId, int numero,
        DateOnly fechaCompromiso, string? motivo, Guid usuarioId, DateTimeOffset ahora) : base(id)
    {
        IncidenciaProveedorOcId = incidenciaId;
        OrdenCompraId = ordenCompraId;
        Numero = numero;
        FechaCompromiso = fechaCompromiso;
        Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        RegistradoPorId = usuarioId;
        RegistradoEn = ahora;
    }
}
