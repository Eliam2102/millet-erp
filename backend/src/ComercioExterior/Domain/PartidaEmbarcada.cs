using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public enum EstadoPartida { Vigente = 1, RequiereRevision = 2, Liberada = 3 }
public sealed class PartidaEmbarcada : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid ContenedorId { get; private set; }
    public Guid PedidoFacturableId { get; private set; }
    public int Posicion { get; private set; }
    public Guid ClienteId { get; private set; }
    public decimal Cantidad { get; private set; }
    public string Unidad { get; private set; } = "";
    public long? VersionOrigenAsignada { get; private set; }
    public EstadoPartida Estado { get; private set; } = EstadoPartida.Vigente;
    public Guid ConfirmadoPorId { get; private set; }
    private PartidaEmbarcada() { }
    internal PartidaEmbarcada(Guid empresa, Guid expediente, Guid contenedor, Guid pedido, int posicion, Guid cliente,
        decimal cantidad, string unidad, long? version, Guid actor) : base(Guid.CreateVersion7())
    {
        if (cantidad <= 0 || cantidad >= 1_000_000_000_000m || decimal.Round(cantidad, 6) != cantidad || posicion < 1 || cliente == Guid.Empty || pedido == Guid.Empty || actor == Guid.Empty || string.IsNullOrWhiteSpace(unidad) || unidad.Length > 20)
            throw new BusinessRuleException("CE_PARTIDA_DATOS_INVALIDOS", "Pedido, posición, cliente, unidad, responsable y cantidad positiva (hasta seis decimales) son obligatorios.");
        EmpresaId = empresa; ExpedienteId = expediente; ContenedorId = contenedor; PedidoFacturableId = pedido;
        Posicion = posicion; ClienteId = cliente; Cantidad = cantidad; Unidad = unidad;
        VersionOrigenAsignada = version; ConfirmadoPorId = actor;
    }
    public EstadoPartida EstadoActual(long? version) => Estado != EstadoPartida.Liberada && version != VersionOrigenAsignada ? EstadoPartida.RequiereRevision : Estado;
    internal void Liberar() => Estado = EstadoPartida.Liberada;
    internal void Revisar(long? version)
    {
        if (Estado != EstadoPartida.Liberada && version != VersionOrigenAsignada) Estado = EstadoPartida.RequiereRevision;
    }
    internal void Confirmar(long? version, string unidad, Guid actor)
    {
        if (actor == Guid.Empty) throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Seleccione quién confirma la partida.");
        VersionOrigenAsignada = version; Unidad = unidad; ConfirmadoPorId = actor; Estado = EstadoPartida.Vigente;
    }
}
