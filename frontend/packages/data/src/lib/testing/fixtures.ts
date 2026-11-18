import type { Account } from '../models/models';

/** For the specs, here and in the packages that use this one: an account as the API describes one. */
export const TEST_ACCOUNT: Account = {
  id: '0b0f6f3e-6a59-4c8c-9a55-2d7f3f0d6c11',
  email: 'first@example.org',
  display_name: 'First',
  is_admin: true,
};
