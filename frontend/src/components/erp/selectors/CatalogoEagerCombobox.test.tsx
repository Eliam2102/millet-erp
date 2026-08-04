import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * Invariante de PR-A: los combos de catálogo (Condiciones de pago / Uso
 * principal del alta de OC) enrutados por <c>CatalogoEagerCombobox</c> NO
 * desbordan la pantalla y SÍ filtran, aunque el catálogo crezca. Fixture: las
 * 27 filas reales del catálogo de condiciones en dev = 7 legítimas + 20 filas
 * "Test 90 días renombrado" que los tests de integración dejan en millet_dev.
 *
 * <para>Verificación por mutación (manual): quitar el cap <c>max-h-[300px]</c>
 * del <c>CommandList</c> (components/ui/command.tsx) hace fallar el test (a);
 * quitar el <c>CommandInput</c> de <c>CatalogoEagerCombobox</c> hace fallar el
 * test (b).</para>
 */

interface CatalogoItem {
  id: string;
  clave: string;
  nombre: string;
}

const REALES: CatalogoItem[] = [
  { id: 'cp-contado', clave: 'CONTADO', nombre: 'Contado' },
  { id: 'cp-15', clave: '15D', nombre: '15 días' },
  { id: 'cp-30', clave: '30D', nombre: '30 días' },
  { id: 'cp-45', clave: '45D', nombre: '45 días' },
  { id: 'cp-60', clave: '60D', nombre: '60 días' },
  { id: 'cp-90', clave: '90D', nombre: '90 días' },
  { id: 'cp-120', clave: '120D', nombre: '120 días' },
];

// Las 20 filas basura que la suite Api.IntegrationTests deja en millet_dev
// (claves aleatorias CP-xxxxxx, mismo nombre). Inflan la lista sin ser el
// defecto — el defecto es el componente, no el dato.
const BASURA_TEST: CatalogoItem[] = Array.from({ length: 20 }, (_, i) => ({
  id: `cp-test-${i}`,
  clave: `CP-${String(i + 1).padStart(6, '0')}`,
  nombre: 'Test 90 días renombrado',
}));

const ITEMS: CatalogoItem[] = [...REALES, ...BASURA_TEST]; // 27

function renderCombo() {
  return render(
    <CatalogoEagerCombobox
      items={ITEMS}
      value={null}
      onChange={() => {}}
      itemToLabel={(c) => `${c.clave} ${c.nombre}`}
      renderItem={(c) => (
        <div className="min-w-0 flex-1">
          <span className="font-mono text-xs">{c.clave}</span>
          <p className="truncate text-sm">{c.nombre}</p>
        </div>
      )}
      searchPlaceholder="Buscar condiciones…"
      ariaLabel="Seleccionar condiciones de pago"
    />,
  );
}

function abrir() {
  fireEvent.click(
    screen.getByRole('combobox', { name: /Seleccionar condiciones de pago/i }),
  );
}

describe('<CatalogoEagerCombobox> — invariante lista larga (PR-A)', () => {
  it('(a) la lista larga se renderiza contenida en el CommandList con cap de altura + scroll (no desborda)', () => {
    renderCombo();
    abrir();

    // Las 27 filas están en el DOM como opciones seleccionables...
    expect(screen.getAllByRole('option')).toHaveLength(27);

    // ...contenidas en el CommandList, que impone el cap de altura y el
    // scroll. Este es el invariante que evita el desbordamiento (bug PR-A):
    // si se quita max-h-[300px]/overflow-y-auto del CommandList, esto truena.
    const lista = document.querySelector('[cmdk-list]');
    expect(lista).not.toBeNull();
    expect(lista!.className).toContain('max-h-[300px]');
    expect(lista!.className).toContain('overflow-y-auto');
  });

  it('(b) la búsqueda filtra la lista — no es una lista plana', async () => {
    renderCombo();
    abrir();
    expect(screen.getAllByRole('option')).toHaveLength(27);

    // El CommandInput filtra client-side: "Contado" descarta las 20 filas
    // "Test …" y deja solo la real. Si se quita el CommandInput, no hay caja
    // que encontrar por placeholder → truena aquí.
    fireEvent.change(screen.getByPlaceholderText('Buscar condiciones…'), {
      target: { value: 'Contado' },
    });

    await waitFor(() => {
      const visibles = screen.getAllByRole('option');
      expect(visibles).toHaveLength(1);
      expect(visibles[0]).toHaveTextContent('Contado');
    });
  });
});
