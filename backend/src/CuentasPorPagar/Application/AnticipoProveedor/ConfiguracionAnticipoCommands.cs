using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.CuentasPorPagar.Application.AnticipoProveedor;
public sealed record ConfigurarSerieAnticipoCommand(Guid ProveedorId, string Serie) : IRequest;
public sealed class ConfigurarSerieAnticipoValidator : AbstractValidator<ConfigurarSerieAnticipoCommand>
{
    public ConfigurarSerieAnticipoValidator() { RuleFor(c => c.ProveedorId).NotEmpty(); RuleFor(c => c.Serie).NotEmpty().MaximumLength(25); }
}
public sealed class ConfigurarSerieAnticipoHandler(CuentasPorPagarDbContext db, ICurrentEmpresaContext empresa, IProveedorReadPort proveedores)
    : IRequestHandler<ConfigurarSerieAnticipoCommand>
{
    public async Task Handle(ConfigurarSerieAnticipoCommand c, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId) throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "Selecciona una empresa.");
        if (await proveedores.ObtenerAsync(c.ProveedorId, cancellationToken) is null) throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO", "No se encontró el proveedor.");
        var config = await db.ConfiguracionesAnticipoProveedor.FirstOrDefaultAsync(x => x.ProveedorId == c.ProveedorId, cancellationToken);
        if (config is null) db.ConfiguracionesAnticipoProveedor.Add(new(empresaId, c.ProveedorId, c.Serie));
        else config.CambiarSerie(c.Serie);
        await db.SaveChangesAsync(cancellationToken);
    }
}
public sealed record ObtenerSerieAnticipoQuery(Guid ProveedorId) : IRequest<string>;
public sealed class ObtenerSerieAnticipoHandler(CuentasPorPagarDbContext db) : IRequestHandler<ObtenerSerieAnticipoQuery, string>
{
    public async Task<string> Handle(ObtenerSerieAnticipoQuery c, CancellationToken cancellationToken) =>
        await db.ConfiguracionesAnticipoProveedor.Where(x => x.ProveedorId == c.ProveedorId).Select(x => x.Serie).FirstOrDefaultAsync(cancellationToken) ?? "FANT";
}
