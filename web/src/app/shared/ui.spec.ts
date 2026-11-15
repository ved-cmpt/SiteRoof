import { TestBed } from '@angular/core/testing';
import { StatusChartComponent } from './charts';
import { AgoPipe, DayPipe, StatusChipComponent } from './ui';
import { newId } from '../core/uuid';

describe('AgoPipe', () => {
  const pipe = new AgoPipe();
  const now = Date.parse('2026-11-15T12:00:00Z');

  it('formats past and future relative times', () => {
    expect(pipe.transform('2026-11-15T11:59:50Z', now)).toBe('just now');
    expect(pipe.transform('2026-11-15T11:30:00Z', now)).toBe('30 min ago');
    expect(pipe.transform('2026-11-15T09:00:00Z', now)).toBe('3 h ago');
    expect(pipe.transform('2026-11-15T12:02:00Z', now)).toBe('in 2 min');
    expect(pipe.transform('2026-11-11T12:00:00Z', now)).toBe('4 days ago');
    expect(pipe.transform(null, now)).toBe('—');
  });
});

describe('DayPipe', () => {
  it('does not shift date-only values across time zones', () => {
    expect(new DayPipe().transform('2026-11-07')).toContain('7');
  });
});

describe('newId', () => {
  it('returns distinct RFC 4122 v4 ids', () => {
    const a = newId();
    expect(a).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(newId()).not.toBe(a);
  });
});

describe('StatusChipComponent', () => {
  it('renders a readable label, not just a colour', async () => {
    const fixture = TestBed.createComponent(StatusChipComponent);
    fixture.componentRef.setInput('status', 'InProgress');
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('In progress');
  });
});

describe('StatusChartComponent', () => {
  it('draws one bar per project with totals and offers a table view', async () => {
    const fixture = TestBed.createComponent(StatusChartComponent);
    fixture.componentRef.setInput('rows', [
      { id: 'a', label: 'RBL', counts: [{ status: 'Open', count: 6 }, { status: 'Blocked', count: 2 }, { status: 'Closed', count: 4 }] },
      { id: 'b', label: 'MCC', counts: [{ status: 'Closed', count: 6 }] },
    ]);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.row').length).toBe(2);
    expect([...el.querySelectorAll('.total')].map((t) => t.textContent?.trim())).toEqual(['12', '6']);
    expect(el.querySelector('[role="img"]')?.getAttribute('aria-label')).toContain('RBL: 6 Open');

    (el.querySelector('.toggle') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(el.querySelectorAll('tbody tr').length).toBe(2);
  });
});
