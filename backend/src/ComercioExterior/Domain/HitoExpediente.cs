using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public sealed class HitoExpediente : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public static IReadOnlyList<(string Clave, string Nombre)> Catalogo { get; } = Array.AsReadOnly(new[]
    {
        ("solicitado", "Solicitado"), ("planeado", "Planeado"),
        ("booking-confirmado", "Booking confirmado"), ("contenedor-asignado", "Contenedor asignado"),
        ("documentacion-en-proceso", "Documentación en proceso"), ("documentacion-completa", "Documentación completa"),
        ("cargado", "Cargado"), ("despacho-aduanal", "Despacho aduanal"), ("embarcado", "Embarcado"),
        ("arribado", "Arribado"), ("liberado", "Liberado"), ("en-entrega", "En entrega"),
        ("entregado", "Entregado"), ("gastos-pendientes", "Gastos pendientes"), ("cerrado", "Cerrado")
    });

    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public string Clave { get; private set; } = "";
    public int Orden { get; private set; }
    public DateOnly? FechaCompromiso { get; private set; }
    public DateOnly? FechaReal { get; private set; }
    public Guid ResponsableUsuarioId { get; private set; }
    public Guid? EvidenciaAdjuntoId { get; private set; }
    public bool Omitido { get; private set; }
    public string? Motivo { get; private set; }
    public string? MotivoCambioCompromiso { get; private set; }

    private HitoExpediente() { }
    internal HitoExpediente(Guid empresa, Guid expediente, string clave, int orden, Guid responsable) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        ExpedienteId = expediente;
        Clave = clave;
        Orden = orden;
        ResponsableUsuarioId = responsable;
    }

    public bool EstaAtrasado(DateOnly hoy) => FechaCompromiso < hoy && FechaReal is null && !Omitido;

    internal void Comprometer(DateOnly fecha, Guid responsable, string? motivo)
    {
        ExigirPendiente();
        ExigirResponsable(responsable);
        var razon = FechaCompromiso is not null && FechaCompromiso != fecha ? Expediente.MotivoValido(motivo) : null;
        FechaCompromiso = fecha;
        ResponsableUsuarioId = responsable;
        if (razon is not null) MotivoCambioCompromiso = razon;
    }

    // PLATFORM-TODO(<CeCorregirHito>): una fecha real o una omisión no se puede corregir; por confirmar con Millet.
    internal void Completar(DateOnly fecha, Guid responsable, Guid? evidencia, string? motivo)
    {
        ExigirPendiente();
        ExigirResponsable(responsable);
        var razon = motivo is null ? null : Expediente.MotivoValido(motivo);
        FechaReal = fecha;
        ResponsableUsuarioId = responsable;
        EvidenciaAdjuntoId = evidencia;
        Motivo = razon;
    }

    internal void Omitir(string motivo, Guid responsable)
    {
        ExigirPendiente();
        ExigirResponsable(responsable);
        var razon = Expediente.MotivoValido(motivo);
        Omitido = true;
        Motivo = razon;
        ResponsableUsuarioId = responsable;
    }

    private static void ExigirResponsable(Guid responsable)
    {
        if (responsable == Guid.Empty)
            throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Seleccione el responsable del hito.");
    }
    private void ExigirPendiente()
    {
        if (FechaReal is not null || Omitido)
            throw new BusinessRuleException("CE_HITO_YA_RESUELTO", "El hito ya tiene fecha real o está omitido; su histórico se conserva.");
    }
}
