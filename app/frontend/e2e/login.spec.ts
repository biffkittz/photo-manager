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
