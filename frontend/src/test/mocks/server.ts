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
export const mswServer = setupServer();
