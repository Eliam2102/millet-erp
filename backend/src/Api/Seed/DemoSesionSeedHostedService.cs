using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Api.Seed;

/// <summary>
/// Composición de datos ficticios de la sesión 12-oct. Usa los agregados y
/// contextos propietarios; no ejecuta SQL de negocio ni configura PAC/CSD o A+W.
/// Cada etapa confirma sus datos y puede reanudarse por clave después de un fallo.
/// </summary>
public sealed partial class DemoSesionSeedHostedService(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<DemoSesionSeedHostedService> logger) : IHostedService
{
    public static readonly Guid EmpresaId = CompartidoDbContext.EmpresaBootstrapId;
    // Actor técnico de auditoría, no usuario ni identidad de Entra. No se crea una cuenta.
    public static readonly Guid ActorId = Id("DEMO-SEMBRADOR");
    private static readonly DateTimeOffset Fecha = new(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
    private const long SeedLockId = 6_672_000_012;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("Seed:DemoSesion:Habilitado"))
            return;

        var usuarios = configuration.GetSection("Seed:DemoSesion:Usuarios").Get<DemoSesionUsuario[]>() ?? [];
        foreach (var usuario in usuarios)
            await new DemoSesionUsuarioValidator().ValidateAndThrowAsync(usuario, cancellationToken);
        if (usuarios.Select(u => u.Correo.Trim().ToUpperInvariant()).Distinct().Count() != usuarios.Length)
            throw new InvalidOperationException("DEMO: cada correo debe aparecer una sola vez en Usuarios.");

        // El contexto del candado NO escribe: las etapas usan otro scope y confirman
        // independientemente (incluidas las FK hacia Compartido). Serializa reinicios
        // concurrentes sin mantener sin confirmar la empresa que requieren otros módulos.
        using var lockScope = scopes.CreateScope();
        await PostgresAdvisoryLock.ExecuteAsync(
            lockScope.ServiceProvider.GetRequiredService<CompartidoDbContext>(), SeedLockId,
            async ct =>
            {
                using var scope = scopes.CreateScope();
                var sp = scope.ServiceProvider;
                using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(DemoSesionSeedHostedService));
                await SembrarMaestrosAsync(sp, ct);
                await SembrarUsuariosAsync(sp, usuarios, ct);
                await SembrarComprasAsync(sp, ct);
                await SembrarFacturacionAsync(sp, ct);
                await SembrarFinanzasAsync(sp, ct);
            }, cancellationToken);
        logger.LogInformation("DEMO: preparación terminada. Los usuarios ausentes requieren iniciar sesión y reiniciar el API.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static Guid Id(string clave) => new(SHA256.HashData(Encoding.UTF8.GetBytes("Millet.DemoSesion/" + clave)).AsSpan(0, 16));

    public static byte[] CrearPdf(string etiqueta)
    {
        // Mismo motor y licencia que QuestPdfOrdenCompraGenerator; solo memoria.
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(d => d.Page(p =>
        {
            p.Margin(40);
            p.Content().Column(c =>
            {
                c.Item().Text("DEMO · DOCUMENTO FICTICIO").Bold().FontSize(20);
                c.Item().Text(etiqueta);
                c.Item().Text("Sesión 12-oct-2026. Sin validez fiscal, comercial ni bancaria.");
            });
        })).GeneratePdf();
    }
}

public sealed record DemoSesionUsuario
{
    public string Correo { get; init; } = "";
    public string Rol { get; init; } = "";
    public string[] Sucursales { get; init; } = [];
}

public sealed class DemoSesionUsuarioValidator : AbstractValidator<DemoSesionUsuario>
{
    public DemoSesionUsuarioValidator()
    {
        RuleFor(u => u.Correo).NotEmpty().EmailAddress();
        RuleFor(u => u.Rol).Must(r => r is "Compras" or "CxP" or "Tesorería" or "Facturación" or "Contabilidad" or "DAF" or "Administrador")
            .WithMessage("Rol DEMO desconocido.");
        RuleFor(u => u.Sucursales).NotEmpty();
        RuleForEach(u => u.Sucursales).Must(s => s is "MID" or "MTY" or "QRO");
        RuleFor(u => u.Sucursales).Must(s => s is not null && s.Distinct().Count() == s.Length)
            .WithMessage("No repita sucursales.");
        RuleFor(u => u.Sucursales).Must(s => s is ["MID"]).When(u => u.Rol == "Compras")
            .WithMessage("El usuario DEMO de Compras debe tener solo MID para la escena 2.");
    }
}
