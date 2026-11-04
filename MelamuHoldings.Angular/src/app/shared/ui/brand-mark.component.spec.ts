import { TestBed } from '@angular/core/testing';
import { BrandMarkComponent, BrandNameComponent } from './brand-mark.component';

describe('Brand artwork', () => {
  it('draws the mark with the M, two candlesticks and the bar that makes them the H', () => {
    const fixture = TestBed.createComponent(BrandMarkComponent);
    fixture.detectChanges();
    const svg = fixture.nativeElement.querySelector('svg') as SVGElement;
    expect(svg.querySelectorAll('path').length).toBe(1);
    expect(svg.querySelectorAll('rect').length).toBe(5);
    expect(fixture.nativeElement.getAttribute('aria-hidden')).toBe('true');
  });

  it('gives each mark its own gradient, so a hidden copy cannot leave a visible one unfilled', () => {
    const one = TestBed.createComponent(BrandMarkComponent);
    const two = TestBed.createComponent(BrandMarkComponent);
    one.detectChanges();
    two.detectChanges();

    const id = (f: typeof one) => (f.nativeElement.querySelector('linearGradient') as Element).id;
    expect(id(one)).not.toBe(id(two));
    expect((one.nativeElement.querySelector('path') as Element).getAttribute('fill')).toBe(`url(#${id(one)})`);
  });

  it('uses a stronger silver on white than on the navy sidebar', () => {
    const dark = TestBed.createComponent(BrandMarkComponent);
    dark.componentRef.setInput('tone', 'on-dark');
    const light = TestBed.createComponent(BrandMarkComponent);
    dark.detectChanges();
    light.detectChanges();

    const stop = (f: typeof dark) => (f.nativeElement.querySelector('stop') as Element).getAttribute('stop-color');
    expect(stop(dark)).not.toBe(stop(light));
  });

  it('labels the drawn name, and draws it on one line or two', () => {
    const fixture = TestBed.createComponent(BrandNameComponent);
    fixture.detectChanges();
    const svg = () => fixture.nativeElement.querySelector('svg') as SVGElement;
    const oneLine = svg().getAttribute('viewBox');
    expect(svg().getAttribute('aria-label')).toBe('Melamu Holdings');
    expect(svg().querySelector('title')!.textContent).toBe('Melamu Holdings');

    fixture.componentRef.setInput('layout', 'stacked');
    fixture.detectChanges();
    expect(svg().getAttribute('viewBox')).not.toBe(oneLine);
  });
});
