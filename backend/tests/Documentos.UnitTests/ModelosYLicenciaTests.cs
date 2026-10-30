using QuestPDF.Infrastructure;
using System.Runtime.Loader;

namespace Millet.Documentos.UnitTests;

public sealed class ModelosYLicenciaTests
{
    [Fact]
    public void Should_Copy_MutableInput()
    {
        var domicilio = new List<string> { "DOMICILIO FICTICIO" };
        byte[] logo = [1, 2, 3];
        var empresa = new EmpresaEncabezado("EMPRESA FICTICIA", null, "XAXX010101000", "601", domicilio, logo);
        domicilio[0] = "CAMBIO";
        logo[0] = 9;
        empresa.Logo![1] = 9;
        empresa.DomicilioLineas.Should().Equal("DOMICILIO FICTICIO");
        empresa.Logo.Should().Equal(1, 2, 3);
        var renglones = new List<RenglonTotal> { new("Total", 1, "MXN", true) };
        var totales = new Totales(renglones);
        renglones.Clear();
        totales.Renglones.Should().HaveCount(1);
    }

    [Fact]
    public void Should_Initialize_ConcurrentlyAndIdempotently()
    {
        Parallel.For(0, 100, _ => PdfLicencia.Inicializar());
        PdfLicencia.Inicializar("Community");
        QuestPDF.Settings.License.Should().Be(LicenseType.Community);
        FluentActions.Invoking(() => PdfLicencia.Inicializar("NoValida")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => PdfLicencia.Inicializar("Professional")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_Initialize_Professional_InIsolatedContext()
    {
        // La licencia es global. Un contexto separado permite comprobar Professional
        // sin cambiar la licencia Community de las pruebas que generan las muestras.
        var contexto = new AssemblyLoadContext("LicenciaProfessional", isCollectible: true);
        try
        {
            var quest = contexto.LoadFromAssemblyPath(typeof(QuestPDF.Settings).Assembly.Location);
            var documentos = contexto.LoadFromAssemblyPath(typeof(PdfLicencia).Assembly.Location);
            var inicializar = documentos.GetType(typeof(PdfLicencia).FullName!)!.GetMethod(nameof(PdfLicencia.Inicializar))!;
            inicializar.Invoke(null, ["Professional"]);
            inicializar.Invoke(null, ["Professional"]);
            quest.GetType(typeof(QuestPDF.Settings).FullName!)!.GetProperty(nameof(QuestPDF.Settings.License))!
                .GetValue(null)!.ToString().Should().Be("Professional");
        }
        finally { contexto.Unload(); }
    }
}
