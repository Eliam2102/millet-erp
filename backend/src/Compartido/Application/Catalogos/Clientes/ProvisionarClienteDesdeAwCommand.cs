using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
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
    private readonly CompartidoDbContext _db;

    public ProvisionarClienteDesdeAwHandler(CompartidoDbContext db) => _db = db;

    public async Task<ProvisionarClienteDesdeAwResponse> Handle(
        ProvisionarClienteDesdeAwCommand request, CancellationToken cancellationToken)
    {
        var existente = await _db.Clientes
            .FirstOrDefaultAsync(c => c.ReferenciaExterna == request.ReferenciaExterna, cancellationToken);
        if (existente is not null)
            return Respuesta(existente, creado: false);

        // Clave determinista desde la referencia A+W (numero_cliente):
        // legible, única (la referencia lo es) y ≤20 chars.
        var clave = $"AW-{request.ReferenciaExterna}";
        var cliente = new Cliente(
            id: Guid.CreateVersion7(),
            clave: clave.Length <= 20 ? clave : clave[..20],
            razonSocial: request.RazonSocial,
            origen: OrigenMaster.Aw,
            referenciaExterna: request.ReferenciaExterna,
            rfc: request.Rfc,
            // CP del domicilio en A+W como prefill del CP FISCAL: el operador
            // lo valida contra la constancia (el SAT exige coincidencia); el
            // gate real de timbrado sigue siendo TieneFiscalesCompletos, que
            // también exige régimen — y ese nunca viene de A+W.
            codigoPostalFiscal: request.CodigoPostalFiscal,
            usoCfdiDefault: request.UsoCfdiDefault,
            formaPagoDefault: request.FormaPagoDefault,
            metodoPagoDefault: request.MetodoPagoDefault,
            monedaDefault: request.MonedaDefault ?? "MXN",
            telefono: request.Telefono,
            // Receptor extranjero (CCE): país+domicilio vienen de vw_erp_cliente;
            // NumRegIdTrib es gap A+W (null hasta que lo agreguen a la vista).
            numRegIdTrib: request.NumRegIdTrib,
            paisResidencia: request.PaisResidencia,
            domicilioExtranjeroCalle: request.DomicilioExtranjeroCalle,
            domicilioExtranjeroEstado: request.DomicilioExtranjeroEstado,
            domicilioExtranjeroCodigoPostal: request.DomicilioExtranjeroCodigoPostal);

        _db.Clientes.Add(cliente);
        await _db.SaveChangesAsync(cancellationToken);

        return Respuesta(cliente, creado: true);
    }

    private static ProvisionarClienteDesdeAwResponse Respuesta(Cliente c, bool creado) => new(
        c.Id, c.Rfc, c.RazonSocial, c.RegimenFiscal, c.CodigoPostalFiscal,
        c.UsoCfdiDefault, c.FormaPagoDefault, c.MetodoPagoDefault,
        c.MonedaDefault, c.EsGenerico, creado);
}
