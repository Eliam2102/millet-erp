import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Tabs, TabsContent, TabsList, TabsTrigger } from './tabs';

function Demo() {
  return (
    <Tabs defaultValue="a">
      <TabsList aria-label="demo">
        <TabsTrigger value="a">Uno</TabsTrigger>
        <TabsTrigger value="b" count={3}>
          Dos
        </TabsTrigger>
      </TabsList>
      <TabsContent value="a" forceMount>
        <input aria-label="campo" />
      </TabsContent>
      <TabsContent value="b">Panel B</TabsContent>
    </Tabs>
  );
}

describe('ui/tabs', () => {
  it('flechas mueven la selección y el conteo se muestra', async () => {
    render(<Demo />);
    const uno = screen.getByRole('tab', { name: 'Uno' });
    uno.focus();
    fireEvent.keyDown(uno, { key: 'ArrowRight' });
    await waitFor(() =>
      expect(screen.getByRole('tab', { name: /Dos/ })).toHaveAttribute('data-state', 'active'),
    );
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  it('forceMount conserva lo escrito al cambiar de pestaña y volver', () => {
    render(<Demo />);
    fireEvent.change(screen.getByLabelText('campo'), { target: { value: 'hola' } });
    fireEvent.mouseDown(screen.getByRole('tab', { name: /Dos/ }), { button: 0 });
    fireEvent.mouseDown(screen.getByRole('tab', { name: 'Uno' }), { button: 0 });
    expect(screen.getByLabelText('campo')).toHaveValue('hola');
  });
});
