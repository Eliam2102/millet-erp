import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';

/**
 * Server MSW compartido para todos los tests del frontend. Se inicia
 * en <c>src/test/setup.ts</c> con <c>onUnhandledRequest: 'error'</c>
 * para que cualquier request sin handler falle ruidosamente y no se
 * cuele con respuestas de red real.
 *
 * <para>Cada test (o suite) registra sus handlers via
 * <c>server.use(...)</c>. Los handlers se resetean entre tests para
 * evitar contaminación cruzada (manejado en setup.ts).</para>
 */
export const mswServer = setupServer(
  http.get('*/api/v1/datos-maestros/clasificaciones-cliente', () => HttpResponse.json([{ id: '00000012-0001-0000-0000-000000000001', nombre: 'Constructora', descripcion: 'Por validar con Millet', activa: true, version: 1 }])),
  http.get('*/api/v1/datos-maestros/productos-aw/fiscal/:tipo/:id/historial', () => HttpResponse.json([])),
);
