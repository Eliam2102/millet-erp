/// <reference types="vitest" />
import { defineConfig } from 'vitest/config';
import path from 'node:path';

/**
 * Configuración de Vitest del frontend (ADR-0016).
 *
 * <para>Convención: tests colocados al lado del source como
 * <c>*.test.ts(x)</c>. Vitest los autodescubre.</para>
 *
 * <para><b>Coverage</b> (UF7-PR1, doc 05 §13.1): provider v8, reporters
 * <c>text</c> + <c>html</c>. Threshold del 70% sobre <c>features/compras/</c>
 * — el módulo más maduro y el que va a release v1. El resto del repo
 * no tiene threshold gateado para evitar bloquear PRs ajenos al módulo
 * mientras crece la cobertura general.</para>
 */
export default defineConfig({
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: ['node_modules', 'dist', '.git'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,tsx}'],
      exclude: [
        'src/**/*.{test,spec}.{ts,tsx}',
        'src/**/__tests__/**',
        'src/**/__snapshots__/**',
        'src/**/*.d.ts',
        'src/test/**',
        'src/routeTree.gen.ts',
        'src/main.tsx',
        'src/components/ui/**', // shadcn primitives — coverage no aplica
      ],
      // Threshold gateado solo en el módulo más maduro (Compras
      // Requisiciones). Otros módulos pueden agregar su threshold
      // cuando alcancen madurez similar.
      //
      // <para>UF7-PR1 sube de 46% → 70%+ en lines/statements (cumple
      // breakdown). functions/branches quedan en 60% — ejercitar
      // todas las branches/callbacks restantes requiere tests
      // interactivos pesados con Radix primitives en jsdom (Select /
      // Dialog flujos completos), ROI bajo. <b>UF7-PR3 polish</b>
      // los subirá a 70% cuando entren los tests con Playwright en
      // E2E (UF7-PR2) que ejercitan flujos reales sin las
      // limitaciones de jsdom.</para>
      thresholds: {
        'src/features/compras/**': {
          lines: 70,
          statements: 70,
          functions: 60,
          branches: 60,
        },
      },
    },
  },
});
