using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Application.Origen;

public sealed record AwOrigenEstado(string Origen, bool Permitido, bool DemoConfigurada,
    string OrigenClientesReal, string OrigenProductosReal, string? CambiadoPor, DateTimeOffset? CambiadoEn, int Version);

public interface IAwOrigenActivo
{
    Task<AwOrigenEstado> LeerAsync(CancellationToken cancellationToken);
}

public sealed class AwOrigenActivo(CompartidoDbContext db, IConfiguration configuration, IHostEnvironment environment) : IAwOrigenActivo
{
    public async Task<AwOrigenEstado> LeerAsync(CancellationToken cancellationToken)
    {
        var p = await db.ParametrosGlobales.AsNoTracking().SingleOrDefaultAsync(x => x.Clave == AwOrigenParametro.Clave, cancellationToken);
        var origen = p?.Valor ?? "Real";
        if (origen is not ("Real" or "Demo"))
            throw new BusinessRuleException("AW_ORIGEN_INVALIDO", "El origen de A+W guardado no es válido. Revise su configuración.");
        var cs = configuration.GetConnectionString("AwOrigenPgDb");
        var configurada = !string.IsNullOrWhiteSpace(cs) && !cs.StartsWith("@Microsoft.KeyVault", StringComparison.OrdinalIgnoreCase);
        return new(origen, configuration.GetValue<bool>("IntegracionesAw:OrigenDemo:Permitido") && !environment.IsProduction(), configurada,
            configuration["IntegracionesAw:Clientes:Origen"] ?? "Simulado",
            configuration["IntegracionesAw:Productos:Origen"] ?? "Simulado",
            p?.UpdatedBy == "seed" ? null : p?.UpdatedBy,
            p?.UpdatedBy == "seed" ? null : p?.UpdatedAt, p?.Version ?? 0);
    }

    public static void VerificarDemo(AwOrigenEstado estado)
    {
        if (!estado.Permitido)
            throw new BusinessRuleException("AW_DEMO_NO_PERMITIDO", "El origen de demo no está permitido en este ambiente");
        if (!estado.DemoConfigurada)
            throw new BusinessRuleException("AW_DEMO_NO_CONFIGURADA", "La copia de demo de A+W no está configurada en este ambiente");
    }
}

public sealed record ObtenerAwOrigenQuery : IRequest<AwOrigenEstado>;
public sealed class ObtenerAwOrigenHandler(IAwOrigenActivo origen) : IRequestHandler<ObtenerAwOrigenQuery, AwOrigenEstado>
{
    public Task<AwOrigenEstado> Handle(ObtenerAwOrigenQuery request, CancellationToken cancellationToken) => origen.LeerAsync(cancellationToken);
}

public sealed record CambiarAwOrigenCommand(string Origen, int? VersionEsperada = null) : IRequest<AwOrigenEstado>;
public sealed class CambiarAwOrigenValidator : AbstractValidator<CambiarAwOrigenCommand>
{
    public CambiarAwOrigenValidator() => RuleFor(x => x.Origen).Must(x => x is "Real" or "Demo")
        .WithMessage("El origen debe ser Real o Demo.");
}

public sealed class CambiarAwOrigenHandler(CompartidoDbContext db, IAwOrigenActivo origen)
    : IRequestHandler<CambiarAwOrigenCommand, AwOrigenEstado>
{
    public async Task<AwOrigenEstado> Handle(CambiarAwOrigenCommand request, CancellationToken cancellationToken)
    {
        var estado = await origen.LeerAsync(cancellationToken);
        if (!estado.Permitido)
            throw new BusinessRuleException("AW_DEMO_NO_PERMITIDO", "El origen de demo no está permitido en este ambiente");
        if (request.Origen is not ("Real" or "Demo"))
            throw new BusinessRuleException("AW_ORIGEN_INVALIDO", "El origen debe ser Real o Demo.");
        if (request.Origen == "Demo") AwOrigenActivo.VerificarDemo(estado);
        var p = await db.ParametrosGlobales.SingleAsync(x => x.Clave == AwOrigenParametro.Clave, cancellationToken);
        if (request.VersionEsperada is int v && p.Version != v)
            throw new ConcurrencyException(nameof(ParametroGlobal), p.Id);
        if (p.Valor != request.Origen)
        {
            p.ActualizarValor(request.Origen);
            // ParametroGlobal es IAuditable: AuditSaveChangesInterceptor guarda usuario y diff antes/después
            // en core.audit_log dentro de la MISMA transacción que este cambio.
            await db.SaveChangesAsync(cancellationToken);
        }
        return await origen.LeerAsync(cancellationToken);
    }
}
