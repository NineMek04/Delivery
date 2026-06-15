import { DispatchScanStarted, getDispatchScanKey } from './tracking-signalr.service';

describe('getDispatchScanKey', () => {
  it('uses order id and dispatch attempt to identify duplicate scan events', () => {
    const scan = {
      order: { id: 'order-123' },
      dispatchAttempt: 2
    } as DispatchScanStarted;

    expect(getDispatchScanKey(scan)).toBe('order-123:2');
  });

  it('keeps different backend retry attempts distinct', () => {
    const first = {
      order: { id: 'order-123' },
      dispatchAttempt: 1
    } as DispatchScanStarted;
    const retry = {
      order: { id: 'order-123' },
      dispatchAttempt: 2
    } as DispatchScanStarted;

    expect(getDispatchScanKey(first)).not.toBe(getDispatchScanKey(retry));
  });
});
