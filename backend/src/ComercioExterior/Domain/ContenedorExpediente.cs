using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public enum EstadoContenedor { Activo = 1, Cancelado = 2 }
public sealed record DatosContenedor(string? Numero, string? Sello, string? Booking, string? BlGuia, string? Ruta,
    DateOnly? FechaCargaPrevista, decimal? PesoKg, int? Bultos);

public sealed class ContenedorExpediente : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public string? Numero { get; private set; }
    public string? Sello { get; private set; }
    public string? Booking { get; private set; }
    public string? BlGuia { get; private set; }
    public string? Ruta { get; private set; }
    public DateOnly? FechaCargaPrevista { get; private set; }
    public DateOnly? FechaEmbarqueReal { get; private set; }
    public decimal? PesoKg { get; private set; }
    public int? Bultos { get; private set; }
    public EstadoContenedor Estado { get; private set; } = EstadoContenedor.Activo;
    public string? MotivoCancelacion { get; private set; }
    private ContenedorExpediente() { }
    internal ContenedorExpediente(Guid id, Guid empresa, Guid expediente, DatosContenedor datos) : base(id)
    {
        EmpresaId = empresa;
        ExpedienteId = expediente;
        Editar(datos);
    }
    internal void ExigirActivo()
    {
        if (Estado != EstadoContenedor.Activo)
            throw new BusinessRuleException("CE_CONTENEDOR_CANCELADO", "El contenedor está cancelado; se conserva su histórico.");
    }
    internal void Editar(DatosContenedor datos)
    {
        ExigirActivo();
        if (new[] { datos.Numero, datos.Sello, datos.Booking, datos.BlGuia, datos.Ruta }.Any(x => x?.Length > 200)
            || datos.PesoKg is < 0 or >= 1_000_000_000_000m || (datos.PesoKg is decimal peso && decimal.Round(peso, 6) != peso) || datos.Bultos < 0)
            throw new BusinessRuleException("CE_CONTENEDOR_DATOS_INVALIDOS", "Referencias: máximo 200 caracteres; peso y bultos no negativos, peso con máximo seis decimales.");
        if (FechaEmbarqueReal is not null && (string.IsNullOrWhiteSpace(datos.Numero) || string.IsNullOrWhiteSpace(datos.BlGuia)))
            throw new BusinessRuleException("CE_EMBARQUE_DATOS_REQUERIDOS", "Un embarque real debe conservar número de contenedor y BL o guía.");
        Numero = Limpiar(datos.Numero); Sello = Limpiar(datos.Sello); Booking = Limpiar(datos.Booking);
        BlGuia = Limpiar(datos.BlGuia); Ruta = Limpiar(datos.Ruta);
        FechaCargaPrevista = datos.FechaCargaPrevista; PesoKg = datos.PesoKg; Bultos = datos.Bultos;
    }
    private static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    internal void Embarcar(DateOnly fecha, DateOnly hoy)
    {
        ExigirActivo();
        if (string.IsNullOrWhiteSpace(Numero) || string.IsNullOrWhiteSpace(BlGuia))
            throw new BusinessRuleException("CE_EMBARQUE_DATOS_REQUERIDOS", "Capture número de contenedor y BL o guía antes de embarcar.");
        if (fecha > hoy || FechaEmbarqueReal is not null)
            throw new BusinessRuleException("CE_FECHA_EMBARQUE_INVALIDA", "La fecha real no puede ser futura ni reemplazar un embarque registrado.");
        FechaEmbarqueReal = fecha;
    }
    internal void Cancelar(string motivo)
    {
        ExigirActivo();
        MotivoCancelacion = Expediente.MotivoValido(motivo);
        Estado = EstadoContenedor.Cancelado;
    }
}
