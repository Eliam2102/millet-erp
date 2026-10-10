using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Idempotency;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.UnitTests;

public sealed class P9IdempotencyTests
{
    [Fact]
    public void Cuerpo_del_replay_se_almacena_sin_normalizar_el_json()
    {
        using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql("Host=localhost;Database=p9_modelo_sin_conexion")
            .UseSnakeCaseNamingConvention().Options, new Empresa());

        var tipo = db.Model.FindEntityType(typeof(IdempotencyKey))!
            .FindProperty(nameof(IdempotencyKey.ResponseBody))!.GetColumnType();

        Assert.Equal("text", tipo);
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
