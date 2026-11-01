using FluentAssertions;
using Millet.Administracion.Application.Series;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Xunit;

namespace Millet.Compartido.UnitTests.Administracion;

/// <summary>86e3wjt5r: el worker de NC de A+W reserva folio en Bypass y sin JWT; el usuario con JWT sigue validado.</summary>
public sealed class SerieSucursalScopeEmpresaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static SerieSucursalScope Scope(Guid? actual, bool bypass) => new(null!, new EmpresaCtx(actual, bypass), null!, null!);

    [Fact]
    public void Proceso_del_sistema_en_bypass_sin_jwt_puede_reservar_con_su_empresa() =>
        Scope(null, bypass: true).Invoking(s => s.VerificarEmpresa(Empresa)).Should().NotThrow();

    [Fact]
    public void Sin_empresa_y_sin_bypass_se_rechaza() =>
        Scope(null, bypass: false).Invoking(s => s.VerificarEmpresa(Empresa)).Should().Throw<ForbiddenException>();

    [Fact]
    public void Usuario_de_otra_empresa_se_rechaza_aunque_haya_bypass() =>
        Scope(Guid.NewGuid(), bypass: true).Invoking(s => s.VerificarEmpresa(Empresa)).Should().Throw<CrossTenantViolationException>();

    private sealed class EmpresaCtx(Guid? actual, bool bypass) : ICurrentEmpresaContext
    {
        public Guid? Current => actual;
        public bool IsBypassed => bypass;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
