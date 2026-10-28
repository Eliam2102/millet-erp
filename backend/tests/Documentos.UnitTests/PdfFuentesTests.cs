namespace Millet.Documentos.UnitTests;

public sealed class PdfFuentesTests
{
    [Fact]
    public void Should_Embed_AllCarlitoVariants()
    {
        PdfFuentes.Recursos().Select(r => r.Split('.')[^2]).Should().BeEquivalentTo(
            "Carlito-Bold", "Carlito-BoldItalic", "Carlito-Italic", "Carlito-Regular");
    }

    [Fact]
    public void Should_Register_ConcurrentlyAndIdempotently()
    {
        FluentActions.Invoking(() => Parallel.For(0, 50, _ => PdfFuentes.Registrar())).Should().NotThrow();
        FluentActions.Invoking(PdfFuentes.Registrar).Should().NotThrow();
    }
}
