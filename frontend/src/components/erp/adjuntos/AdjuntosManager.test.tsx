import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  AdjuntosManager,
  type AdjuntoItem,
} from '@/components/erp/adjuntos/AdjuntosManager';

/**
 * Test clave de Camino (a): con <c>resolverContenidoUrl</c>, el manager
 * baja el contenido por el endpoint autenticado y usa el object URL
 * (blob:) para el preview y la descarga. El camino file:// queda CERRADO:
 * el blobUrl crudo (file:// en dev) NUNCA llega al DOM. ADR-0024.
 */
describe('<AdjuntosManager> — contenido vía endpoint (Camino a)', () => {
  const adjunto: AdjuntoItem = {
    id: 'adj-1',
    tipoDocumentoId: 'tipo-x',
    nombreArchivo: 'prueba.pdf',
    // blobUrl crudo del stub local: NO debe renderizarse en el browser.
    blobUrl: 'file://C:/Users/x/AppData/Local/Temp/millet-oc-blobs/adj-1.pdf',
    contentType: 'application/pdf',
    tamanoBytes: 1234,
    fechaCarga: '2026-06-08T12:00:00Z',
    usuarioCargaId: 'u-1',
  };

  beforeAll(() => {
    // jsdom no implementa createObjectURL/revokeObjectURL.
    URL.createObjectURL = vi.fn(() => 'blob:mock-url');
    URL.revokeObjectURL = vi.fn();
  });

  beforeEach(() => {
    mswServer.use(
      http.get(
        '*/api/v1/compras/ordenes/:ocId/adjuntos/:adjuntoId/contenido',
        () =>
          new HttpResponse(new Uint8Array([0x25, 0x50, 0x44, 0x46]), {
            headers: { 'Content-Type': 'application/pdf' },
          }),
      ),
    );
  });

  it('usa el object URL del endpoint para descargar; nunca file://', async () => {
    const { container } = render(
      <AdjuntosManager
        adjuntos={[adjunto]}
        tipos={[]}
        onUpload={async () => {}}
        canUpload={false}
        canRemove={false}
        resolverContenidoUrl={(a) =>
          `/api/v1/compras/ordenes/oc-1/adjuntos/${a.id}/contenido`
        }
      />,
      { wrapper: createQueryWrapper() },
    );

    // Cuando el contenido resuelve, el nombre se vuelve un <a download> que
    // apunta al object URL — no al blobUrl crudo.
    const link = await screen.findByRole('link', { name: /prueba\.pdf/i });
    expect(link).toHaveAttribute('href', 'blob:mock-url');
    expect(link).toHaveAttribute('download', 'prueba.pdf');

    // El camino file:// queda cerrado: no aparece en ningún href/src.
    await waitFor(() => {
      expect(container.innerHTML).not.toContain('file://');
    });
  });

  it('sin resolver (legacy): cae al blobUrl crudo', () => {
    const { container } = render(
      <AdjuntosManager
        adjuntos={[adjunto]}
        tipos={[]}
        onUpload={async () => {}}
        canUpload={false}
        canRemove={false}
      />,
      { wrapper: createQueryWrapper() },
    );
    // Sin resolver el manager conserva el comportamiento previo (blobUrl
    // directo). En prod ese blobUrl es https://; en dev file:// — por eso
    // OC SIEMPRE inyecta el resolver. Este test documenta el fallback.
    const link = container.querySelector('a[download]');
    expect(link?.getAttribute('href')).toBe(adjunto.blobUrl);
  });
});
