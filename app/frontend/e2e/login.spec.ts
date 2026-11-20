import { expect, expectAccessible, mockSession, test } from './fixtures'

test('anonymous visitors are sent to the GitHub sign-in page', async ({ page }) => {
  await mockSession(page, null)
  await page.goto('/uploads')

  await expect(page).toHaveURL(/\/login\?returnUrl=(%2F|\/)uploads$/)
  const signIn = page.getByRole('link', { name: 'Sign in with GitHub' })
  await expect(signIn).toHaveAttribute('href', '/api/auth/login?returnUrl=%2Fuploads')
  await expectAccessible(page)
})

test('denied accounts see an explanation', async ({ page }) => {
  await mockSession(page, null)
  await page.goto('/login?error=access_denied')
  await expect(page.getByRole('alert')).toContainText('not allowed')
})

test('a sign-in that did not complete is not reported as a denied account', async ({ page }) => {
  await mockSession(page, null)
  await page.goto('/login?error=signin_failed')
  await expect(page.getByRole('alert')).toContainText('did not complete')
  await expect(page.getByRole('alert')).not.toContainText('not allowed')
  await expectAccessible(page)
})

test('an unreachable server is explained, not treated as signed out, and retried', async ({
  page,
}) => {
  // Session checks fail (offline or a server error) until the server is back.
  let serverDown = true
  await page.route('**/api/auth/session', (route) =>
    serverDown
      ? route.fulfill({ status: 503, json: { title: 'Service Unavailable' } })
      : route.fulfill({
          json: {
            authenticated: true,
            user: { gitHubId: 1001, login: 'octo', name: 'Octo', avatarUrl: null, role: 'Admin' },
            csrfToken: 'test-csrf',
          },
        }),
  )
  await page.route('**/api/uploads**', (route) => route.fulfill({ json: [] }))
  await page.goto('/uploads')

  await expect(page).toHaveURL(/\/login\?.*error=unavailable/)
  await expect(page.getByRole('alert')).toContainText('could not be reached')
  await expectAccessible(page)

  serverDown = false
  await page.getByRole('button', { name: 'Try again' }).click()
  await expect(page).toHaveURL(/\/uploads$/)
})
