import { isPublicAuthRequest } from './api-error';

describe('isPublicAuthRequest', () => {
  it('treats the session probe as an auth lifecycle request', () => {
    expect(isPublicAuthRequest('/api/v1/auth/session')).toBeTrue();
  });

  it('does not classify protected business endpoints as public auth requests', () => {
    expect(isPublicAuthRequest('/api/v1/orders')).toBeFalse();
  });
});
