import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BuscadorInteligente } from '@/components/erp/selectors/BuscadorInteligente';

interface Item {
  id: string;
  clave: string;
  nombre: string;
}

const ITEMS: Item[] = [
  { id: 'a', clave: 'A-01', nombre: 'Papelería' },
  { id: 'b', clave: 'B-02', nombre: 'Químicos' },
];

type Props = Partial<React.ComponentProps<typeof BuscadorInteligente<Item>>>;

function renderBuscador(props: Props = {}) {
  const onChange = props.onChange ?? vi.fn();
  const onTerminoChange = props.onTerminoChange ?? vi.fn();
  render(
    <BuscadorInteligente<Item>
      value={null}
      items={ITEMS}
      itemToLabel={(i) => `${i.clave} · ${i.nombre}`}
      renderItem={(i) => <span>{i.nombre}</span>}
      placeholder="Buscar cosa…"
      ariaLabel="Seleccionar cosa"
      searchAriaLabel="Buscar cosa"
      searchPlaceholder="Buscar por clave o nombre…"
      emptyText="Empieza a escribir."
      noMatchText={(t) => `Nada coincide con "${t}".`}
      {...props}
      onChange={onChange}
      onTerminoChange={onTerminoChange}
    />,
  );
  fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar cosa' }));
  return { onChange, onTerminoChange };
}

describe('<BuscadorInteligente>', () => {
  it('tiene una sola caja de búsqueda', () => {
    renderBuscador();
    expect(screen.getAllByRole('textbox')).toHaveLength(1);
    expect(screen.getByRole('textbox', { name: 'Buscar cosa' })).toHaveAttribute(
      'placeholder',
      'Buscar por clave o nombre…',
    );
  });

  it('avisa el término con debounce de 300 ms y con trim, una sola vez', () => {
    vi.useFakeTimers();
    try {
      const { onTerminoChange } = renderBuscador();
      const caja = screen.getByRole('textbox', { name: 'Buscar cosa' });
      fireEvent.change(caja, { target: { value: 'pap' } });
      fireEvent.change(caja, { target: { value: ' papel ' } });
      act(() => vi.advanceTimersByTime(299));
      expect(onTerminoChange).not.toHaveBeenCalledWith('papel');
      act(() => vi.advanceTimersByTime(1));
      expect(onTerminoChange).toHaveBeenLastCalledWith('papel');
      expect(onTerminoChange).not.toHaveBeenCalledWith('pap');
    } finally {
      vi.useRealTimers();
    }
  });

  it('muestra el estado cargando', () => {
    renderBuscador({ cargando: true, items: [] });
    expect(screen.getByRole('status')).toHaveTextContent('Buscando…');
  });

  it('muestra el error del catálogo con role=alert', () => {
    renderBuscador({ error: new Error('boom'), items: [] });
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('sin término y sin items muestra el texto vacío; con término, el de sin coincidencias', async () => {
    renderBuscador({ items: [] });
    expect(screen.getByText('Empieza a escribir.')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('textbox', { name: 'Buscar cosa' }), {
      target: { value: 'zzz' },
    });
    expect(await screen.findByText('Nada coincide con "zzz".')).toBeInTheDocument();
  });

  it('se navega con flechas y se elige con Enter', async () => {
    const { onChange } = renderBuscador();
    const caja = screen.getByRole('textbox', { name: 'Buscar cosa' });
    fireEvent.keyDown(caja, { key: 'ArrowDown' });
    fireEvent.keyDown(caja, { key: 'Enter' });
    await waitFor(() => expect(onChange).toHaveBeenCalledWith(ITEMS[1]));
  });

  it('tras elegir conserva la etiqueta aunque la lista se recargue sin el item', () => {
    function Controlado() {
      const [v, setV] = useState<string | null>(null);
      const [items, setItems] = useState(ITEMS);
      return (
        <BuscadorInteligente<Item>
          value={v}
          onChange={(i) => {
            setV(i?.id ?? null);
            setItems([]);
          }}
          onTerminoChange={() => {}}
          items={items}
          itemToLabel={(i) => `${i.clave} · ${i.nombre}`}
          renderItem={(i) => <span>{i.nombre}</span>}
          placeholder="Buscar cosa…"
          ariaLabel="Seleccionar cosa"
          searchAriaLabel="Buscar cosa"
          searchPlaceholder="Buscar…"
          emptyText="Vacío."
          noMatchText={() => 'Nada.'}
        />
      );
    }
    render(<Controlado />);
    fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar cosa' }));
    fireEvent.click(screen.getByText('Químicos'));
    expect(screen.getByRole('combobox', { name: 'Seleccionar cosa' })).toHaveTextContent(
      'B-02 · Químicos',
    );
  });

  it('clic sobre el ya elegido lo deselecciona', () => {
    const { onChange } = renderBuscador({ value: 'a' });
    fireEvent.click(screen.getByText('Papelería'));
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('value fuera de la lista muestra valueLabel en el trigger', () => {
    render(
      <BuscadorInteligente<Item>
        value="zz"
        valueLabel="ZZ · Fuera de página"
        onChange={() => {}}
        onTerminoChange={() => {}}
        items={ITEMS}
        itemToLabel={(i) => i.clave}
        renderItem={(i) => i.nombre}
        placeholder="Buscar cosa…"
        ariaLabel="Seleccionar cosa"
        searchAriaLabel="Buscar cosa"
        searchPlaceholder="Buscar…"
        emptyText="Vacío."
        noMatchText={() => 'Nada.'}
      />,
    );
    expect(screen.getByText('ZZ · Fuera de página')).toBeInTheDocument();
  });
});
