import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';

export default function Ejemplo() {
  return (
    <div className="flex gap-4">
      <MoneyDisplay amount={1234.56} currency="MXN" strong />
      <MoneyDisplay money={null} />
      <MoneyDisplay money={{ amount: 0, currency: 'USD' }} />
    </div>
  );
}
