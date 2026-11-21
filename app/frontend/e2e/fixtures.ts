import AxeBuilder from '@axe-core/playwright'
import { expect, test as base, type Page } from '@playwright/test'

/** A 1x1 PNG served for thumbnail/preview requests. */
export const pixelPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
)

export const admin = {
  id: 1,
  login: 'octo',
  name: 'Octo Cat',
  avatarUrl: null,
  role: 'Admin',
  provider: 'github',
}

/** Answers session checks; signed-out visitors get a CSRF token too (opening an invite is a POST). */
export async function mockSession(
  page: Page,
  user: typeof admin | null,
  options: { googleSignIn?: boolean } = {},
): Promise<void> {
  const googleSignIn = options.googleSignIn ?? false
  await page.route('**/api/auth/session', (route) =>
    route.fulfill({
      json: user
        ? { authenticated: true, user, csrfToken: 'test-csrf', googleSignIn }
        : { authenticated: false, user: null, csrfToken: 'anonymous-csrf', googleSignIn },
    }),
  )
}

/**
 * Fails the test on any CSP violation or uncaught page error. The gallery's timeline and "On this day" answer
 * empty unless a test mocks them itself (routes registered later win).
 */
export const test = base.extend<{ guard: void; galleryExtras: void }>({
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
  galleryExtras: [
    async ({ page }, use) => {
      await page.route(/\/api\/photos\/(timeline|on-this-day)\?/, (route) =>
        route.fulfill({ json: [] }),
      )
      await use()
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
