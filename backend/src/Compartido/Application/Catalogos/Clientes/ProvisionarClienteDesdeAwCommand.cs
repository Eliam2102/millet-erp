using FluentValidation;
using MediatR;
using Millet.DatosMaestros.Domain;

namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>
/// Auto-provisión de cliente desde A+W (ADR-0048 D6; cierra la mitad
/// DatosMaestros de PLATFORM-TODO(&lt;MasterProvisioningAw&gt;)). <b>Upsert
/// idempotente por <see cref="Cliente.ReferenciaExterna"/></b>: si ya existe
/// devuelve el existente sin tocar lo que el operador haya completado a
/// mano; si no, lo crea con <see cref="OrigenMaster.Aw"/> y los datos que la
/// vista/pedido aportan. Datos fiscales incompletos NO bloquean (bloquean
/// timbrado). El caller es el bridge <c>AwMasterProvisioningAdapter</c>
/// (Integraciones.Aw) — Compartido no conoce Integraciones.Aw ni
/// Facturación, por eso el command recibe valores planos.
/// </summary>
public sealed record ProvisionarClienteDesdeAwCommand(
    string ReferenciaExterna,
    string RazonSocial,
    string? Rfc,
    string? Telefono,
    string? CodigoPostalFiscal,
    string? UsoCfdiDefault,
    string? FormaPagoDefault,
    string? MetodoPagoDefault,
    string? MonedaDefault,
    string? NumRegIdTrib = null,
    string? PaisResidencia = null,
    string? DomicilioExtranjeroCalle = null,
    string? DomicilioExtranjeroEstado = null,
    string? DomicilioExtranjeroCodigoPostal = null) : IRequest<ProvisionarClienteDesdeAwResponse>;

public sealed record ProvisionarClienteDesdeAwResponse(
    Guid ClienteId,
    string? Rfc,
    string RazonSocial,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string? UsoCfdiDefault,
    string? FormaPagoDefault,
    string? MetodoPagoDefault,
    string MonedaDefault,
    bool EsGenerico,
    bool Creado);

public sealed class ProvisionarClienteDesdeAwValidator
    : AbstractValidator<ProvisionarClienteDesdeAwCommand>
{
    public ProvisionarClienteDesdeAwValidator()
    {
        RuleFor(c => c.ReferenciaExterna).NotEmpty().MaximumLength(50);
        RuleFor(c => c.RazonSocial).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Rfc!).Length(12, 13).When(c => c.Rfc is not null);
        RuleFor(c => c.CodigoPostalFiscal!).Matches(@"^\d{5}$").When(c => c.CodigoPostalFiscal is not null);
        RuleFor(c => c.MonedaDefault!).Length(3).When(c => c.MonedaDefault is not null);
    }
}

public sealed class ProvisionarClienteDesdeAwHandler
    : IRequestHandler<ProvisionarClienteDesdeAwCommand, ProvisionarClienteDesdeAwResponse>
{
    private readonly AplicarClienteAwService _aplicar;

    public ProvisionarClienteDesdeAwHandler(AplicarClienteAwService aplicar) => _aplicar = aplicar;

    public async Task<ProvisionarClienteDesdeAwResponse> Handle(
        ProvisionarClienteDesdeAwCommand request, CancellationToken cancellationToken)
    {
        // Existente: se devuelve sin tocar (SoloCrear). Nuevo: Cliente Origen=Aw +
        // registro de origen con datos mínimos, en una transacción. CP del
        // domicilio en A+W como prefill del CP FISCAL (el operador lo valida
        // contra la constancia). La validación fiscal completa (régimen, etc.)
        // ocurre en la emisión y NO se ha verificado contra el master.
        var r = await _aplicar.AplicarAsync(new AplicarClienteAwSnapshot(
            request.ReferenciaExterna, request.RazonSocial, DateTime.UtcNow,
            VersionContrato: "1", VersionMapeo: "0-borrador",
            Rfc: request.Rfc,
            CodigoPostalFiscal: request.CodigoPostalFiscal,
            UsoCfdiDefault: request.UsoCfdiDefault,
            FormaPagoDefault: request.FormaPagoDefault,
            MetodoPagoDefault: request.MetodoPagoDefault,
            MonedaDefault: request.MonedaDefault,
            NumRegIdTrib: request.NumRegIdTrib,
            PaisResidencia: request.PaisResidencia,
            DomicilioExtranjeroCalle: request.DomicilioExtranjeroCalle,
            DomicilioExtranjeroEstado: request.DomicilioExtranjeroEstado,
            DomicilioExtranjeroCodigoPostal: request.DomicilioExtranjeroCodigoPostal,
            Telefono: request.Telefono,
            SoloCrear: true), cancellationToken);

        // SoloCrear nunca devuelve NoCreado: Cliente siempre viene.
        return Respuesta(r.Cliente!, creado: r.Accion == AplicarClienteAwAccion.Creado);
    }

    private static ProvisionarClienteDesdeAwResponse Respuesta(Cliente c, bool creado) => new(
        c.Id, c.Rfc, c.RazonSocial, c.RegimenFiscal, c.CodigoPostalFiscal,
        c.UsoCfdiDefault, c.FormaPagoDefault, c.MetodoPagoDefault,
        c.MonedaDefault, c.EsGenerico, creado);
}
