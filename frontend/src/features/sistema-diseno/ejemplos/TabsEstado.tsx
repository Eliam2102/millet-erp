import { TabsEstado } from '@/components/erp/data/TabsEstado';
import { useState } from 'react';

export default function Ejemplo() {
  const [valor, setValor] = useState<number>();
  return (
    <TabsEstado
      opciones={[
        { valor: 1, label: 'Capturada' },
        { valor: 2, label: 'Autorizada' },
      ]}
      valor={valor}
      onChange={setValor}
      conteos={{ 1: 10, 2: 0 }}
    />
  );
}
