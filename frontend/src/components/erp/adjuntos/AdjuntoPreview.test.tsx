import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import { AdjuntoPreview } from '@/components/erp/adjuntos/AdjuntoPreview';

/**
 * Bloquea la regresión del camino file://: el preview NUNCA debe meter un
 * blobUrl crudo file:// en un &lt;embed&gt;/&lt;img&gt; (el browser no lo
 * navega desde una página http). Solo renderiza inline para http(s) o un
 * object URL blob:. ADR-0024.
 */
describe('<AdjuntoPreview>', () => {
  it('file://: cae al ícono genérico (no embebe la URL cruda)', () => {
    const { container } = render(
      <AdjuntoPreview
        blobUrl="file://C:/Users/x/AppData/Local/Temp/millet-oc-blobs/a.pdf"
        contentType="application/pdf"
        nombreArchivo="a.pdf"
      />,
    );
    expect(
      container.querySelector('[data-component="adjunto-preview-icon"]'),
    ).not.toBeNull();
    expect(
      container.querySelector('[data-component="adjunto-preview-pdf"]'),
    ).toBeNull();
    expect(container.innerHTML).not.toContain('file://');
  });

  it('vacío (cargando): cae al ícono, sin embed', () => {
    const { container } = render(
      <AdjuntoPreview blobUrl="" contentType="application/pdf" nombreArchivo="a.pdf" />,
    );
    expect(
      container.querySelector('[data-component="adjunto-preview-icon"]'),
    ).not.toBeNull();
  });

  it('object URL blob: PDF → embed inline', () => {
    const { container } = render(
      <AdjuntoPreview
        blobUrl="blob:http://localhost/abc"
        contentType="application/pdf"
        nombreArchivo="a.pdf"
      />,
    );
    const embed = container.querySelector(
      '[data-component="adjunto-preview-pdf"] embed',
    );
    expect(embed).not.toBeNull();
    expect(embed?.getAttribute('src')).toContain('blob:http://localhost/abc');
  });

  it('http imagen → img inline', () => {
    const { container } = render(
      <AdjuntoPreview
        blobUrl="https://blob.example/x.png"
        contentType="image/png"
        nombreArchivo="x.png"
      />,
    );
    const img = container.querySelector(
      '[data-component="adjunto-preview-img"]',
    );
    expect(img).not.toBeNull();
    expect(img?.getAttribute('src')).toBe('https://blob.example/x.png');
  });
});
