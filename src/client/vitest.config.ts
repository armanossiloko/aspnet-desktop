import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['test/**/*.test.ts'],
    // Angular's partially compiled packages need the JIT compiler outside an Angular build.
    setupFiles: ['test/setup.ts'],
  },
});
