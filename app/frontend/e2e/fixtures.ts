import AxeBuilder from '@axe-core/playwright'
import { expect, test as base, type Page } from '@playwright/test'

/** A 1x1 PNG served for thumbnail/preview requests. */
export const pixelPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
)

export const admin = {
  gitHubId: 1,
  login: 'octo',
  name: 'Octo Cat',
  avatarUrl: null,
  role: 'Admin',
}

export async function mockSession(page: Page, user: typeof admin | null): Promise<void> {
  await page.route('**/api/auth/session', (route) =>
    route.fulfill({
      json: user
        ? { authenticated: true, user, csrfToken: 'test-csrf' }
        : { authenticated: false, user: null, csrfToken: null },
    }),
  )
}

/** Fails the test on any CSP violation or uncaught page error. */
export const test = base.extend<{ guard: void }>({
  guard: [
    async ({ page }, use) => {
      const problems: string[] = []
      page.on('console', (message) => {
        if (message.type() === 'error' && /Content Security Policy/i.test(message.text()))
          problems.push(message.text())
      })
      page.on('pageerror', (error) => problems.push(error.message))
      await use()
      expect(problems, problems.join('\n')).toEqual([])
    },
    { auto: true },
  ],
})

/** Runs axe-core against WCAG 2.1 A/AA rules and fails on any violation. */
export async function expectAccessible(page: Page): Promise<void> {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()
  const report = results.violations.flatMap((v) =>
    v.nodes.map(
      (n) => `${v.id}: ${n.target.join(' ')} -> ${n.failureSummary?.replace(/\s+/g, ' ')}`,
    ),
  )
  expect(report).toEqual([])
}

export { expect }
