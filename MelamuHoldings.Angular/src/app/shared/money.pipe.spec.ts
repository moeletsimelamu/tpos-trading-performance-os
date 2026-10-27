import { MoneyPipe } from './money.pipe';

describe('MoneyPipe', () => {
  const pipe = new MoneyPipe();

  it('always shows the currency code with two decimals and grouping', () => {
    expect(pipe.transform(1350, 'USD')).toBe('USD 1,350.00');
    expect(pipe.transform(-300.5, 'ZAR')).toBe('ZAR -300.50');
  });

  it('can mark gains with a plus sign', () => {
    expect(pipe.transform(405, 'ZAR', true)).toBe('ZAR +405.00');
    expect(pipe.transform(-405, 'ZAR', true)).toBe('ZAR -405.00');
  });

  it('shows a dash when the amount or currency is missing', () => {
    expect(pipe.transform(null, 'ZAR')).toBe('—');
    expect(pipe.transform(10, '')).toBe('—');
  });
});
