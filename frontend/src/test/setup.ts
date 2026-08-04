import '@testing-library/jest-dom/vitest';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { mswServer } from '@/test/mocks/server';

/**
 * Polyfills de jsdom para componentes basados en <c>cmdk</c>/Radix
 * (ej. <c>&lt;ArticuloSelector/&gt;</c>): jsdom no implementa
 * <c>ResizeObserver</c> ni <c>scrollIntoView</c>, que esos componentes
 * invocan al abrir su popover. Stubs no-op suficientes para tests.
 */
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}
if (typeof Element !== 'undefined' && !Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {};
}

/**
 * Setup global de Vitest. Registra:
 *
 * - Matchers de <c>@testing-library/jest-dom</c> sobre <c>expect</c>.
 * - Server MSW compartido: arranca antes de la suite, resetea
 *   handlers entre tests, y se cierra al terminar. Cualquier request
 *   sin handler explícito **lanza error** (<c>onUnhandledRequest:
 *   'error'</c>) — opcional pero detecta tests que olvidan mockear.
 */
beforeAll(() => {
  // 'warn' (no 'error'): queries en flight de selectores que disparan
  // al montar pueden caer fuera del scope del test cuando otro file
  // cierra sus handlers en paralelo. Falsos positivos. El warn sigue
  // siendo visible en consola para diagnóstico.
  mswServer.listen({ onUnhandledRequest: 'warn' });
});

afterEach(() => {
  mswServer.resetHandlers();
});

afterAll(() => {
  mswServer.close();
});
