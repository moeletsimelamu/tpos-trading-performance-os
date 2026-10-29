import { Pipe, PipeTransform } from '@angular/core';

const formatter = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// Every monetary figure shows its currency code: "ZAR 1,350.00", "USD -300.00"
@Pipe({ name: 'money', standalone: true })
export class MoneyPipe implements PipeTransform {
  transform(amount: number | null | undefined, currency: string | null | undefined, signed = false): string {
    if (amount === null || amount === undefined || !currency) return '—';
    const sign = signed && amount > 0 ? '+' : '';
    return `${currency} ${sign}${formatter.format(amount)}`;
  }
}
