using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Adjuntos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Blob;

namespace Millet.Compartido.UnitTests.Adjuntos;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 31, 18, 0, 0, TimeSpan.Zero);
}

internal sealed class FakeUser : ICurrentUserContext
{
    public Guid? UserId { get; set; } = Guid.NewGuid();
    public string? UserName { get; set; } = "Ana Prueba";
    public string? Email { get; set; } = "ana@millet.test";
}

internal sealed class FakeEmpresa : ICurrentEmpresaContext
{
    public Guid? Current { get; set; } = Guid.NewGuid();
    public bool IsBypassed => false;
    public IDisposable Bypass() => throw new NotSupportedException();
}

internal sealed class FakePermisos : ICurrentUserPermissions
{
    public HashSet<string> Concedidos { get; } = [];

    public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Concedidos.Contains(permiso));
}

internal sealed class FakeAudit : IAuditLogWriter
{
    public List<(string Operacion, string Resumen, Guid? EntidadId, Guid? AggregateRootId)> Registros { get; } = [];

    public Task RegistrarAsync(
        string operacion, string modulo, string entidad, Guid? entidadId, Guid? aggregateRootId,
        string actorNombre, string actorTipo, string? actorEmail, string entidadEtiqueta, string resumen,
        Guid? usuarioId = null, Guid? empresaId = null, string cambios = "{}", string? metadatos = null,
        CancellationToken cancellationToken = default)
    {
        Registros.Add((operacion, resumen, entidadId, aggregateRootId));
        return Task.CompletedTask;
    }
}

internal sealed class FakeBlob : IBlobStoragePort
{
    public Dictionary<string, byte[]> Blobs { get; } = [];
    public bool FallarAlSubir { get; set; }

    public async Task SubirAsync(string clave, Stream contenido, string contentType, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await contenido.CopyToAsync(ms, cancellationToken);
        if (FallarAlSubir) throw new IOException("blob caído");
        Blobs[clave] = ms.ToArray();
    }

    public Task<Stream> ObtenerStreamAsync(string clave, CancellationToken cancellationToken)
        => Blobs.TryGetValue(clave, out var b)
            ? Task.FromResult<Stream>(new MemoryStream(b))
            : throw new FileNotFoundException(clave);

    public Task EliminarAsync(string clave, CancellationToken cancellationToken)
    {
        Blobs.Remove(clave);
        return Task.CompletedTask;
    }
}

/// <summary>Arma el entorno de handlers de adjuntos con fakes y EF InMemory.</summary>
internal sealed class AdjuntosEntorno : IDisposable
{
    public readonly FakeClock Clock = new();
    public readonly FakeUser Usuario = new();
    public readonly FakeEmpresa Empresa = new();
    public readonly FakePermisos Permisos = new();
    public readonly FakeAudit Audit = new();
    public readonly FakeBlob Blob = new();
    public readonly FakeEnlaces Enlaces = new();
    public readonly FallaAlGuardarInterceptor FallaAlGuardar = new();
    public readonly CompartidoDbContext Db;
    public readonly AdjuntoAcceso Acceso;

    public AdjuntosEntorno()
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(FallaAlGuardar)
            .Options;
        Db = new CompartidoDbContext(options, Empresa);
        Acceso = new AdjuntoAcceso(
            [new ProveedorAdjuntoPropietario(Db)], Permisos, Usuario, Empresa, Audit);
    }

    public void Conceder(params string[] permisos)
    {
        foreach (var p in permisos) Permisos.Concedidos.Add(p);
    }

    public void Dispose() => Db.Dispose();
}

internal sealed class FakeEnlaces : IAdjuntoEnlaceTokenService
{
    public AdjuntoEnlaceEmitido Emitir(Guid adjuntoId, Guid usuarioId)
        => new($"{adjuntoId:N}.{usuarioId:N}", new DateTimeOffset(2026, 10, 31, 18, 1, 0, TimeSpan.Zero));

    public AdjuntoEnlaceClaims? Validar(string token)
    {
        var p = token.Split('.');
        return p.Length == 2 && Guid.TryParseExact(p[0], "N", out var a) && Guid.TryParseExact(p[1], "N", out var u)
            ? new AdjuntoEnlaceClaims(a, u)
            : null;
    }
}

/// <summary>Siembra de datos comunes de las pruebas de adjuntos.</summary>
internal static class Siembra
{
    public static readonly Guid TipoCsf = Guid.Parse("00000011-0001-0000-0000-000000000001");
    public static readonly Guid TipoContrato = Guid.Parse("00000011-0001-0000-0000-000000000002");
    public static readonly Guid TipoActa = Guid.Parse("00000011-0001-0000-0000-000000000003");
    public static readonly Guid TipoIdentificacion = Guid.Parse("00000011-0001-0000-0000-000000000004");
    public static readonly Guid TipoDomicilio = Guid.Parse("00000011-0001-0000-0000-000000000005");

    /// <summary>Tipos de proveedor (los mismos ids deterministas del seed de la migración).</summary>
    public static async Task TiposProveedorAsync(CompartidoDbContext db)
    {
        db.AdjuntoTiposDocumento.AddRange(
            new Millet.SharedKernel.Domain.Adjuntos.AdjuntoTipoDocumento(TipoCsf, "proveedor", "constancia_situacion_fiscal", "Constancia de situación fiscal", 1, true, 3, false),
            new Millet.SharedKernel.Domain.Adjuntos.AdjuntoTipoDocumento(TipoContrato, "proveedor", "contrato", "Contrato", 2, true, null, false),
            new Millet.SharedKernel.Domain.Adjuntos.AdjuntoTipoDocumento(TipoActa, "proveedor", "acta_constitutiva", "Acta constitutiva", 3, true, null, true),
            new Millet.SharedKernel.Domain.Adjuntos.AdjuntoTipoDocumento(TipoIdentificacion, "proveedor", "identificacion_representante_legal", "Identificación del representante legal", 4, true, null, false),
            new Millet.SharedKernel.Domain.Adjuntos.AdjuntoTipoDocumento(TipoDomicilio, "proveedor", "comprobante_domicilio", "Comprobante de domicilio", 5, true, 3, false));
        await db.SaveChangesAsync();
    }
}

/// <summary>Simula una caída de la BD al guardar (para probar la compensación del blob).</summary>
internal sealed class FallaAlGuardarInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public bool Activo { get; set; }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
        => Activo ? throw new InvalidOperationException("BD caída") : base.SavingChangesAsync(eventData, result, cancellationToken);
}
