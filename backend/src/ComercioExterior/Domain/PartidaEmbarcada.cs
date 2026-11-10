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
    public string? FraccionArancelaria { get; private set; }
    public string? UnidadAduana { get; private set; }
    public Guid? AduanalesRevisadoPorId { get; private set; }
    public DateTimeOffset? AduanalesRevisadoEn { get; private set; }
    public decimal? PesoKg { get; private set; }
    public int? Bultos { get; private set; }
    public decimal Cantidad { get; private set; }
    public string Unidad { get; private set; } = "";
    public long? VersionOrigenAsignada { get; private set; }
    public EstadoPartida Estado { get; private set; } = EstadoPartida.Vigente;
    public Guid ConfirmadoPorId { get; private set; }
    private PartidaEmbarcada() { }
    internal PartidaEmbarcada(Guid empresa, Guid expediente, Guid contenedor, Guid pedido, int posicion, Guid cliente,
        decimal cantidad, string unidad, long? version, Guid actor, bool propuesta = false) : base(Guid.CreateVersion7())
    {
        if (cantidad <= 0 || cantidad >= 1_000_000_000_000m || decimal.Round(cantidad, 6) != cantidad || posicion < 1 || cliente == Guid.Empty || pedido == Guid.Empty || (actor == Guid.Empty && !propuesta) || string.IsNullOrWhiteSpace(unidad) || unidad.Length > 20)
            throw new BusinessRuleException("CE_PARTIDA_DATOS_INVALIDOS", "Pedido, posición, cliente, unidad, responsable y cantidad positiva (hasta seis decimales) son obligatorios.");
        EmpresaId = empresa; ExpedienteId = expediente; ContenedorId = contenedor; PedidoFacturableId = pedido;
        Posicion = posicion; ClienteId = cliente; Cantidad = cantidad; Unidad = unidad;
        VersionOrigenAsignada = version; ConfirmadoPorId = propuesta ? Guid.Empty : actor;
        if (propuesta) Estado = EstadoPartida.RequiereRevision;
    }
    internal void GuardarDatosAduanales(string fraccion, string unidad, Guid actor, DateTimeOffset fecha, bool revisar)
    {
        if (string.IsNullOrWhiteSpace(fraccion) || fraccion.Length > 20 || string.IsNullOrWhiteSpace(unidad) || unidad.Length > 20 || actor == Guid.Empty)
            throw new BusinessRuleException("CE_DATOS_ADUANALES_INVALIDOS", "Capture fracción arancelaria y unidad aduana (máximo 20 caracteres) e identifique a quien los revisa.");
        // M-158: al revisarla, la fracción queda lista para el CCE 2.0 (10 dígitos); el borrador puede guardarse incompleto.
        if (revisar && unidad.Trim() != "99" && !Millet.SharedKernel.Domain.FraccionArancelariaExportacion.EsValida(fraccion))
            throw new BusinessRuleException(Millet.SharedKernel.Domain.FraccionArancelariaExportacion.Codigo,
                $"Partida {Posicion}: {Millet.SharedKernel.Domain.FraccionArancelariaExportacion.Mensaje}");
        FraccionArancelaria = fraccion.Trim(); UnidadAduana = unidad.Trim();
        AduanalesRevisadoPorId = revisar ? actor : null; AduanalesRevisadoEn = revisar ? fecha : null;
    }
    internal void AjustarCantidad(decimal nueva) => Cantidad = nueva;
    internal void GuardarPacking(decimal pesoKg, int bultos)
    {
        if (pesoKg < 0 || pesoKg >= 1_000_000_000_000m || decimal.Round(pesoKg, 6) != pesoKg || bultos < 0)
            throw new BusinessRuleException("CE_PACKING_INVALIDO", "Capture peso y bultos no negativos; el peso admite hasta seis decimales.");
        PesoKg = pesoKg; Bultos = bultos;
    }
    public EstadoPartida EstadoActual(long? version) => Estado != EstadoPartida.Liberada && version != VersionOrigenAsignada ? EstadoPartida.RequiereRevision : Estado;
    internal void RequerirRevision() { if (Estado != EstadoPartida.Liberada) Estado = EstadoPartida.RequiereRevision; }
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
